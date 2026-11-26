using System.Text.Json.Nodes;
using AncosSantiye.Setup;

namespace AncosSantiye.Tests;

public sealed class ConnectorsTests : IDisposable
{
    private const string Url = "http://127.0.0.1:5170";
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);

    private readonly string _home = Path.Combine(Path.GetTempPath(), "santiye-home-" + Guid.NewGuid());

    private string AgyFile => AgyHooks.PathIn(_home);

    private string Plugin => OpenCodePlugin.PathIn(_home);

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void Agy_Install_AddsOnlyOurKey_AndKeepsTheUsersHooks()
    {
        Write(AgyFile, """{"mine":{"Stop":[{"hooks":[{"type":"command","command":"say done"}]}]}}""");

        var result = AgyHooks.Install(AgyFile, Now);

        Assert.True(result.Changed);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(AgyFile))!;
        Assert.Equal("say done", root["mine"]!["Stop"]![0]!["hooks"]![0]!["command"]!.GetValue<string>());
        var pre = root[AgyHooks.Key]!["PreToolUse"]![0]!;
        Assert.Equal(".*", pre["matcher"]!.GetValue<string>());
        Assert.Equal("ancos-santiye hook --from agy --event PreToolUse", pre["hooks"]![0]!["command"]!.GetValue<string>());
        Assert.Null(root[AgyHooks.Key]!["Stop"]![0]!["matcher"]);
        Assert.Equal(ToolStatus.Connected, AgyHooks.Check(_home).Status);
    }

    [Fact]
    public void Agy_Install_Twice_ChangesNothing()
    {
        AgyHooks.Install(AgyFile, Now);
        var before = File.ReadAllText(AgyFile);

        var result = AgyHooks.Install(AgyFile, Now);

        Assert.False(result.Changed);
        Assert.Equal(before, File.ReadAllText(AgyFile));
    }

    [Fact]
    public void Agy_Install_RepairsAnOldEntry()
    {
        Write(AgyFile, """{"ancos-santiye":{"Stop":[]}}""");
        Assert.Equal(ToolStatus.Partly, AgyHooks.Check(_home).Status);

        Assert.True(AgyHooks.Install(AgyFile, Now).Changed);
        Assert.Equal(ToolStatus.Connected, AgyHooks.Check(_home).Status);
    }

    [Theory]
    [InlineData("{ // comment\n}")]
    [InlineData("[1,2]")]
    [InlineData("{\"a\":1,\"a\":2}")]
    public void Agy_RefusesAFileItCannotFullyRead(string text)
    {
        Write(AgyFile, text);

        var result = AgyHooks.Install(AgyFile, Now);

        Assert.NotNull(result.Error);
        Assert.Equal(text, File.ReadAllText(AgyFile));
        Assert.Equal(ToolStatus.NotConnected, AgyHooks.Check(_home).Status);
    }

    [Fact]
    public void Agy_Uninstall_RemovesOnlyOurKey()
    {
        Write(AgyFile, """{"mine":{}}""");
        AgyHooks.Install(AgyFile, Now);

        Assert.True(AgyHooks.Uninstall(AgyFile, Now).Changed);
        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(AgyFile))!;
        Assert.NotNull(root["mine"]);
        Assert.Null(root[AgyHooks.Key]);
        Assert.False(AgyHooks.Uninstall(AgyFile, Now).Changed);
    }

    [Fact]
    public void Agy_NoFile_IsNotConnected_AndUninstallDoesNothing()
    {
        Assert.Equal(ToolStatus.NotConnected, AgyHooks.Check(_home).Status);
        Assert.False(AgyHooks.Uninstall(AgyFile, Now).Changed);
        Assert.False(File.Exists(AgyFile));
    }

    [Fact]
    public void OpenCode_Install_WritesOurMarkedPlugin()
    {
        var result = OpenCodePlugin.Install(Plugin, Url);

        Assert.True(result.Changed);
        var text = File.ReadAllText(Plugin);
        Assert.StartsWith(OpenCodePlugin.Mark, text);
        Assert.Contains("\"http://127.0.0.1:5170/hooks/opencode\"", text);
        Assert.Equal(ToolStatus.Connected, OpenCodePlugin.Check(_home, Url).Status);
        Assert.False(OpenCodePlugin.Install(Plugin, Url).Changed);
    }

    [Fact]
    public void OpenCode_AnotherAddress_IsPartly_UntilConnectedAgain()
    {
        OpenCodePlugin.Install(Plugin, "http://127.0.0.1:5999");

        Assert.Equal(ToolStatus.Partly, OpenCodePlugin.Check(_home, Url).Status);
        Assert.True(OpenCodePlugin.Install(Plugin, Url).Changed);
        Assert.Equal(ToolStatus.Connected, OpenCodePlugin.Check(_home, Url).Status);
    }

    [Fact]
    public void OpenCode_NeverOverwritesOrDeletesSomeoneElsesFile()
    {
        Write(Plugin, "export default () => ({})\n");

        Assert.NotNull(OpenCodePlugin.Install(Plugin, Url).Error);
        Assert.NotNull(OpenCodePlugin.Uninstall(Plugin).Error);
        Assert.Equal("export default () => ({})\n", File.ReadAllText(Plugin));
        Assert.Equal(ToolStatus.NotConnected, OpenCodePlugin.Check(_home, Url).Status);
    }

    [Fact]
    public void OpenCode_Uninstall_DeletesOurFile()
    {
        OpenCodePlugin.Install(Plugin, Url);

        Assert.True(OpenCodePlugin.Uninstall(Plugin).Changed);
        Assert.False(File.Exists(Plugin));
        Assert.False(OpenCodePlugin.Uninstall(Plugin).Changed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/home")]
    public void Connect_WithoutARealHome_WritesNothing(string home)
    {
        var before = Directory.GetCurrentDirectory();

        var result = Connect.Run("opencode", true, home, Url, Now);

        Assert.NotNull(result!.Error);
        Assert.False(Directory.Exists(Path.Combine(before, ".config")));
        Assert.False(Directory.Exists(Path.Combine(before, "relative")));
    }

    [Fact]
    public void Connect_KnowsThreeTools()
    {
        Directory.CreateDirectory(_home);
        Assert.Null(Connect.Run("codex", true, _home, Url, Now));
        Assert.True(Connect.Run("claude-code", true, _home, Url, Now)!.Changed);
        Assert.True(File.Exists(Path.Combine(_home, ".claude", "settings.json")));
        Assert.True(Connect.Run("agy", true, _home, Url, Now)!.Changed);
        Assert.True(Connect.Run("opencode", true, _home, Url, Now)!.Changed);
        Assert.True(Connect.Run("opencode", false, _home, Url, Now)!.Changed);
    }

    [Fact]
    public void Diagnose_AConfigThatCannotBeRead_IsANote_NotACrash()
    {
        var (status, notes) = Diagnose.Safely(() => throw new IOException("The file is locked."));

        Assert.Equal(ToolStatus.NotConnected, status);
        Assert.Contains("locked", notes.Single());
        Assert.Throws<InvalidOperationException>(() => Diagnose.Safely(() => throw new InvalidOperationException()));
    }

    [Fact]
    public void Diagnose_ReportsAgyAndOpenCode()
    {
        Directory.CreateDirectory(Path.Combine(_home, ".gemini", "antigravity-cli"));
        Directory.CreateDirectory(Path.Combine(_home, ".config", "opencode"));
        Connect.Run("agy", true, _home, Url, Now);

        var reports = Diagnose.Detect(_home, "", Url);

        Assert.Equal(ToolStatus.Connected, reports.Single(r => r.Id == "agy").Status);
        Assert.Equal(ToolStatus.NotConnected, reports.Single(r => r.Id == "opencode").Status);
    }
}
