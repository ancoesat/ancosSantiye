using System.Collections.Immutable;

namespace AncosSantiye.Scene;

/// <summary>What an actor is doing right now. The browser turns this into an animation.</summary>
public static class Activities
{
    public const string Idle = "idle";
    public const string Working = "working";
    public const string Failed = "failed";
}

/// <summary>
/// What kind of thing an actor is doing, so the page can put it in the right place: reading at the board,
/// building on the building, running at the machine, planning at the office, waiting at its own spot.
/// </summary>
public static class Doing
{
    public const string Start = "start";
    public const string Read = "read";
    public const string Build = "build";
    public const string Run = "run";
    public const string Plan = "plan";
    public const string Wait = "wait";
    public const string Fail = "fail";

    public static string Of(string? tool) => tool switch
    {
        "Read" or "Grep" or "Glob" or "LS" or "WebFetch" or "WebSearch" or "NotebookRead" => Read,
        _ when tool?.StartsWith("mcp__Claude_Browser__", StringComparison.Ordinal) == true => Read,
        "Write" or "Edit" or "MultiEdit" or "NotebookEdit" => Build,
        "Bash" or "PowerShell" or "BashOutput" or "Monitor" or "KillShell" => Run,
        "AskUserQuestion" => Wait,
        // Planning tools of other agents, before the word rules below could misread them ("todowrite").
        "TodoWrite" or "todowrite" or "todoread" or "task" => Plan,
        _ when tool?.StartsWith("mcp__", StringComparison.Ordinal) == true => Plan,
        // Tools of other agents (OpenCode, agy) by what their names say, in the same order as their bubbles
        // (AgyHookMapper.Say): a command first, then writing, then reading.
        _ when HasWord(tool, "bash", "shell", "command", "terminal", "powershell") => Run,
        _ when HasWord(tool, "write", "edit", "replace", "create", "patch") => Build,
        _ when HasWord(tool, "read", "view", "grep", "glob", "search", "find", "list", "fetch") => Read,
        _ => Plan,
    };

    private static bool HasWord(string? tool, params string[] words) =>
        tool is not null && words.Any(w => tool.Contains(w, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A worker that has said nothing for a while has left, even if its session never said goodbye
/// (the app was closed, the login ran out). Applied when the state is served, so the reducer and the
/// log stay free of clock time. Waiting for you gets longer: you may just be away from the desk.
/// </summary>
public static class Silence
{
    public static readonly TimeSpan Working = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Waiting = TimeSpan.FromMinutes(60);

    /// <summary>A background job sends nothing while it runs and no hook says when it is done.</summary>
    public static readonly TimeSpan Job = TimeSpan.FromMinutes(30);

    private static TimeSpan LimitFor(ActorState a) =>
        a.Id.StartsWith("job:", StringComparison.Ordinal) ? Job : a.Action == Doing.Wait ? Waiting : Working;

    public static SiteState Drop(SiteState state, DateTimeOffset now)
    {
        var gone = state.Actors
            .Where(a => now - a.Value.LastSeen > LimitFor(a.Value))
            .Select(a => a.Key)
            .ToArray();
        return gone.Length == 0 ? state : state with { Actors = state.Actors.RemoveRange(gone) };
    }
}

/// <summary>Bubble lines the reducer sets itself; tool lines come from the adapter.</summary>
public static class Saying
{
    public const string Starting = "Getting started";
    public const string Waiting = "Waiting for you";
    public const string Thinking = "Thinking";
}

public sealed record ActorState(
    string Id,
    string Role,
    string? Name,
    string Activity,
    string? Tool,
    DateTimeOffset LastSeen,
    string? Say = null,
    string? Action = null);

public sealed record PieceState(string Id, string Status, string? Verdict, DateTimeOffset Updated);

/// <summary>One task of a session's task list. The subject is the agent's own short title for it.</summary>
public sealed record TaskState(string Id, string Session, string? Subject, bool Done, DateTimeOffset Created, DateTimeOffset Updated, bool Active = false);

public sealed record GateState(string Id, string? Piece, string? Label, DateTimeOffset Since);

/// <summary>
/// The whole site at one moment. Built only by <see cref="SiteReducer"/>, so the same events
/// always give the same state.
/// </summary>
public sealed record SiteState(
    string? Stage,
    ImmutableSortedDictionary<string, ActorState> Actors,
    ImmutableSortedDictionary<string, PieceState> Pieces,
    ImmutableSortedDictionary<string, GateState> Gates,
    ImmutableSortedDictionary<string, TaskState> Tasks,
    ImmutableSortedDictionary<string, string> Usage,
    long Version)
{
    /// <summary>
    /// The task list on screen: the one of the session that touched its tasks last. Older lists of the
    /// same project stay in <see cref="Tasks"/> but do not count, so a finished list never inflates a new one.
    /// </summary>
    private TaskState[] CurrentList
    {
        get
        {
            if (Tasks.IsEmpty)
            {
                return [];
            }

            var session = Tasks.Values.MaxBy(t => t.Updated)!.Session;
            return [.. Tasks.Values.Where(t => t.Session == session).OrderBy(t => t.Created).ThenBy(t => t.Id, StringComparer.Ordinal)];
        }
    }

    /// <summary>The current list itself, in the order the tasks were created, for the page to show.</summary>
    public IReadOnlyList<TaskState> CurrentTasks => CurrentList;

    public int TasksPlanned => CurrentList.Length;

    public int TasksDone => CurrentList.Count(t => t.Done);

    /// <summary>The first open task of the current list: what the work is about right now.</summary>
    public string? CurrentTask =>
        (CurrentList.FirstOrDefault(t => t.Active && !t.Done) ?? CurrentList.FirstOrDefault(t => !t.Done))?.Subject;

    /// <summary>When the status line last sent new values. The page uses it to say how old Budget is.</summary>
    public DateTimeOffset? UsageUpdated { get; init; }

    public static SiteState Empty { get; } = new(
        null,
        ImmutableSortedDictionary<string, ActorState>.Empty,
        ImmutableSortedDictionary<string, PieceState>.Empty,
        ImmutableSortedDictionary<string, GateState>.Empty,
        ImmutableSortedDictionary<string, TaskState>.Empty,
        ImmutableSortedDictionary<string, string>.Empty,
        0);
}
