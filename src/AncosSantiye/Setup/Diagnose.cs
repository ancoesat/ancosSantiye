using System.Text.Json.Nodes;
using AncosSantiye.Adapters;

namespace AncosSantiye.Setup;

/// <summary>One AI tool as found on this machine, and how far ancosSantiye is connected to it.</summary>
public sealed record ToolReport(
    string Id,
    string Name,
    bool Found,
    IReadOnlyList<string> FoundBy,
    string Status,
    IReadOnlyList<string> Notes,
    DateTimeOffset? LastEvent = null);

public static class ToolStatus
{
    public const string Connected = "connected";
    public const string Partly = "partly connected";
    public const string NotConnected = "not connected";
    public const string NotSupported = "found, not supported yet";
    public const string Missing = "not found";
}

/// <summary>
/// Looks for AI coding tools on this machine, read only: a program on PATH, a config folder in the home
/// folder. For tools ancosSantiye can connect to, it also reads that tool's config to see whether our
/// integration is there. Paths in the report start at "~" so the home folder's name is not shown.
/// </summary>
public static class Diagnose
{
    private sealed record Known(string Id, string Name, string[] Programs, string[] Folders);

    private static readonly Known[] Tools =
    [
        new("claude-code", "Claude Code", ["claude"], [".claude"]),
        new("opencode", "OpenCode", ["opencode"], [".config/opencode", "AppData/Roaming/opencode"]),
        new("agy", "Antigravity CLI (agy)", ["agy"], [".gemini/antigravity-cli"]),
        new("codex", "Codex CLI", ["codex"], [".codex"]),
        new("gemini", "Gemini CLI", ["gemini"], [".gemini/settings.json"]),
        new("cursor", "Cursor agent", ["cursor-agent"], [".cursor"]),
        new("aider", "Aider", ["aider"], [".aider.conf.yml"]),
        new("qwen", "Qwen Code", ["qwen"], [".qwen"]),
    ];

    /// <summary>Everything that can be learned without the server. Last events are added by the caller.</summary>
    public static IReadOnlyList<ToolReport> Detect(string home, string? pathVariable, string serverUrl)
    {
        var reports = new List<ToolReport>();
        foreach (var tool in Tools)
        {
            var foundBy = new List<string>();
            foundBy.AddRange(tool.Programs.Where(p => OnPath(p, pathVariable)).Select(p => $"{p} on PATH"));
            foundBy.AddRange(tool.Folders.Where(f => Exists(Path.Combine(home, f))).Select(f => $"~/{f}"));
            var found = foundBy.Count > 0;

            var (status, notes) = tool.Id switch
            {
                "claude-code" when found => ClaudeCode(home, serverUrl),
                "opencode" or "agy" when found => (ToolStatus.NotConnected, (IReadOnlyList<string>)["Adapter coming in the next step of v0.3."]),
                _ when found => (ToolStatus.NotSupported, (IReadOnlyList<string>)[]),
                _ => (ToolStatus.Missing, (IReadOnlyList<string>)[]),
            };

            reports.Add(new ToolReport(tool.Id, tool.Name, found, foundBy, status, notes));
        }

        return reports;
    }

    /// <summary>Reads ~/.claude/settings.json the way HookInstaller writes it. Never changes it.</summary>
    private static (string, IReadOnlyList<string>) ClaudeCode(string home, string serverUrl)
    {
        var path = Path.Combine(home, ".claude", "settings.json");
        if (!File.Exists(path))
        {
            return (ToolStatus.NotConnected, ["No ~/.claude/settings.json yet. Run: ancos-santiye hooks install"]);
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip }) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return (ToolStatus.NotConnected, ["~/.claude/settings.json is not valid JSON, so it cannot be checked."]);
        }

        var url = HookInstaller.HookUrl(serverUrl);
        var hooks = root?["hooks"] as JsonObject;
        var missing = new List<string>();
        var oldHttpStart = false;
        foreach (var name in ClaudeHookMapper.Kinds.Keys)
        {
            var entries = (hooks?[name] as JsonArray)?.OfType<JsonObject>()
                .SelectMany(g => (g["hooks"] as JsonArray)?.OfType<JsonObject>() ?? []) ?? [];
            var ours = entries.Where(h =>
                (Str(h, "type") == "http" && Str(h, "url") == url) || (Str(h, "type") == "command" && Str(h, "command") == HookInstaller.HookCommand)).ToList();
            if (HookInstaller.CommandOnly.Contains(name) && ours.Any(h => Str(h, "type") == "http"))
            {
                oldHttpStart = true;
            }

            if (!ours.Any(h => !HookInstaller.CommandOnly.Contains(name) || Str(h, "type") == "command"))
            {
                missing.Add(name);
            }
        }

        var notes = new List<string>();
        if (missing.Count > 0)
        {
            notes.Add($"Missing hooks: {string.Join(", ", missing)}. Run: ancos-santiye hooks install");
        }

        if (oldHttpStart)
        {
            notes.Add("SessionStart still has the old http entry, which never fires. Run: ancos-santiye hooks install");
        }

        var statusLine = root?["statusLine"] as JsonObject;
        if (statusLine is null)
        {
            notes.Add("No status line: the Budget panel stays empty. Run: ancos-santiye hooks install");
        }
        else if (Str(statusLine, "command") != HookInstaller.StatusLineCommand)
        {
            notes.Add("Your own status line is set, so the Budget panel stays empty unless it passes its input to ancos-santiye statusline.");
        }

        notes.Add("Budget data only comes from terminal sessions (claude); the desktop app does not run the status line.");
        var status = missing.Count == 0 ? ToolStatus.Connected : missing.Count < ClaudeHookMapper.Kinds.Count ? ToolStatus.Partly : ToolStatus.NotConnected;
        return (status, notes);
    }

    /// <summary>A program on PATH, with the Windows launcher extensions (agy.exe, opencode.cmd).</summary>
    public static bool OnPath(string program, string? pathVariable)
    {
        if (string.IsNullOrEmpty(pathVariable))
        {
            return false;
        }

        string[] names = OperatingSystem.IsWindows()
            ? [.. new[] { ".exe", ".cmd", ".bat", ".ps1" }.Select(e => program + e), program]
            : [program];
        // File.Exists says false for a path that cannot exist, so a broken PATH entry is simply skipped.
        return pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(dir => names.Any(n => File.Exists(Path.Combine(dir.Trim('"'), n))));
    }

    private static bool Exists(string path) => Directory.Exists(path) || File.Exists(path);

    private static string? Str(JsonObject o, string name) =>
        o.TryGetPropertyValue(name, out var v) && v is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
}
