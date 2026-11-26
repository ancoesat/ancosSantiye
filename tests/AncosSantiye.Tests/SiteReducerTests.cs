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

    private static SiteEvent Task(int second, string kind, string session, string id, string? subject = null)
    {
        var data = subject is null
            ? new Dictionary<string, string> { ["task"] = id }
            : new Dictionary<string, string> { ["task"] = id, ["subject"] = subject };
        return SiteEvent.Create(Sources.ClaudeCode, "demo", Ev.Chief, kind, session: session, data: data,
            ts: new DateTimeOffset(2026, 11, 26, 10, 0, second, TimeSpan.Zero));
    }

    [Fact]
    public void Tasks_CountOnceEvenIfTheHookRepeats()
    {
        var s = SiteReducer.Replay([
            Task(0, EventKinds.TaskCreated, "a", "1", "Map the code"),
            Task(1, EventKinds.TaskCreated, "a", "1", "Map the code"),
            Task(2, EventKinds.TaskCreated, "a", "2", "Write tests"),
            Task(3, EventKinds.TaskCompleted, "a", "1"),
            Task(4, EventKinds.TaskCompleted, "a", "1"),
            Task(5, EventKinds.TaskCreated, "a", "1", "Map the code"),
        ]);

        Assert.Equal((2, 1), (s.TasksPlanned, s.TasksDone));
        Assert.Equal("Write tests", s.CurrentTask);
    }

    [Fact]
    public void Tasks_TheNewestListCounts_NotAnOldFinishedOne()
    {
        var s = SiteReducer.Replay([
            Task(0, EventKinds.TaskCreated, "old", "1", "Old work"),
            Task(1, EventKinds.TaskCompleted, "old", "1"),
            Task(5, EventKinds.TaskCreated, "new", "1", "Plan"),
            Task(6, EventKinds.TaskCreated, "new", "2", "Build"),
        ]);

        Assert.Equal((2, 0), (s.TasksPlanned, s.TasksDone));
        Assert.Equal("Plan", s.CurrentTask);
        Assert.Equal(3, s.Tasks.Count);
    }

    [Fact]
    public void Tasks_FollowOrderOfCreation_AndAllDoneHasNoCurrentTask()
    {
        var s = SiteReducer.Replay([
            Task(0, EventKinds.TaskCreated, "a", "b", "First"),
            Task(1, EventKinds.TaskCreated, "a", "a", "Second"),
        ]);

        Assert.Equal("First", s.CurrentTask);

        s = SiteReducer.Reduce(s, Task(2, EventKinds.TaskCompleted, "a", "b"));

        Assert.Equal("Second", s.CurrentTask);

        s = SiteReducer.Reduce(s, Task(3, EventKinds.TaskCompleted, "a", "a"));

        Assert.Equal((2, 2), (s.TasksPlanned, s.TasksDone));
        Assert.Null(s.CurrentTask);
    }

    [Fact]
    public void Tasks_ACompletionWeNeverSawCreated_StillCounts()
    {
        var s = SiteReducer.Replay([Task(0, EventKinds.TaskCompleted, "a", "9", "Late")]);

        Assert.Equal((1, 1), (s.TasksPlanned, s.TasksDone));
    }

    [Fact]
    public void Tasks_TheirCountsReachThePage()
    {
        var json = Json(SiteReducer.Replay([Task(0, EventKinds.TaskCreated, "a", "1", "Plan")]));

        Assert.Contains("\"tasksPlanned\":1", json);
        Assert.Contains("\"tasksDone\":0", json);
        Assert.Contains("\"currentTask\":\"Plan\"", json);
    }

    [Theory]
    [InlineData("Read", Doing.Read)]
    [InlineData("Grep", Doing.Read)]
    [InlineData("WebSearch", Doing.Read)]
    [InlineData("Edit", Doing.Build)]
    [InlineData("Write", Doing.Build)]
    [InlineData("Bash", Doing.Run)]
    [InlineData("PowerShell", Doing.Run)]
    [InlineData("AskUserQuestion", Doing.Wait)]
    [InlineData("Task", Doing.Plan)]
    [InlineData("TodoWrite", Doing.Plan)]
    [InlineData("mcp__github__x", Doing.Plan)]
    public void Action_FollowsTheTool(string tool, string action)
    {
        var s = SiteReducer.Replay([Ev.At(0, EventKinds.ToolStart, data: ("tool", tool))]);

        Assert.Equal(action, s.Actors["chief"].Action);
    }

    [Fact]
    public void Action_StaysBetweenTools_AndWaitsAfterTheTurn()
    {
        var s = SiteReducer.Replay([
            Ev.At(0, EventKinds.SessionStart),
            Ev.At(1, EventKinds.ToolStart, data: ("tool", "Edit")),
            Ev.At(2, EventKinds.ToolEnd, data: ("tool", "Edit")),
        ]);

        Assert.Equal(Doing.Build, s.Actors["chief"].Action);

        s = SiteReducer.Reduce(s, Ev.At(3, EventKinds.ToolFail, data: ("tool", "Bash")));
        Assert.Equal(Doing.Fail, s.Actors["chief"].Action);

        s = SiteReducer.Reduce(s, Ev.At(4, EventKinds.TurnStop));
        Assert.Equal(Doing.Wait, s.Actors["chief"].Action);
        Assert.Equal(Doing.Start, SiteReducer.Replay([Ev.At(0, EventKinds.SessionStart)]).Actors["chief"].Action);
    }

    [Fact]
    public void Usage_IsReplaced_AndKnowsItsTime()
    {
        var s = SiteReducer.Replay([
            Ev.At(0, EventKinds.Usage, null, null, ("contextUsedPercentage", "8"), ("fiveHourUsedPercentage", "23.5")),
            Ev.At(9, EventKinds.Usage, null, null, ("contextUsedPercentage", "12")),
        ]);

        Assert.Equal("12", s.Usage["contextUsedPercentage"]);
        Assert.False(s.Usage.ContainsKey("fiveHourUsedPercentage"));
        Assert.Equal(new DateTimeOffset(2026, 11, 24, 20, 0, 9, TimeSpan.Zero), s.UsageUpdated);
        Assert.Contains("\"usageUpdated\"", Json(s));
    }

    [Fact]
    public void Silence_DropsQuietWorkers_WaitingOnesLater()
    {
        var s = SiteReducer.Replay([
            Ev.At(0, EventKinds.ToolStart, Ev.Worker("busy"), data: ("tool", "Read")),
            Ev.At(0, EventKinds.TurnStop),
        ]);
        var start = s.Actors["chief"].LastSeen;

        Assert.Same(s, Silence.Drop(s, start.AddMinutes(14)));
        Assert.Equal(["chief"], Silence.Drop(s, start.AddMinutes(16)).Actors.Keys);
        Assert.Equal(["chief"], Silence.Drop(s, start.AddMinutes(59)).Actors.Keys);
        Assert.Empty(Silence.Drop(s, start.AddMinutes(61)).Actors);
    }

    private static SiteEvent PlanEvent(int second, params (string, string)[] plan)
    {
        var data = new Dictionary<string, string> { ["tool"] = "mcp__ccd_turn__update_plan" };
        foreach (var (k, v) in plan)
        {
            data[k] = v;
        }

        return SiteEvent.Create(Sources.ClaudeCode, "demo", Ev.Chief, EventKinds.ToolStart, session: "d", data: data,
            ts: new DateTimeOffset(2026, 11, 26, 10, 0, second, TimeSpan.Zero));
    }

    [Fact]
    public void DesktopPlan_IsTheTaskList()
    {
        var s = SiteReducer.Replay([PlanEvent(0,
            ("planItems", "3"), ("step1", "completed|Check health"), ("step2", "in_progress|Read the log"), ("step3", "pending|Write report"))]);

        Assert.Equal((3, 1), (s.TasksPlanned, s.TasksDone));
        Assert.Equal("Read the log", s.CurrentTask);
        Assert.Equal(Doing.Plan, s.Actors["chief"].Action);
    }

    [Fact]
    public void DesktopPlan_Advance_Done_Start_AndReplace()
    {
        var s = SiteReducer.Replay([
            PlanEvent(0, ("planItems", "4"), ("step1", "in_progress|A"), ("step2", "pending|B"), ("step3", "pending|C"), ("step4", "pending|D")),
            PlanEvent(1, ("planAdvance", "true")),
        ]);
        Assert.Equal((4, 1), (s.TasksPlanned, s.TasksDone));
        Assert.Equal("B", s.CurrentTask);

        s = SiteReducer.Reduce(s, PlanEvent(2, ("planDone", "2,3")));
        Assert.Equal((4, 3), (s.TasksPlanned, s.TasksDone));
        Assert.Equal("D", s.CurrentTask);

        s = SiteReducer.Replay([
            PlanEvent(0, ("planItems", "3"), ("step1", "pending|A"), ("step2", "pending|B"), ("step3", "pending|C")),
            PlanEvent(1, ("planStart", "3")),
        ]);
        Assert.Equal("C", s.CurrentTask);

        s = SiteReducer.Reduce(s, PlanEvent(2, ("planItems", "1"), ("step1", "pending|Only")));
        Assert.Equal((1, 0), (s.TasksPlanned, s.TasksDone));
        Assert.Equal("Only", s.CurrentTask);
    }

    [Fact]
    public void DesktopPlan_KeepsOrderPastNineSteps()
    {
        var steps = Enumerable.Range(1, 11).Select(n => ($"step{n}", n == 10 ? "in_progress|Ten" : n < 10 ? $"completed|{n}" : "pending|Eleven"));
        var s = SiteReducer.Replay([PlanEvent(0, [("planItems", "11"), .. steps])]);

        Assert.Equal("Ten", s.CurrentTask);
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8", "9", "Ten", "Eleven"], s.CurrentTasks.Select(t => t.Subject));
    }

    private static SiteEvent Job(int second, string kind, string session, string id, string? lane, string say = "Run")
    {
        var data = new Dictionary<string, string> { ["job"] = id, ["say"] = say };
        if (lane is not null)
        {
            data["lane"] = lane;
        }

        return SiteEvent.Create(Sources.ClaudeCode, "demo", new Actor($"job:{session}:{id}", Roles.Subcontractor, lane ?? "Background job"),
            kind, session: session, data: data, ts: new DateTimeOffset(2026, 11, 26, 10, 0, second, TimeSpan.Zero));
    }

    [Fact]
    public void Jobs_StartWork_ReplaceTheirOwnLane_AndStop()
    {
        var s = SiteReducer.Replay([
            Job(0, EventKinds.JobStart, "s1", "a1", "agy", "Run rovans-010 on agy"),
            Job(1, EventKinds.JobStart, "s1", "o1", "opencode"),
            Job(2, EventKinds.JobStart, "s2", "a9", "agy"),
        ]);

        var agy = s.Actors["job:s1:a1"];
        Assert.Equal((Roles.Subcontractor, Activities.Working, Doing.Build, "Run rovans-010 on agy"), (agy.Role, agy.Activity, agy.Action, agy.Say));

        s = SiteReducer.Reduce(s, Job(3, EventKinds.JobStart, "s1", "a2", "agy"));
        Assert.Equal(["job:s1:a2", "job:s1:o1", "job:s2:a9"], s.Actors.Keys);

        s = SiteReducer.Reduce(s, Job(4, EventKinds.JobStop, "s1", "o1", null));
        Assert.Equal(["job:s1:a2", "job:s2:a9"], s.Actors.Keys);

        s = SiteReducer.Reduce(s, SiteEvent.Create(Sources.ClaudeCode, "demo", new Actor("chief:s1", Roles.Chief), EventKinds.SessionEnd,
            session: "s1", ts: new DateTimeOffset(2026, 11, 26, 10, 0, 5, TimeSpan.Zero)));
        Assert.Equal(["job:s2:a9"], s.Actors.Keys);
    }

    [Fact]
    public void Jobs_WithoutALane_AreNotWorkers()
    {
        var s = SiteReducer.Replay([Job(0, EventKinds.JobStart, "s1", "d1", null), Job(1, EventKinds.JobStart, "s1", "d2", null)]);

        Assert.Empty(s.Actors);
    }

    [Fact]
    public void APrompt_SetsTheWorkerThinking()
    {
        var s = SiteReducer.Replay([Ev.At(0, EventKinds.TurnStop), Ev.At(1, EventKinds.TurnStart)]);

        Assert.Equal((Saying.Thinking, Doing.Plan, Activities.Working), (s.Actors["chief"].Say, s.Actors["chief"].Action, s.Actors["chief"].Activity));
    }

    [Fact]
    public void Silence_GivesJobsHalfAnHour()
    {
        var s = SiteReducer.Replay([Job(0, EventKinds.JobStart, "s1", "a1", "agy")]);
        var start = s.Actors["job:s1:a1"].LastSeen;

        Assert.Single(Silence.Drop(s, start.AddMinutes(29)).Actors);
        Assert.Empty(Silence.Drop(s, start.AddMinutes(31)).Actors);
    }
}
