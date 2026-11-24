using System.Text.Json.Nodes;
using AncosSantiye.Adapters;

namespace AncosSantiye.Tests;

public sealed class HookInstallerTests : IDisposable
{
    private const string Url = "http://127.0.0.1:5170";
    private const string HookUrl = Url + "/hooks/claude";
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 20, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "santiye-hooks-" + Guid.NewGuid());

    private string Settings => Path.Combine(_dir, ".claude", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private void Write(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Settings)!);
        File.WriteAllText(Settings, json);
    }

    private JsonObject Read() => (JsonObject)JsonNode.Parse(File.ReadAllText(Settings))!;

    private static int OurHooks(JsonObject root) =>
        (root["hooks"] as JsonObject ?? [])
            .SelectMany(kv => kv.Value!.AsArray())
            .SelectMany(g => g!["hooks"]!.AsArray())
            .Count(h => (string?)h!["url"] == HookUrl);

    private const string UserSettings = """
        {
          "permissions": { "deny": ["Bash(git push:*)"] },
          "hooks": {
            "PreToolUse": [ { "matcher": "Bash", "hooks": [ { "type": "command", "command": "my-guard.sh" } ] } ]
          },
          "model": "opus"
        }
        """;

    [Fact]
    public void Install_NewFile_AddsEveryHookAndTheStatusLine()
    {
        var result = HookInstaller.Install(Settings, Url, Now);
        var root = Read();

        Assert.True(result.Changed);
        Assert.Null(result.BackupPath);
        Assert.Equal(ClaudeHookMapper.Kinds.Count, OurHooks(root));
        Assert.Equal(HookInstaller.StatusLineCommand, (string?)root["statusLine"]!["command"]);
        Assert.Equal(2, (int)root["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["timeout"]!);
        Assert.Equal("*", (string?)root["hooks"]!["PreToolUse"]![0]!["matcher"]);
        Assert.Null(root["hooks"]!["SessionStart"]![0]!["matcher"]);
    }

    [Fact]
    public void Install_ExistingFile_KeepsEverythingElseAndBacksUpFirst()
    {
        Write(UserSettings);

        var result = HookInstaller.Install(Settings, Url, Now);
        var root = Read();

        Assert.NotNull(result.BackupPath);
        Assert.Equal(UserSettings, File.ReadAllText(result.BackupPath!));
        Assert.Equal("Bash(git push:*)", (string?)root["permissions"]!["deny"]![0]);
        Assert.Equal("opus", (string?)root["model"]);
        Assert.Equal("my-guard.sh", (string?)root["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["command"]);
        Assert.Equal(2, root["hooks"]!["PreToolUse"]!.AsArray().Count);
    }

    [Fact]
    public void Install_Twice_DoesNotDuplicate()
    {
        HookInstaller.Install(Settings, Url, Now);
        var second = HookInstaller.Install(Settings, Url, Now.AddSeconds(1));

        Assert.False(second.Changed);
        Assert.Null(second.BackupPath);
        Assert.Equal(ClaudeHookMapper.Kinds.Count, OurHooks(Read()));
    }

    [Fact]
    public void Install_KeepsAnExistingStatusLine()
    {
        Write("""{ "statusLine": { "type": "command", "command": "~/my-line.sh" } }""");

        var result = HookInstaller.Install(Settings, Url, Now);

        Assert.True(result.StatusLineKept);
        Assert.Equal("~/my-line.sh", (string?)Read()["statusLine"]!["command"]);
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("[1, 2, 3]")]
    public void Install_UnreadableFile_ChangesNothing(string content)
    {
        Write(content);

        var result = HookInstaller.Install(Settings, Url, Now);

        Assert.False(result.Changed);
        Assert.NotNull(result.Error);
        Assert.Equal(content, File.ReadAllText(Settings));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Settings)!));
    }

    [Fact]
    public void Uninstall_RemovesOnlyOurs()
    {
        Write(UserSettings);
        HookInstaller.Install(Settings, Url, Now);

        var result = HookInstaller.Uninstall(Settings, Url, Now.AddSeconds(1));
        var root = Read();

        Assert.True(result.Changed);
        Assert.NotNull(result.BackupPath);
        Assert.Equal(0, OurHooks(root));
        Assert.Null(root["statusLine"]);
        Assert.Equal("my-guard.sh", (string?)root["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["command"]);
        Assert.Single(root["hooks"]!.AsObject());
        Assert.Equal("opus", (string?)root["model"]);
    }

    [Fact]
    public void Uninstall_NoFile_DoesNothing()
    {
        var result = HookInstaller.Uninstall(Settings, Url, Now);

        Assert.False(result.Changed);
        Assert.False(File.Exists(Settings));
    }

    [Theory]
    [InlineData("""{ "hooks": "oops" }""")]
    [InlineData("""{ "hooks": { "PreToolUse": { "matcher": "Bash" } } }""")]
    public void Install_WrongShapeWeWouldTouch_ChangesNothing(string content)
    {
        Write(content);

        var result = HookInstaller.Install(Settings, Url, Now);

        Assert.NotNull(result.Error);
        Assert.Equal(content, File.ReadAllText(Settings));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Settings)!));
    }

    [Fact]
    public void OddEntries_DoNotCrashAndAreKept()
    {
        Write("""{ "statusLine": "plain", "hooks": { "Stop": [ "x", { "hooks": [ 1, { "type": 5, "url": [] } ] } ] } }""");

        var install = HookInstaller.Install(Settings, Url, Now);
        var uninstall = HookInstaller.Uninstall(Settings, Url, Now.AddSeconds(1));
        var root = Read();

        Assert.Null(install.Error);
        Assert.True(install.StatusLineKept);
        Assert.Null(uninstall.Error);
        Assert.Equal("plain", (string?)root["statusLine"]);
        Assert.Equal("x", (string?)root["hooks"]!["Stop"]![0]);
        Assert.Equal(2, root["hooks"]!["Stop"]!.AsArray().Count);
    }

    [Fact]
    public void Install_FileWithComments_ChangesNothing()
    {
        const string content = """
            {
              // my note
              "model": "opus"
            }
            """;
        Write(content);

        var result = HookInstaller.Install(Settings, Url, Now);

        Assert.NotNull(result.Error);
        Assert.Equal(content, File.ReadAllText(Settings));
    }

    [Fact]
    public void Install_KeepsTurkishAndSpecialCharactersReadable()
    {
        Write("""{ "permissions": { "allow": ["Read(C:/Kullanıcılar/çağrı/**)", "Bash(a && b > c)"] } }""");

        HookInstaller.Install(Settings, Url, Now);
        var text = File.ReadAllText(Settings);

        Assert.Contains("Kullanıcılar/çağrı", text);
        Assert.Contains("a && b > c", text);
        Assert.DoesNotContain("\\u", text);
    }

    [Fact]
    public void Install_SymlinkedSettings_WritesThroughToTheTarget()
    {
        var real = Path.Combine(_dir, "dotfiles", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(real)!);
        File.WriteAllText(real, """{ "model": "opus" }""");
        Directory.CreateDirectory(Path.GetDirectoryName(Settings)!);
        try
        {
            File.CreateSymbolicLink(Settings, real);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return; // Windows without symlink rights: nothing to test here.
        }

        HookInstaller.Install(Settings, Url, Now);

        Assert.NotNull(new FileInfo(Settings).LinkTarget);
        Assert.Contains("hooks/claude", File.ReadAllText(real));
    }

    [Fact]
    public void Uninstall_KeepsTheUsersOwnEmptyGroups()
    {
        Write("""{ "hooks": { "Notification": [ { "hooks": [] } ] } }""");
        HookInstaller.Install(Settings, Url, Now);

        HookInstaller.Uninstall(Settings, Url, Now.AddSeconds(1));

        Assert.Single(Read()["hooks"]!["Notification"]!.AsArray());
    }

    [Fact]
    public void Install_DuplicateKey_ChangesNothingAndDoesNotCrash()
    {
        const string content = """{ "model": "opus", "model": "sonnet" }""";
        Write(content);

        var result = HookInstaller.Install(Settings, Url, Now);

        Assert.NotNull(result.Error);
        Assert.Equal(content, File.ReadAllText(Settings));
    }

    [Fact]
    public void Install_DanglingSymlink_ChangesNothing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Settings)!);
        try
        {
            File.CreateSymbolicLink(Settings, Path.Combine(_dir, "gone", "settings.json"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return;
        }

        var result = HookInstaller.Install(Settings, Url, Now);

        Assert.NotNull(result.Error);
        Assert.NotNull(new FileInfo(Settings).LinkTarget);
    }
}
