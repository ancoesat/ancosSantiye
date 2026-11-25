using AncosSantiye.Events;

namespace AncosSantiye.Scene;

/// <summary>
/// Pure function: state and event in, new state out. No clock, no I/O, no randomness.
/// Every time it needs comes from the event itself.
/// </summary>
public static class SiteReducer
{
    public static SiteState Replay(IEnumerable<SiteEvent> events) =>
        events.Aggregate(SiteState.Empty, Reduce);

    public static SiteState Reduce(SiteState state, SiteEvent e)
    {
        var next = e.Kind switch
        {
            EventKinds.SessionStart or EventKinds.AgentStart => SetActor(state, e, Activities.Idle, tool: null, Saying.Starting),
            EventKinds.TurnStop => SetActor(state, e, Activities.Idle, tool: null, Saying.Waiting),
            // The bubble keeps the last action between two tools, so it does not flicker.
            EventKinds.ToolEnd => SetActor(state, e, Activities.Idle, tool: null, state.Actors.GetValueOrDefault(e.Actor.Id)?.Say),
            EventKinds.ToolStart => SetActor(state, e, Activities.Working, Value(e, "tool"), Value(e, "say") ?? UsingTool(e)),
            EventKinds.ToolFail => SetActor(state, e, Activities.Failed, Value(e, "tool"), "Failed: " + (Value(e, "say") ?? UsingTool(e))),
            EventKinds.SessionEnd or EventKinds.AgentStop => state with { Actors = state.Actors.Remove(e.Actor.Id) },
            EventKinds.TaskCreated => state with { TasksPlanned = state.TasksPlanned + 1 },
            EventKinds.TaskCompleted => state with { TasksDone = state.TasksDone + 1 },
            EventKinds.StageChanged when Value(e, "stage") is { } stage => state with { Stage = stage },
            EventKinds.PieceStatus when e.Piece is not null && Value(e, "status") is { } status
                => SetPiece(state, e.Piece, e.Ts, status, verdict: null),
            EventKinds.DeliveryArrived when e.Piece is not null => SetPiece(state, e.Piece, e.Ts, "teslim", verdict: null),
            EventKinds.ReviewVerdict when e.Piece is not null && Value(e, "verdict") is { } verdict
                => SetPiece(state, e.Piece, e.Ts, status: null, verdict),
            EventKinds.GatePending when GateId(e) is { } id
                => state with { Gates = state.Gates.SetItem(id, new GateState(id, e.Piece, Value(e, "label"), e.Ts)) },
            EventKinds.GateCleared when GateId(e) is { } id => state with { Gates = state.Gates.Remove(id) },
            EventKinds.Usage when e.Data is { Count: > 0 } data => state with { Usage = state.Usage.SetItems(data) },
            _ => null,
        };

        return next is null ? state : next with { Version = state.Version + 1 };
    }

    private static string UsingTool(SiteEvent e) => $"Using {Value(e, "tool") ?? "a tool"}";

    private static SiteState SetActor(SiteState state, SiteEvent e, string activity, string? tool, string? say)
    {
        var actor = new ActorState(e.Actor.Id, e.Actor.Role, e.Actor.Name, activity, tool, e.Ts, say);
        return state with { Actors = state.Actors.SetItem(actor.Id, actor) };
    }

    private static SiteState SetPiece(SiteState state, string id, DateTimeOffset at, string? status, string? verdict)
    {
        var old = state.Pieces.GetValueOrDefault(id);
        var piece = new PieceState(id, status ?? old?.Status ?? "brif", verdict ?? old?.Verdict, at);
        return state with { Pieces = state.Pieces.SetItem(id, piece) };
    }

    private static string? GateId(SiteEvent e) => Value(e, "gate") ?? e.Piece;

    private static string? Value(SiteEvent e, string key) =>
        e.Data is not null && e.Data.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : null;
}
