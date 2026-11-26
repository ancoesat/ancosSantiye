using System.Text.Json;
using AncosSantiye.Events;

namespace AncosSantiye.Adapters;

/// <summary>
/// Turns one Claude Code hook payload into one site event. Reads names and ids, plus a few short,
/// safe fields of <c>tool_input</c> for the speech bubble (see <see cref="Say"/>). Commands, file
/// contents, full paths, prompts and messages are never read, so they can never reach the log.
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
        Dictionary<string, string>? data = null;
        if (kind is EventKinds.TaskCreated or EventKinds.TaskCompleted)
        {
            // The task's id and the agent's own short title for it; never its description.
            data = new Dictionary<string, string>();
            if (Text(hook, "task_id") is { } taskId)
            {
                data["task"] = taskId;
            }

            if (Short(Text(hook, "task_subject")) is { } subject)
            {
                data["subject"] = subject.Length <= MaxSay ? subject : subject[..(MaxSay - 3)] + "...";
            }
        }

        if (tool is not null)
        {
            data = new Dictionary<string, string> { ["tool"] = tool };
            var input = hook.TryGetProperty("tool_input", out var given) ? given : default;
            if (kind is EventKinds.ToolStart or EventKinds.ToolFail)
            {
                data["say"] = Say(tool, input);
            }

            if (kind is EventKinds.ToolStart && tool == PlanTool)
            {
                AddPlan(data, input);
            }
        }

        return SiteEvent.Create(
            Sources.ClaudeCode,
            ProjectName(Text(hook, "cwd")),
            ActorFor(Text(hook, "agent_id"), Text(hook, "agent_type"), Text(hook, "session_id")),
            kind,
            session: Text(hook, "session_id"),
            data: data,
            ts: at);
    }

    public const int MaxSay = 60;

    /// <summary>The Claude desktop app keeps its task list with this tool instead of TaskCreate.</summary>
    public const string PlanTool = "mcp__ccd_turn__update_plan";

    public const int MaxPlanSteps = 12;

    private static readonly HashSet<string> PlanStatuses = ["pending", "in_progress", "completed"];

    /// <summary>
    /// The plan as flat values: <c>step1</c>..<c>step12</c> as "status|title" when the whole list is sent,
    /// <c>planItems</c> with its length, and <c>planDone</c>, <c>planStart</c>, <c>planAdvance</c> for the
    /// small updates. Step titles are the agent's own short words, cut like a bubble line.
    /// </summary>
    private static void AddPlan(Dictionary<string, string> data, JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (input.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            var n = 0;
            foreach (var item in items.EnumerateArray())
            {
                if (n == MaxPlanSteps)
                {
                    break;
                }

                if (Field(item, "status") is { } status && PlanStatuses.Contains(status))
                {
                    var title = Short(Field(item, "content")) ?? "Step";
                    title = title.Length <= MaxSay ? title : title[..(MaxSay - 3)] + "...";
                    data[$"step{++n}"] = $"{status}|{title}";
                }
            }

            data["planItems"] = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (input.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.Array)
        {
            var steps = done.EnumerateArray()
                .Where(d => d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out var i) && i is >= 1 and <= MaxPlanSteps)
                .Select(d => d.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture));
            data["planDone"] = string.Join(',', steps);
        }

        if (input.TryGetProperty("start", out var start) && start.ValueKind == JsonValueKind.Number
            && start.TryGetInt32(out var s) && s is >= 1 and <= MaxPlanSteps)
        {
            data["planStart"] = s.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (input.TryGetProperty("advance", out var advance) && advance.ValueKind == JsonValueKind.True)
        {
            data["planAdvance"] = "true";
        }
    }

    /// <summary>
    /// One short line for the speech bubble. Only these parts of the input are ever read: the last
    /// segment of a file path (never the folders), the agent's own short <c>description</c>, a
    /// subagent or skill name, and a URL's host. Commands, patterns and contents are not.
    /// </summary>
    public static string Say(string tool, JsonElement input)
    {
        var line = tool switch
        {
            "Read" => $"Reading {FileName(input, "file_path")}",
            "Write" => $"Writing {FileName(input, "file_path")}",
            "Edit" or "MultiEdit" => $"Editing {FileName(input, "file_path")}",
            "NotebookEdit" => $"Editing {FileName(input, "notebook_path")}",
            "Glob" => "Looking for files",
            "Grep" => "Searching the code",
            "Bash" or "PowerShell" => Short(Field(input, "description")) ?? "Running a command",
            "BashOutput" or "Monitor" => "Watching a command",
            "WebFetch" => $"Reading {Host(Field(input, "url"))}",
            "WebSearch" => "Searching the web",
            "Task" or "Agent" => $"Calling {Short(Field(input, "subagent_type")) ?? "a helper"}",
            "TodoWrite" or "TaskCreate" or "TaskUpdate" or "TaskList" or "TaskGet" => "Updating the task list",
            "AskUserQuestion" => "Asking you something",
            "ExitPlanMode" => "Plan ready",
            "Skill" => $"Using skill {Short(Field(input, "skill")) ?? "?"}",
            PlanTool => "Updating the plan",
            "mcp__ccd_turn__wrap_up" => "Wrapping up",
            _ when tool.StartsWith("mcp__Claude_Browser__", StringComparison.Ordinal) => "Using the browser",
            _ when tool.StartsWith("mcp__", StringComparison.Ordinal) => $"Using {tool.Split("__")[1]}",
            _ => $"Using {tool}",
        };

        return line.Length <= MaxSay ? line : line[..(MaxSay - 3)] + "...";
    }

    private static string? Field(JsonElement input, string name) =>
        input.ValueKind == JsonValueKind.Object ? Text(input, name) : null;

    private static string FileName(JsonElement input, string field) =>
        Field(input, field) is { } path ? ProjectName(path) : "a file";

    /// <summary>First line only, so a long description never turns into a wall of text.</summary>
    private static string? Short(string? text) =>
        text?.Split('\n')[0].Trim() is { Length: > 0 } line ? line : null;

    private static string Host(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host : "a page";

    /// <summary>Last folder of a path, for both Windows and POSIX separators.</summary>
    public static string ProjectName(string? cwd)
    {
        var name = cwd?.TrimEnd('/', '\\').Split('/', '\\').LastOrDefault();
        return string.IsNullOrEmpty(name) ? "unknown" : name;
    }

    /// <summary>
    /// One main worker per session, so two Claude Code windows on one project are two workers.
    /// Subagents already have their own id.
    /// </summary>
    private static Actor ActorFor(string? agentId, string? agentType, string? session) =>
        agentId is null
            ? session is null ? Chief : Chief with { Id = $"chief:{session}" }
            : new Actor(agentId, agentType switch
            {
                "denetci" => Roles.Inspector,
                "danisman" => Roles.Architect,
                _ => Roles.Worker,
            }, agentType);

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}
