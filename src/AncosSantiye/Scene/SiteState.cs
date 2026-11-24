using System.Collections.Immutable;

namespace AncosSantiye.Scene;

/// <summary>What an actor is doing right now. The browser turns this into an animation.</summary>
public static class Activities
{
    public const string Idle = "idle";
    public const string Working = "working";
    public const string Failed = "failed";
}

public sealed record ActorState(
    string Id,
    string Role,
    string? Name,
    string Activity,
    string? Tool,
    DateTimeOffset LastSeen);

public sealed record PieceState(string Id, string Status, string? Verdict, DateTimeOffset Updated);

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
    int TasksPlanned,
    int TasksDone,
    ImmutableSortedDictionary<string, string> Usage,
    long Version)
{
    public static SiteState Empty { get; } = new(
        null,
        ImmutableSortedDictionary<string, ActorState>.Empty,
        ImmutableSortedDictionary<string, PieceState>.Empty,
        ImmutableSortedDictionary<string, GateState>.Empty,
        0,
        0,
        ImmutableSortedDictionary<string, string>.Empty,
        0);
}
