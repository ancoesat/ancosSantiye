using AncosSantiye.Events;
using AncosSantiye.Store;

namespace AncosSantiye.Tests;

public sealed class SiteHubTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 26, 10, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "santiye-hub-" + Guid.NewGuid());
    private readonly FixedClock _clock = new(Start.AddMinutes(1));
    private readonly SiteHub _hub;

    public SiteHubTests() => _hub = new SiteHub(new EventLog(_dir), _clock);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static SiteEvent In(string project, int second, string kind, string? piece = null, params (string, string)[] data) =>
        SiteEvent.Create(
            Sources.ClaudeCode,
            project,
            new Actor($"chief:{project}", Roles.Chief, "Claude Code"),
            kind,
            session: project,
            piece: piece,
            data: data.Length == 0 ? null : data.ToDictionary(d => d.Item1, d => d.Item2),
            ts: Start.AddSeconds(second));

    [Fact]
    public void EachProjectKeepsItsOwnState()
    {
        _hub.Publish(In("kbyte", 0, EventKinds.PieceStatus, "kbyte-001", ("status", "sahada")));
        _hub.Publish(In("rovans", 1, EventKinds.ToolStart, null, ("tool", "Read")));

        Assert.True(_hub.StateOf("kbyte").Pieces.ContainsKey("kbyte-001"));
        Assert.Empty(_hub.StateOf("kbyte").Actors);
        Assert.Empty(_hub.StateOf("rovans").Pieces);
        Assert.Single(_hub.StateOf("rovans").Actors);
    }

    [Fact]
    public void ProjectNames_MatchWithoutCase()
    {
        _hub.Publish(In("kbyte", 0, EventKinds.PieceStatus, "kbyte-001", ("status", "sahada")));
        _hub.Publish(In("KBYTE", 1, EventKinds.ToolStart, null, ("tool", "Read")));

        Assert.Single(_hub.Projects);
        Assert.Single(_hub.StateOf("Kbyte").Actors);
    }

    [Fact]
    public void TheLatestProject_IsTheDefault_AndComesFirst()
    {
        _hub.Publish(In("kbyte", 0, EventKinds.ToolStart, null, ("tool", "Read")));
        _hub.Publish(In("rovans", 5, EventKinds.ToolStart, null, ("tool", "Read")));
        _hub.Publish(In("kbyte", 3, EventKinds.ToolEnd, null, ("tool", "Read")));

        Assert.Equal(["rovans", "kbyte"], _hub.Projects.Select(p => p.Name));
        Assert.Same(_hub.StateOf("rovans"), _hub.State);
        Assert.Same(_hub.StateOf("rovans"), _hub.Subscribe().Snapshot);
        Assert.Same(_hub.StateOf("kbyte"), _hub.Subscribe("kbyte").Snapshot);
    }

    [Fact]
    public void AnOlderEventArrivingLate_DoesNotPushAProjectBack()
    {
        _hub.Publish(In("rovans", 5, EventKinds.ToolStart, null, ("tool", "Read")));
        _hub.Publish(In("kbyte", 10, EventKinds.ToolStart, null, ("tool", "Read")));
        _hub.Publish(In("kbyte", 3, EventKinds.ToolEnd, null, ("tool", "Read")));

        Assert.Equal("kbyte", _hub.Projects[0].Name);
    }

    [Fact]
    public void Projects_CarryTheirWaitingGates()
    {
        _hub.Publish(In("kbyte", 0, EventKinds.GatePending, "kbyte-003", ("label", "Plan")));
        _hub.Publish(In("rovans", 1, EventKinds.ToolStart, null, ("tool", "Read")));

        var kbyte = _hub.Projects.Single(p => p.Name == "kbyte");
        Assert.Equal("kbyte-003", Assert.Single(kbyte.Gates).Id);
        Assert.Empty(_hub.Projects.Single(p => p.Name == "rovans").Gates);
        Assert.Equal(1, _hub.Projects.Single(p => p.Name == "rovans").Workers);
    }

    [Fact]
    public void Load_RebuildsEveryProjectFromTheLog()
    {
        _hub.Publish(In("kbyte", 0, EventKinds.GatePending, "kbyte-003", ("label", "Plan")));
        _hub.Publish(In("rovans", 1, EventKinds.ToolStart, null, ("tool", "Read")));
        var fresh = new SiteHub(new EventLog(_dir), _clock);

        fresh.Load(DateOnly.FromDateTime(Start.UtcDateTime));

        Assert.Equal(["rovans", "kbyte"], fresh.Projects.Select(p => p.Name));
        Assert.Single(fresh.StateOf("kbyte").Gates);
    }

    [Fact]
    public void SilentWorkers_AreNotServed_ButStayInTheState()
    {
        _hub.Publish(In("kbyte", 0, EventKinds.ToolStart, null, ("tool", "Read")));
        _clock.Now = Start.AddMinutes(16);

        Assert.Empty(_hub.StateOf("kbyte").Actors);
        Assert.Empty(_hub.Subscribe("kbyte").Snapshot.Actors);
        Assert.Equal(0, _hub.Projects[0].Workers);

        _clock.Now = Start.AddMinutes(14);
        Assert.Single(_hub.StateOf("kbyte").Actors);
    }

    [Fact]
    public void Load_CanLeaveEventsOut()
    {
        _hub.Publish(SiteEvent.Create(Sources.Folder, "ancosSantiye", new Actor("folder", Roles.Chief), EventKinds.GatePending,
            piece: "santiye-f8", ts: Start));
        _hub.Publish(In("kbyte", 1, EventKinds.GatePending, "kbyte-003", ("label", "Plan")));
        var fresh = new SiteHub(new EventLog(_dir), _clock);

        fresh.Load(DateOnly.FromDateTime(Start.UtcDateTime), e => e.Source != Sources.Folder);

        Assert.Empty(fresh.StateOf("ancosSantiye").Gates);
        Assert.Single(fresh.StateOf("kbyte").Gates);
    }

    [Fact]
    public void NothingYet_IsEmpty()
    {
        Assert.Empty(_hub.Projects);
        Assert.Equal(0, _hub.State.Version);
        Assert.Equal(0, _hub.StateOf("nope").Version);
    }
}
