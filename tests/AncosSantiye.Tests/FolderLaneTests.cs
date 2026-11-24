using AncosSantiye.Adapters;
using AncosSantiye.Profiles;
using AncosSantiye.Store;
using Microsoft.Extensions.Logging.Abstractions;

namespace AncosSantiye.Tests;

public sealed class FolderLaneTests : IDisposable
{
    private const string Header = "| id | piece | lane / model | env / risk | status | reviewer | fix rounds | notes |\n|---|---|---|---|---|---|---|---|\n";
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "santiye-lane-" + Guid.NewGuid());
    private readonly SiteHub _hub;
    private readonly FolderLane _lane;
    private int _writes;

    public FolderLaneTests()
    {
        Directory.CreateDirectory(_dir);
        _hub = new SiteHub(new EventLog(Path.Combine(_dir, "log")));
        _lane = new FolderLane(new FolderLaneProfile(_dir, "x-durum.md", "x"), _hub, NullLogger<FolderLane>.Instance);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void WriteStatus(string body)
    {
        var path = Path.Combine(_dir, "x-durum.md");
        File.WriteAllText(path, body);
        // A fresh time on every write, so fast tests never see two writes in the same tick of the file clock.
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(++_writes));
    }

    private static string Row(string id, string status) => $"| {id} | Piece {id} | claude / opus | draft / low | {status} | | 0 | |\n";

    [Fact]
    public void FirstRead_PublishesStageAndPendingGate()
    {
        WriteStatus("Aşama: F2\n\n" + Header + Row("x-001", "bitti") + Row("x-002", "onay bekliyor"));

        _lane.Tick(Now);

        Assert.Equal("F2", _hub.State.Stage);
        Assert.Contains("x-002", _hub.State.Gates.Keys);
    }

    [Fact]
    public void UnchangedFile_IsNotReadAgain()
    {
        WriteStatus("Aşama: F2\n\n" + Header + Row("x-001", "sahada"));
        _lane.Tick(Now);
        var version = _hub.State.Version;

        _lane.Tick(Now);

        Assert.Equal(version, _hub.State.Version);
    }

    [Fact]
    public void Approval_ClearsTheGate()
    {
        WriteStatus("Aşama: F2\n\n" + Header + Row("x-002", "onay bekliyor"));
        _lane.Tick(Now);

        WriteStatus("Aşama: F2\n\n" + Header + Row("x-002", "bitti"));
        _lane.Tick(Now);

        Assert.Empty(_hub.State.Gates);
    }

    [Fact]
    public void HalfWrittenFile_KeepsTheGatesAndIsReadAgainWhenFinished()
    {
        WriteStatus("Aşama: F2\n\n" + Header + Row("x-002", "onay bekliyor"));
        _lane.Tick(Now);

        WriteStatus("Aşama: F2\n\n");
        _lane.Tick(Now);
        Assert.Contains("x-002", _hub.State.Gates.Keys);

        WriteStatus("Aşama: F3\n\n" + Header + Row("x-002", "onay bekliyor"));
        _lane.Tick(Now);
        Assert.Equal("F3", _hub.State.Stage);
        Assert.Contains("x-002", _hub.State.Gates.Keys);
    }

    [Fact]
    public void Deliveries_OnlyNewOnesForThisProjectArrive()
    {
        File.WriteAllText(Path.Combine(_dir, "x-001-gemini.md"), "old");
        _lane.Tick(Now);
        Assert.Equal(0, _hub.State.Version);

        File.WriteAllText(Path.Combine(_dir, "other-002-gemini.md"), "not ours");
        File.WriteAllText(Path.Combine(_dir, "notes.md"), "not a delivery");
        _lane.Tick(Now);
        Assert.Equal(0, _hub.State.Version);

        File.WriteAllText(Path.Combine(_dir, "x-003-studio.md"), "new");
        _lane.Tick(Now);
        _lane.Tick(Now);

        Assert.Equal(1, _hub.State.Version);
        Assert.Equal("teslim", _hub.State.Pieces["x-003"].Status);
    }

    [Fact]
    public void MissingFolder_IsQuiet()
    {
        var lane = new FolderLane(new FolderLaneProfile(Path.Combine(_dir, "nope"), "a.md", "x"), _hub, NullLogger<FolderLane>.Instance);

        lane.Tick(Now);

        Assert.Equal(0, _hub.State.Version);
    }
}
