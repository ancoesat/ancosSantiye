using System.Text.Json;
using AncosSantiye.Events;

namespace AncosSantiye.Adapters;

/// <summary>
/// Events from our OpenCode plugin (see <see cref="Setup.OpenCodePlugin"/>). The plugin already sends only
/// safe fields; this side checks them again, because anything local can post here. Read: <c>kind</c> from
/// a fixed list, <c>session</c>, <c>project</c> (last folder only), <c>tool</c>, <c>file</c> (last segment
/// only), <c>description</c> (capped) and <c>todos</c> (content and status, capped like a plan).
/// </summary>
public static class OpenCodeMapper
{
    private static readonly IReadOnlySet<string> Kinds = new HashSet<string>
    {
        EventKinds.AgentStart, EventKinds.AgentStop, EventKinds.TurnStop,
        EventKinds.ToolStart, EventKinds.ToolEnd, EventKinds.ToolFail,
    };

    /// <summary>Ids and tool names from other agents: short, no spaces. Shared with <see cref="AgyHookMapper"/>.</summary>
    internal static readonly System.Text.RegularExpressions.Regex SafeId =
        new(@"^[A-Za-z0-9_.:-]{1,80}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static SiteEvent? Map(JsonElement body, DateTimeOffset at)
    {
        if (body.ValueKind != JsonValueKind.Object
            || ClaudeHookMapper.Text(body, "kind") is not { } kind
            || !Kinds.Contains(kind)
            || ClaudeHookMapper.Text(body, "session") is not { } session
            || !SafeId.IsMatch(session))
        {
            return null;
        }

        var tool = ClaudeHookMapper.Text(body, "tool") is { } t && SafeId.IsMatch(t) ? t : null;
        if (kind is EventKinds.ToolStart or EventKinds.ToolEnd or EventKinds.ToolFail && tool is null)
        {
            return null;
        }

        Dictionary<string, string>? data = null;
        if (tool is not null)
        {
            data = new Dictionary<string, string> { ["tool"] = tool };
            if (kind is not EventKinds.ToolEnd)
            {
                var say = Say(tool, body);
                data["say"] = kind == EventKinds.ToolFail ? ClaudeHookMapper.Cap("Failed: " + say) : say;
            }

            if (body.TryGetProperty("todos", out var todos) && todos.ValueKind == JsonValueKind.Array)
            {
                // Same shape as the desktop app's plan, so the same rules and caps apply.
                using var plan = JsonDocument.Parse($"{{\"items\":{todos.GetRawText()}}}");
                ClaudeHookMapper.AddPlan(data, plan.RootElement);
            }
        }

        return SiteEvent.Create(
            Sources.OpenCode,
            ClaudeHookMapper.ProjectName(ClaudeHookMapper.Text(body, "project")),
            new Actor($"opencode:{session}", Roles.Subcontractor, "opencode"),
            kind,
            session: session,
            data: data,
            ts: at);
    }

    public static string Say(string tool, JsonElement body)
    {
        var file = ClaudeHookMapper.Text(body, "file") is { Length: > 0 } path
                   && ClaudeHookMapper.ProjectName(path) is { Length: <= AgyHookMapper.MaxFileName } name ? name : "a file";
        var line = tool.ToLowerInvariant() switch
        {
            "read" => $"Reading {file}",
            "write" => $"Writing {file}",
            "edit" or "patch" or "multiedit" => $"Editing {file}",
            "bash" => ClaudeHookMapper.Short(ClaudeHookMapper.Text(body, "description")) ?? "Running a command",
            "grep" or "glob" or "list" => "Searching the code",
            "webfetch" or "websearch" => "Reading the web",
            "todowrite" => "Updating the plan",
            "todoread" => "Checking the plan",
            "task" => ClaudeHookMapper.Short(ClaudeHookMapper.Text(body, "description")) is { } d ? $"Calling a helper: {d}" : "Calling a helper",
            _ => $"Using {tool}",
        };
        return ClaudeHookMapper.Cap(line);
    }
}
