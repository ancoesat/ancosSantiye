using System.Text.Json.Nodes;
using AncosSantiye.Adapters;
using AncosSantiye.Setup;

namespace AncosSantiye.Tests;

public sealed class DiagnoseTests : IDisposable
{
    private const string Url = "http://127.0.0.1:5170";

    private readonly string _home = Path.Combine(Path.GetTempPath(), "santiye-home-" + Guid.NewGuid());
    private readonly string _bin;

    public DiagnoseTests()
    {
        _bin = Path.Combine(_home, "bin");
        Directory.CreateDirectory(_bin);
    }

    public void Dispose() => Directory.Delete(_home, recursive: true);

    private IReadOnlyList<ToolReport> Run() => Diagnose.Detect(_home, _bin, Url);

    private ToolReport Tool(string id) => Run().Single(r => r.Id == id);

    private string Settings => Path.Combine(_home, ".claude", "settings.json");

    [Fact]
    public void AnEmptyMachine_HasNothing()
    {
        Assert.All(Run(), r => Assert.Equal((false, ToolStatus.Missing), (r.Found, r.Status)));
    }

    [Fact]
    public void Tools_AreFoundByProgramOrFolder_AndShownFromTheHomeFolder()
    {
        File.WriteAllText(Path.Combine(_bin, "agy"), "");
        Directory.CreateDirectory(Path.Combine(_home, ".config", "opencode"));
        Directory.CreateDirectory(Path.Combine(_home, ".codex"));

        Assert.Equal(["agy on PATH"], Tool("agy").FoundBy);
        Assert.Equal(["~/.config/opencode"], Tool("opencode").FoundBy);
        Assert.Equal(ToolStatus.NotConnected, Tool("agy").Status);
        Assert.Equal(ToolStatus.NotSupported, Tool("codex").Status);
        Assert.DoesNotContain(Run().SelectMany(r => r.FoundBy.Concat(r.Notes)), s => s.Contains(_home));
    }

    [Fact]
    public void ClaudeCode_WithEverythingInstalled_IsConnected()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Settings)!);
        HookInstaller.Install(Settings, Url, DateTimeOffset.UtcNow);

        var claude = Tool("claude-code");

        Assert.Equal(ToolStatus.Connected, claude.Status);
        Assert.DoesNotContain(claude.Notes, n => n.Contains("Missing") || n.Contains("No status line") || n.Contains("Your own status line"));
        Assert.Contains(claude.Notes, n => n.Contains("terminal sessions"));
    }

    [Fact]
    public void ClaudeCode_ReportsWhatIsMissing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Settings)!);
        HookInstaller.Install(Settings, Url, DateTimeOffset.UtcNow);
        var root = JsonNode.Parse(File.ReadAllText(Settings))!.AsObject();
        root["hooks"]!.AsObject().Remove("Stop");
        root["hooks"]!["SessionStart"]![0]!["hooks"]![0] = new JsonObject { ["type"] = "http", ["url"] = Url + "/hooks/claude" };
        root["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = "my-line.sh" };
        File.WriteAllText(Settings, root.ToJsonString());

        var claude = Tool("claude-code");

        Assert.Equal(ToolStatus.Partly, claude.Status);
        Assert.Contains(claude.Notes, n => n.Contains("Missing hooks: SessionStart, Stop"));
        Assert.Contains(claude.Notes, n => n.Contains("old http entry"));
        Assert.Contains(claude.Notes, n => n.Contains("Your own status line"));
    }

    [Theory]
    [InlineData(null, "No ~/.claude/settings.json")]
    [InlineData("{ not json", "not valid JSON")]
    [InlineData("{}", "Missing hooks")]
    public void ClaudeCode_NotConnected_SaysWhy(string? content, string note)
    {
        Directory.CreateDirectory(Path.Combine(_home, ".claude"));
        if (content is not null)
        {
            File.WriteAllText(Settings, content);
        }

        var claude = Tool("claude-code");

        Assert.Equal(ToolStatus.NotConnected, claude.Status);
        Assert.Contains(claude.Notes, n => n.Contains(note));
    }

    [Fact]
    public void Diagnose_NeverWritesAnything()
    {
        Directory.CreateDirectory(Path.Combine(_home, ".claude"));
        File.WriteAllText(Settings, "{}");
        var before = Directory.EnumerateFileSystemEntries(_home, "*", SearchOption.AllDirectories).Order().ToList();

        Run();

        Assert.Equal(before, Directory.EnumerateFileSystemEntries(_home, "*", SearchOption.AllDirectories).Order());
        Assert.Equal("{}", File.ReadAllText(Settings));
    }

    [Fact]
    public void OnPath_SkipsBrokenEntries()
    {
        File.WriteAllText(Path.Combine(_bin, "opencode"), "");

        Assert.True(Diagnose.OnPath("opencode", $"{Path.Combine(_home, "nope")}{Path.PathSeparator}<>|bad{Path.PathSeparator}\"{_bin}\"{Path.PathSeparator}"));
        Assert.False(Diagnose.OnPath("opencode", null));
        Assert.False(Diagnose.OnPath("agy", _bin));
    }
}
