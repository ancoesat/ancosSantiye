using System.Text.Json;
using AncosSantiye.Events;

namespace AncosSantiye.Adapters;

/// <summary>
/// Antigravity CLI (agy) hooks. Its payload has no event name, so the hook command passes it
/// (<c>ancos-santiye hook --from agy --event PreToolUse</c>). Read: <c>conversationId</c>, the last folder of
/// the first <c>workspacePaths</c> entry, <c>toolCall.name</c>, the last segment of a file path in
/// <c>toolCall.args</c>, and whether <c>error</c> is set. Never the command, the file contents,
/// <c>lastUserInput</c> or the transcript.
/// </summary>
public static class AgyHookMapper
{
    public static readonly IReadOnlyList<string> Events = ["SessionStart", "PreToolUse", "PostToolUse", "PreInvocation", "Stop"];

    /// <summary>Events that carry a tool call and take a matcher.</summary>
    public static readonly IReadOnlySet<string> ToolEvents = new HashSet<string> { "PreToolUse", "PostToolUse" };

    public const int MaxFileName = 40;

    private static readonly string[] PathFields = ["TargetFile", "AbsolutePath", "FilePath", "file_path", "filePath", "path", "Path"];

    public static SiteEvent? Map(string? eventName, JsonElement hook, DateTimeOffset at)
    {
        if (hook.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (ClaudeHookMapper.Text(hook, "conversationId") is not { } conversation || !OpenCodeMapper.SafeId.IsMatch(conversation))
        {
            return null;
        }

        var tool = hook.TryGetProperty("toolCall", out var call) && call.ValueKind == JsonValueKind.Object
            && ClaudeHookMapper.Text(call, "name") is { } name && OpenCodeMapper.SafeId.IsMatch(name) ? name : null;
        var args = call.ValueKind == JsonValueKind.Object && call.TryGetProperty("args", out var a) ? a : default;
        var failed = ClaudeHookMapper.Text(hook, "error") is { Length: > 0 };

        string? kind = eventName switch
        {
            "SessionStart" => EventKinds.AgentStart,
            "PreToolUse" when tool is not null => EventKinds.ToolStart,
            "PostToolUse" when tool is not null => failed ? EventKinds.ToolFail : EventKinds.ToolEnd,
            "PreInvocation" => EventKinds.TurnStart,
            // agy mostly runs as a lane (agy -p): a finished turn is a finished run, so the worker leaves.
            "Stop" => EventKinds.AgentStop,
            _ => null,
        };

        if (kind is null)
        {
            return null;
        }

        Dictionary<string, string>? data = null;
        if (tool is not null && kind is EventKinds.ToolStart or EventKinds.ToolFail or EventKinds.ToolEnd)
        {
            data = new Dictionary<string, string> { ["tool"] = tool };
            if (kind is not EventKinds.ToolEnd)
            {
                var say = Say(tool, args);
                data["say"] = kind == EventKinds.ToolFail ? ClaudeHookMapper.Cap("Failed: " + say) : say;
            }
        }

        return SiteEvent.Create(
            Sources.Agy,
            Project(hook),
            new Actor($"agy:{conversation}", Roles.Subcontractor, "agy"),
            kind,
            session: conversation,
            data: data,
            ts: at);
    }

    public static string Say(string tool, JsonElement args)
    {
        var file = FileName(args);
        string Has(params string[] words) => words.FirstOrDefault(w => tool.Contains(w, StringComparison.OrdinalIgnoreCase)) ?? "";
        var line = Has("command", "shell", "terminal", "bash", "powershell") != "" ? "Running a command"
            : Has("browser") != "" ? "Using the browser"
            : Has("write", "create") != "" ? $"Writing {file}"
            : Has("edit", "replace") != "" ? $"Editing {file}"
            : Has("read", "view") != "" ? $"Reading {file}"
            : Has("grep", "search") != "" ? "Searching the code"
            : Has("list", "find") != "" ? "Looking for files"
            : $"Using {tool}";
        return ClaudeHookMapper.Cap(line);
    }

    private static string FileName(JsonElement args)
    {
        if (args.ValueKind == JsonValueKind.Object)
        {
            foreach (var field in PathFields)
            {
                // A file name, not whatever text sits in the field: a long one is not shown.
                if (ClaudeHookMapper.Text(args, field) is { Length: > 0 } path
                    && ClaudeHookMapper.ProjectName(path) is { Length: <= MaxFileName } file)
                {
                    return file;
                }
            }
        }

        return "a file";
    }

    private static string Project(JsonElement hook) =>
        hook.TryGetProperty("workspacePaths", out var paths) && paths.ValueKind == JsonValueKind.Array
            && paths.EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.String } first
            ? ClaudeHookMapper.ProjectName(first.GetString())
            : "agy";
}
