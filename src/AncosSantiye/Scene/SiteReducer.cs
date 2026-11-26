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
            EventKinds.SessionStart or EventKinds.AgentStart => SetActor(state, e, Activities.Idle, tool: null, Saying.Starting, Doing.Start),
            EventKinds.TurnStop => SetActor(state, e, Activities.Idle, tool: null, Saying.Waiting, Doing.Wait),
            // Between two tools the bubble and the place stay, so nothing flickers or walks back and forth.
            EventKinds.ToolEnd => SetActor(state, e, Activities.Idle, tool: null, Previous(state, e)?.Say, Previous(state, e)?.Action),
            EventKinds.ToolStart => SetActor(state, e, Activities.Working, Value(e, "tool"), Value(e, "say") ?? UsingTool(e), Doing.Of(Value(e, "tool"))),
            EventKinds.ToolFail => SetActor(state, e, Activities.Failed, Value(e, "tool"), "Failed: " + (Value(e, "say") ?? UsingTool(e)), Doing.Fail),
            EventKinds.SessionEnd or EventKinds.AgentStop => state with { Actors = state.Actors.Remove(e.Actor.Id) },
            EventKinds.TaskCreated => AddTask(state, e),
            EventKinds.TaskCompleted => CompleteTask(state, e),
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

    private static ActorState? Previous(SiteState state, SiteEvent e) => state.Actors.GetValueOrDefault(e.Actor.Id);

    private static SiteState SetActor(SiteState state, SiteEvent e, string activity, string? tool, string? say, string? action)
    {
        var actor = new ActorState(e.Actor.Id, e.Actor.Role, e.Actor.Name, activity, tool, e.Ts, say, action);
        return state with { Actors = state.Actors.SetItem(actor.Id, actor) };
    }

    // Tasks are keyed by session and task id, so a repeated hook never counts a task twice.
    private static string TaskKey(SiteEvent e, string id) => $"{e.Session ?? ""}/{id}";

    private static SiteState AddTask(SiteState state, SiteEvent e)
    {
        var id = Value(e, "task") ?? e.Id.ToString();
        var key = TaskKey(e, id);
        if (state.Tasks.ContainsKey(key))
        {
            return state;
        }

        var task = new TaskState(id, e.Session ?? "", Value(e, "subject"), Done: false, e.Ts, e.Ts);
        return state with { Tasks = state.Tasks.SetItem(key, task) };
    }

    private static SiteState CompleteTask(SiteState state, SiteEvent e)
    {
        // Without an id (older hooks), the oldest open task of the session is the one that got done.
        var key = Value(e, "task") is { } id
            ? TaskKey(e, id)
            : state.Tasks.Where(t => t.Value.Session == (e.Session ?? "") && !t.Value.Done)
                .OrderBy(t => t.Value.Created).Select(t => t.Key).FirstOrDefault();

        if (key is null)
        {
            return state;
        }

        var old = state.Tasks.GetValueOrDefault(key);
        var task = old is null
            ? new TaskState(Value(e, "task") ?? e.Id.ToString(), e.Session ?? "", Value(e, "subject"), Done: true, e.Ts, e.Ts)
            : old with { Done = true, Subject = old.Subject ?? Value(e, "subject"), Updated = e.Ts };
        return state with { Tasks = state.Tasks.SetItem(key, task) };
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
