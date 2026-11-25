using System.Text.Json;
using AncosSantiye.Events;
using AncosSantiye.Scene;

namespace AncosSantiye.Tests;

public class SiteReducerTests
{
    private static readonly SiteEvent[] Story =
    [
        Ev.At(0, EventKinds.SessionStart),
        Ev.At(1, EventKinds.StageChanged, data: ("stage", "F1")),
        Ev.At(2, EventKinds.AgentStart, Ev.Worker()),
        Ev.At(3, EventKinds.ToolStart, Ev.Worker(), data: ("tool", "Grep")),
        Ev.At(4, EventKinds.TaskCreated),
        Ev.At(5, EventKinds.TaskCreated),
        Ev.At(6, EventKinds.TaskCompleted),
        Ev.At(7, EventKinds.PieceStatus, piece: "demo-001", data: ("status", "sahada")),
        Ev.At(8, EventKinds.GatePending, null, "demo-001", ("gate", "K3"), ("label", "approval note")),
        Ev.At(9, EventKinds.Usage, null, null, ("contextUsedPercentage", "8"), ("fiveHourUsedPercentage", "23.5")),
    ];

    private static string Json(SiteState s) => JsonSerializer.Serialize(s, EventJson.Options);

    [Fact]
    public void Replay_TwiceGivesTheSameState()
    {
        Assert.Equal(Json(SiteReducer.Replay(Story)), Json(SiteReducer.Replay(Story)));
    }

    [Fact]
    public void Replay_AfterJsonRoundTripGivesTheSameState()
    {
        var roundTripped = Story
            .Select(e => JsonSerializer.Serialize(e, EventJson.Options))
            .Select(line => JsonSerializer.Deserialize<SiteEvent>(line, EventJson.Options)!);

        Assert.Equal(Json(SiteReducer.Replay(Story)), Json(SiteReducer.Replay(roundTripped)));
    }

    [Fact]
    public void Story_BuildsTheExpectedSite()
    {
        var s = SiteReducer.Replay(Story);

        Assert.Equal("F1", s.Stage);
        Assert.Equal(Activities.Working, s.Actors["agent_1"].Activity);
        Assert.Equal("Grep", s.Actors["agent_1"].Tool);
        Assert.Equal(Activities.Idle, s.Actors["chief"].Activity);
        Assert.Equal((2, 1), (s.TasksPlanned, s.TasksDone));
        Assert.Equal("sahada", s.Pieces["demo-001"].Status);
        Assert.Equal("approval note", s.Gates["K3"].Label);
        Assert.Equal("23.5", s.Usage["fiveHourUsedPercentage"]);
        Assert.Equal(Story.Length, s.Version);
    }

    [Fact]
    public void ToolEnd_ReturnsTheActorToIdle()
    {
        var s = SiteReducer.Replay([.. Story, Ev.At(10, EventKinds.ToolEnd, Ev.Worker())]);

        Assert.Equal(Activities.Idle, s.Actors["agent_1"].Activity);
        Assert.Null(s.Actors["agent_1"].Tool);
    }

    [Fact]
    public void AgentStop_RemovesTheWorker()
    {
        var s = SiteReducer.Replay([.. Story, Ev.At(10, EventKinds.AgentStop, Ev.Worker())]);

        Assert.False(s.Actors.ContainsKey("agent_1"));
        Assert.True(s.Actors.ContainsKey("chief"));
    }

    [Fact]
    public void GateCleared_RemovesTheGate()
    {
        var s = SiteReducer.Replay([.. Story, Ev.At(10, EventKinds.GateCleared, data: ("gate", "K3"))]);

        Assert.Empty(s.Gates);
    }

    [Fact]
    public void ReviewVerdict_KeepsTheStatusAndAddsTheVerdict()
    {
        var s = SiteReducer.Replay([.. Story, Ev.At(10, EventKinds.ReviewVerdict, piece: "demo-001", data: ("verdict", "ONAY"))]);

        Assert.Equal("sahada", s.Pieces["demo-001"].Status);
        Assert.Equal("ONAY", s.Pieces["demo-001"].Verdict);
    }

    [Fact]
    public void UnknownKind_LeavesTheStateUntouched()
    {
        var before = SiteReducer.Replay(Story);
        var after = SiteReducer.Reduce(before, Ev.At(10, "something.new"));

        Assert.Same(before, after);
    }

    [Fact]
    public void EventsMissingRequiredData_AreIgnored()
    {
        var before = SiteReducer.Replay(Story);

        Assert.Same(before, SiteReducer.Reduce(before, Ev.At(10, EventKinds.StageChanged)));
        Assert.Same(before, SiteReducer.Reduce(before, Ev.At(11, EventKinds.PieceStatus, data: ("status", "bitti"))));
        Assert.Same(before, SiteReducer.Reduce(before, Ev.At(12, EventKinds.GatePending)));
    }

    private static string? SayAfter(params SiteEvent[] events) => SiteReducer.Replay(events).Actors["chief"].Say;

    [Fact]
    public void Bubble_FollowsWhatTheActorDoes()
    {
        Assert.Equal(Saying.Starting, SayAfter(Ev.At(0, EventKinds.SessionStart)));
        Assert.Equal("Reading a.cs", SayAfter(Ev.At(0, EventKinds.ToolStart, data: [("tool", "Read"), ("say", "Reading a.cs")])));
        Assert.Equal("Using Read", SayAfter(Ev.At(0, EventKinds.ToolStart, data: ("tool", "Read"))));
        Assert.Equal("Failed: Run tests", SayAfter(Ev.At(0, EventKinds.ToolFail, data: [("tool", "Bash"), ("say", "Run tests")])));
        Assert.Equal(Saying.Waiting, SayAfter(
            Ev.At(0, EventKinds.ToolStart, data: [("tool", "Read"), ("say", "Reading a.cs")]),
            Ev.At(1, EventKinds.TurnStop)));
    }

    [Fact]
    public void Bubble_KeepsTheLastActionBetweenTools()
    {
        var state = SiteReducer.Replay([
            Ev.At(0, EventKinds.ToolStart, data: [("tool", "Read"), ("say", "Reading a.cs")]),
            Ev.At(1, EventKinds.ToolEnd, data: ("tool", "Read")),
        ]);

        Assert.Equal("Reading a.cs", state.Actors["chief"].Say);
        Assert.Equal(Activities.Idle, state.Actors["chief"].Activity);
    }
}
