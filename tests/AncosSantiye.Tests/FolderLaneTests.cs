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
    public void APattern_ReadsEveryStatusFile_EachForItsOwnProject()
    {
        var lane = new FolderLane(new FolderLaneProfile(_dir, "*-durum.md"), _hub, NullLogger<FolderLane>.Instance);
        File.WriteAllText(Path.Combine(_dir, "rovans-durum.md"), "Stage: P6 part 1\n\n" + Header + Row("rovans-001", "bitti") + Row("rovans-002", "onay bekliyor"));
        File.WriteAllText(Path.Combine(_dir, "kbyte-preview-durum.md"), "Stage: F2\n\n" + Header + Row("kbyte-preview-001", "sahada"));
        File.WriteAllText(Path.Combine(_dir, "rovans-001-check.log"), "not a status file");
        File.WriteAllText(Path.Combine(_dir, "-durum.md"), "Stage: F9\n\n" + Header + Row("nobody-001", "sahada"));

        lane.Tick(Now);

        Assert.Equal("P6 part 1", _hub.StateOf("rovans").Stage);
        Assert.Equal(2, _hub.StateOf("rovans").Pieces.Count);
        Assert.Contains("rovans-002", _hub.StateOf("rovans").Gates.Keys);
        Assert.Equal("F2", _hub.StateOf("kbyte-preview").Stage);
        Assert.DoesNotContain(_hub.Projects, p => p.Name == "nobody" || p.Name == "");
    }

    [Fact]
    public void APattern_TakesDeliveriesOnlyForProjectsWithAStatusFile()
    {
        var lane = new FolderLane(new FolderLaneProfile(_dir, "*-durum.md"), _hub, NullLogger<FolderLane>.Instance);
        File.WriteAllText(Path.Combine(_dir, "rovans-durum.md"), "Stage: P6\n\n" + Header + Row("rovans-001", "sahada"));
        lane.Tick(Now);

        File.WriteAllText(Path.Combine(_dir, "rovans-002-gemini.md"), "delivery");
        File.WriteAllText(Path.Combine(_dir, "stranger-002-gemini.md"), "delivery");
        lane.Tick(Now);

        Assert.Equal("teslim", _hub.StateOf("rovans").Pieces["rovans-002"].Status);
        Assert.DoesNotContain(_hub.Projects, p => p.Name == "stranger");
    }

    [Fact]
    public void APattern_FilesChangeOneAtATime()
    {
        var lane = new FolderLane(new FolderLaneProfile(_dir, "*-durum.md"), _hub, NullLogger<FolderLane>.Instance);
        var a = Path.Combine(_dir, "a-durum.md");
        var b = Path.Combine(_dir, "b-durum.md");
        File.WriteAllText(a, "Stage: F1\n\n" + Header + Row("a-001", "sahada"));
        File.WriteAllText(b, "Stage: F1\n\n" + Header + Row("b-001", "sahada"));
        lane.Tick(Now);
        var bVersion = _hub.StateOf("b").Version;

        File.WriteAllText(a, "Stage: F2\n\n" + Header + Row("a-001", "bitti"));
        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddMinutes(1));
        lane.Tick(Now);

        Assert.Equal("F2", _hub.StateOf("a").Stage);
        Assert.Equal(bVersion, _hub.StateOf("b").Version);
    }

    [Fact]
    public void ARowWithAnEmptyStatus_IsAPieceNotStartedYet()
    {
        WriteStatus("Stage: P6\n\n" + Header + Row("x-001", "bitti") + Row("x-002", ""));

        _lane.Tick(Now);

        Assert.Equal("bitti", _hub.State.Pieces["x-001"].Status);
        Assert.Equal("brif", _hub.State.Pieces["x-002"].Status);
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
