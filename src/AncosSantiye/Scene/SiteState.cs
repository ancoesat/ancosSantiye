using System.Collections.Immutable;

namespace AncosSantiye.Scene;

/// <summary>What an actor is doing right now. The browser turns this into an animation.</summary>
public static class Activities
{
    public const string Idle = "idle";
    public const string Working = "working";
    public const string Failed = "failed";
}

/// <summary>Bubble lines the reducer sets itself; tool lines come from the adapter.</summary>
public static class Saying
{
    public const string Starting = "Getting started";
    public const string Waiting = "Waiting for you";
}

public sealed record ActorState(
    string Id,
    string Role,
    string? Name,
    string Activity,
    string? Tool,
    DateTimeOffset LastSeen,
    string? Say = null);

public sealed record PieceState(string Id, string Status, string? Verdict, DateTimeOffset Updated);

/// <summary>One task of a session's task list. The subject is the agent's own short title for it.</summary>
public sealed record TaskState(string Id, string Session, string? Subject, bool Done, DateTimeOffset Created, DateTimeOffset Updated);

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
    public string? CurrentTask => CurrentList.FirstOrDefault(t => !t.Done)?.Subject;

    public static SiteState Empty { get; } = new(
        null,
        ImmutableSortedDictionary<string, ActorState>.Empty,
        ImmutableSortedDictionary<string, PieceState>.Empty,
        ImmutableSortedDictionary<string, GateState>.Empty,
        ImmutableSortedDictionary<string, TaskState>.Empty,
        ImmutableSortedDictionary<string, string>.Empty,
        0);
}
