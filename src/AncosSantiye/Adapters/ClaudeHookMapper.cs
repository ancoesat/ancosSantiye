using System.Text.Json;
using AncosSantiye.Events;

namespace AncosSantiye.Adapters;

/// <summary>
/// Turns one Claude Code hook payload into one site event. Reads only names and ids:
/// <c>tool_input</c>, prompts and messages are never looked at, so they can never reach the log.
/// </summary>
public static class ClaudeHookMapper
{
    public static readonly Actor Chief = new("chief", Roles.Chief, "Claude Code");

    /// <summary>Hook events we subscribe to, and the site event each one becomes.</summary>
    public static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>
    {
        ["SessionStart"] = EventKinds.SessionStart,
        ["SessionEnd"] = EventKinds.SessionEnd,
        ["PreToolUse"] = EventKinds.ToolStart,
        ["PostToolUse"] = EventKinds.ToolEnd,
        ["PostToolUseFailure"] = EventKinds.ToolFail,
        ["SubagentStart"] = EventKinds.AgentStart,
        ["SubagentStop"] = EventKinds.AgentStop,
        ["Stop"] = EventKinds.TurnStop,
        ["TaskCreated"] = EventKinds.TaskCreated,
        ["TaskCompleted"] = EventKinds.TaskCompleted,
    };

    /// <summary>Hook events that carry a tool name, so their config gets a match-all matcher.</summary>
    public static readonly IReadOnlySet<string> ToolHooks = new HashSet<string> { "PreToolUse", "PostToolUse", "PostToolUseFailure" };

    public static SiteEvent? Map(JsonElement hook, DateTimeOffset at)
    {
        if (hook.ValueKind != JsonValueKind.Object
            || Text(hook, "hook_event_name") is not { } name
            || !Kinds.TryGetValue(name, out var kind))
        {
            return null;
        }

        var tool = Text(hook, "tool_name");
        return SiteEvent.Create(
            Sources.ClaudeCode,
            ProjectName(Text(hook, "cwd")),
            ActorFor(Text(hook, "agent_id"), Text(hook, "agent_type")),
            kind,
            session: Text(hook, "session_id"),
            data: tool is null ? null : new Dictionary<string, string> { ["tool"] = tool },
            ts: at);
    }

    /// <summary>Last folder of a path, for both Windows and POSIX separators.</summary>
    public static string ProjectName(string? cwd)
    {
        var name = cwd?.TrimEnd('/', '\\').Split('/', '\\').LastOrDefault();
        return string.IsNullOrEmpty(name) ? "unknown" : name;
    }

    private static Actor ActorFor(string? agentId, string? agentType) =>
        agentId is null
            ? Chief
            : new Actor(agentId, agentType switch
            {
                "denetci" => Roles.Inspector,
                "danisman" => Roles.Architect,
                _ => Roles.Worker,
            }, agentType);

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}
