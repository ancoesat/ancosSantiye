using System.Collections.Immutable;
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
            EventKinds.TurnStart => SetActor(state, e, Activities.Working, tool: null, Saying.Thinking, Doing.Plan),
            // Between two tools the bubble and the place stay, so nothing flickers or walks back and forth.
            EventKinds.ToolEnd => SetActor(state, e, Activities.Idle, tool: null, Previous(state, e)?.Say, Previous(state, e)?.Action),
            EventKinds.ToolStart => ApplyPlan(
                SetActor(state, e, Activities.Working, Value(e, "tool"), Value(e, "say") ?? UsingTool(e), Doing.Of(Value(e, "tool"))), e),
            EventKinds.ToolFail => SetActor(state, e, Activities.Failed, Value(e, "tool"), "Failed: " + (Value(e, "say") ?? UsingTool(e)), Doing.Fail),
            EventKinds.AgentStop or EventKinds.JobStop => state with { Actors = state.Actors.Remove(e.Actor.Id) },
            // A session that ends takes its background jobs with it.
            EventKinds.SessionEnd => state with
            {
                Actors = state.Actors.Remove(e.Actor.Id).RemoveRange(
                    state.Actors.Keys.Where(k => k.StartsWith($"job:{e.Session ?? ""}:", StringComparison.Ordinal))),
            },
            EventKinds.JobStart => StartJob(state, e),
            EventKinds.TaskCreated => AddTask(state, e),
            EventKinds.TaskCompleted => CompleteTask(state, e),
            EventKinds.StageChanged when Value(e, "stage") is { } stage => state with { Stage = stage },
            // An empty status cell is a piece nobody started yet ("brif"), not a row to ignore.
            EventKinds.PieceStatus when e.Piece is not null && e.Data?.ContainsKey("status") == true
                => SetPiece(state, e.Piece, e.Ts, Value(e, "status") ?? "brif", verdict: null),
            EventKinds.DeliveryArrived when e.Piece is not null => SetPiece(state, e.Piece, e.Ts, "teslim", verdict: null),
            EventKinds.ReviewVerdict when e.Piece is not null && Value(e, "verdict") is { } verdict
                => SetPiece(state, e.Piece, e.Ts, status: null, verdict),
            EventKinds.GatePending when GateId(e) is { } id
                => state with { Gates = state.Gates.SetItem(id, new GateState(id, e.Piece, Value(e, "label"), e.Ts)) },
            EventKinds.GateCleared when GateId(e) is { } id => state with { Gates = state.Gates.Remove(id) },
            // Replaced, not merged: a value the status line stopped sending (a limit window that reset) must go.
            EventKinds.Usage when e.Data is { Count: > 0 } data
                => state with { Usage = ImmutableSortedDictionary.CreateRange(data), UsageUpdated = e.Ts },
            _ => null,
        };

        if (next is not null && SelfReporting(e))
        {
            next = DropStandIns(next, e);
        }

        return next is null ? state : next with { Version = state.Version + 1 };
    }

    /// <summary>An agent that sends its own hooks (agy, OpenCode), not a job Claude only started.</summary>
    private static bool SelfReporting(SiteEvent e) =>
        e.Source is Sources.Agy or Sources.OpenCode && e.Kind is not (EventKinds.AgentStop or EventKinds.SessionEnd);

    /// <summary>
    /// Claude's background job of the same program stood in for this agent until it reported itself.
    /// Now the real one is on the site, so the stand-in goes: one worker per run, not two.
    /// </summary>
    private static SiteState DropStandIns(SiteState state, SiteEvent e)
    {
        var standIns = state.Actors
            .Where(a => a.Key.StartsWith("job:", StringComparison.Ordinal)
                        && string.Equals(a.Value.Name, e.Actor.Name, StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Key).ToArray();
        return standIns.Length == 0 ? state : state with { Actors = state.Actors.RemoveRange(standIns) };
    }

    private static string UsingTool(SiteEvent e) => $"Using {Value(e, "tool") ?? "a tool"}";

    /// <summary>A new run of a lane replaces the session's earlier run of the same lane (that one is over).</summary>
    private static SiteState StartJob(SiteState state, SiteEvent e)
    {
        // Only lanes are workers. A wait or a dev server in the background is something Claude waits on,
        // not someone working: it stays a line in Activity.
        if (Value(e, "lane") is not { } lane)
        {
            return state;
        }

        // The program already reports itself (its hooks came before Claude's result did): no stand-in.
        if (state.Actors.Any(a => !a.Key.StartsWith("job:", StringComparison.Ordinal)
                                  && a.Value.Role == Roles.Subcontractor
                                  && string.Equals(a.Value.Name, lane, StringComparison.OrdinalIgnoreCase)))
        {
            return state;
        }

        var older = state.Actors.Where(a => a.Key != e.Actor.Id
                    && a.Key.StartsWith($"job:{e.Session ?? ""}:", StringComparison.Ordinal)
                    && string.Equals(a.Value.Name, lane, StringComparison.OrdinalIgnoreCase))
                .Select(a => a.Key).ToArray();
        var cleared = older.Length == 0 ? state : state with { Actors = state.Actors.RemoveRange(older) };
        return SetActor(cleared, e, Activities.Working, tool: null, Value(e, "say"), Doing.Build);
    }

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

    /// <summary>
    /// The desktop app's plan (see ClaudeHookMapper.AddPlan). A whole list replaces the session's plan steps;
    /// done, start and advance change them by step number. Steps are tasks "p1".."p12" of the session.
    /// </summary>
    private static SiteState ApplyPlan(SiteState state, SiteEvent e)
    {
        if (e.Data is not { } data)
        {
            return state;
        }

        var session = e.Session ?? "";
        string Key(int n) => TaskKey(e, $"p{n}");
        var tasks = state.Tasks;

        if (data.TryGetValue("planItems", out var countText) && int.TryParse(countText, out var count))
        {
            tasks = tasks.RemoveRange(tasks.Keys.Where(k => k.StartsWith($"{session}/p", StringComparison.Ordinal)));
            for (var n = 1; n <= count; n++)
            {
                if (data.TryGetValue($"step{n}", out var step) && step.Split('|', 2) is [var status, var title])
                {
                    // Created moves one tick per step, so the list keeps its order.
                    tasks = tasks.SetItem(Key(n), new TaskState(
                        $"p{n}", session, title, Done: status == "completed", e.Ts.AddTicks(n), e.Ts, Active: status == "in_progress"));
                }
            }
        }

        TaskState? At(int n) => tasks.GetValueOrDefault(Key(n));

        foreach (var text in (data.GetValueOrDefault("planDone") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(text, out var n) && At(n) is { } t)
            {
                tasks = tasks.SetItem(Key(n), t with { Done = true, Active = false, Updated = e.Ts });
            }
        }

        if (data.GetValueOrDefault("planAdvance") == "true")
        {
            var steps = Enumerable.Range(1, ClaudeStepLimit).Select(n => (n, t: At(n))).Where(x => x.t is not null).ToArray();
            var active = steps.FirstOrDefault(x => x.t!.Active && !x.t.Done);
            if (active.t is not null)
            {
                tasks = tasks.SetItem(Key(active.n), active.t with { Done = true, Active = false, Updated = e.Ts });
            }

            var next = steps.FirstOrDefault(x => !x.t!.Done && x.n != active.n && x.n > active.n);
            if (next.t is not null)
            {
                tasks = tasks.SetItem(Key(next.n), next.t with { Active = true, Updated = e.Ts });
            }
        }

        if (int.TryParse(data.GetValueOrDefault("planStart"), out var start) && At(start) is { } started)
        {
            foreach (var (key, task) in tasks.Where(t => t.Value.Session == session && t.Value.Active).ToArray())
            {
                tasks = tasks.SetItem(key, task with { Active = false });
            }

            tasks = tasks.SetItem(Key(start), started with { Active = true, Updated = e.Ts });
        }

        return tasks == state.Tasks ? state : state with { Tasks = tasks };
    }

    private const int ClaudeStepLimit = 12;

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
