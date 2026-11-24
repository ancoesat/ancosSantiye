using AncosSantiye.Adapters;
using AncosSantiye.Events;

namespace AncosSantiye.Tests;

public class StatusFileTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 24, 20, 0, 0, TimeSpan.Zero);

    private const string Example = """
        # ancosSantiye durum

        Aşama: F1

        | id | piece | lane / model | env / risk | status | reviewer | fix rounds | notes |
        |---|---|---|---|---|---|---|---|
        | santiye-f0 | F0 hazırlık | claude / opus | draft / mid | bitti | | 0 | |
        | santiye-f1 | F1 çekirdek | claude / opus | draft / mid | onay bekliyor | ONAY | 1 | 20 test |
        """;

    [Fact]
    public void Parse_ReadsTheExampleFile()
    {
        var snapshot = StatusFile.Parse(Example);

        Assert.Equal("F1", snapshot.Stage);
        Assert.Equal(2, snapshot.Rows.Count);

        var first = snapshot.Rows[0];
        Assert.Equal("santiye-f0", first.Id);
        Assert.Equal("F0 hazırlık", first.Piece);
        Assert.Equal("claude / opus", first.Lane);
        Assert.Equal("draft / mid", first.EnvRisk);
        Assert.Equal("bitti", first.Status);
        Assert.Equal("", first.Reviewer);
        Assert.Equal(0, first.FixRounds);
        Assert.Equal("", first.Notes);

        var second = snapshot.Rows[1];
        Assert.Equal("santiye-f1", second.Id);
        Assert.Equal("F1 çekirdek", second.Piece);
        Assert.Equal("onay bekliyor", second.Status);
        Assert.Equal("ONAY", second.Reviewer);
        Assert.Equal(1, second.FixRounds);
        Assert.Equal("20 test", second.Notes);
    }

    [Fact]
    public void Parse_DoesNotCareAboutColumnOrder()
    {
        var snapshot = StatusFile.Parse("""
            | status | notes | fix rounds | id | reviewer | env / risk | lane / model | piece |
            |---|---|---|---|---|---|---|---|
            | bitti | ilk | 2 | p-1 | ONAY | draft / mid | claude / opus | Bir |
            """);

        var row = Assert.Single(snapshot.Rows);
        Assert.Equal("p-1", row.Id);
        Assert.Equal("Bir", row.Piece);
        Assert.Equal("claude / opus", row.Lane);
        Assert.Equal("draft / mid", row.EnvRisk);
        Assert.Equal("bitti", row.Status);
        Assert.Equal("ONAY", row.Reviewer);
        Assert.Equal(2, row.FixRounds);
        Assert.Equal("ilk", row.Notes);
    }

    [Fact]
    public void Parse_LeavesMissingColumnsEmpty()
    {
        var snapshot = StatusFile.Parse("""
            | id | status |
            |---|---|
            | p-1 | bitti |
            """);

        var row = Assert.Single(snapshot.Rows);
        Assert.Equal("p-1", row.Id);
        Assert.Equal("bitti", row.Status);
        Assert.Equal("", row.Piece);
        Assert.Equal("", row.Lane);
        Assert.Equal("", row.EnvRisk);
        Assert.Equal("", row.Reviewer);
        Assert.Equal("", row.Notes);
        Assert.Equal(0, row.FixRounds);
    }

    [Fact]
    public void Parse_SkipsTablesWithoutAnIdColumn()
    {
        var snapshot = StatusFile.Parse("""
            | piece | status |
            |---|---|
            | Bir | bitti |

            | id | status |
            |---|---|
            | p-9 | bitti |
            """);

        var row = Assert.Single(snapshot.Rows);
        Assert.Equal("p-9", row.Id);
    }

    [Theory]
    [InlineData("Aşama: F1", "F1")]
    [InlineData("Asama: F2", "F2")]
    [InlineData("Stage: F3", "F3")]
    [InlineData("stage:   F4   ", "F4")]
    public void Parse_ReadsEveryStageKey(string line, string expected)
    {
        Assert.Equal(expected, StatusFile.Parse(line + "\n").Stage);
    }

    [Fact]
    public void Parse_WithoutAStageLineGivesNull()
    {
        var snapshot = StatusFile.Parse("""
            | id | status |
            |---|---|
            | p-1 | bitti |
            """);

        Assert.Null(snapshot.Stage);
    }

    [Fact]
    public void Parse_GivesZeroForFixRoundsThatIsNotAWholeNumber()
    {
        var snapshot = StatusFile.Parse("""
            | id | status | fix rounds |
            |---|---|---|
            | p-1 | bitti | 2.5 |
            | p-2 | bitti | cok |
            | p-3 | bitti | |
            | p-4 | bitti | 3 |
            """);

        Assert.Equal([0, 0, 0, 3], snapshot.Rows.Select(r => r.FixRounds).ToArray());
    }

    [Fact]
    public void Parse_LastDuplicateIdWinsAndKeepsTheFirstPosition()
    {
        var snapshot = StatusFile.Parse("""
            | id | piece | status |
            |---|---|---|
            | p-1 | birinci | bitti |
            | p-2 | ikinci | bitti |
            | p-1 | ucuncu | sahada |
            """);

        Assert.Equal(2, snapshot.Rows.Count);
        Assert.Equal("p-1", snapshot.Rows[0].Id);
        Assert.Equal("ucuncu", snapshot.Rows[0].Piece);
        Assert.Equal("p-2", snapshot.Rows[1].Id);
    }

    [Fact]
    public void Parse_GivesAnEmptySnapshotForGarbage()
    {
        var snapshot = StatusFile.Parse("""
            Bu bir durum dosyasi degil.
            | yok |
            |---|
            ## baslik
            """);

        Assert.Null(snapshot.Stage);
        Assert.Empty(snapshot.Rows);
        Assert.Empty(StatusFile.Parse("").Rows);
        Assert.Null(StatusFile.Parse("").Stage);
    }

    [Fact]
    public void Diff_WithoutABeforeEmitsTheWholeFile()
    {
        var events = StatusFile.Diff(null, StatusFile.Parse(Example), "ancosSantiye", At);

        Assert.Equal(
            [
                EventKinds.StageChanged,
                EventKinds.PieceStatus,
                EventKinds.PieceStatus,
                EventKinds.ReviewVerdict,
                EventKinds.GatePending,
            ],
            events.Select(e => e.Kind).ToArray());

        var stage = events[0];
        Assert.Equal("F1", stage.Data!["stage"]);
        Assert.Equal("ancosSantiye", stage.Project);
        Assert.Equal(Sources.Folder, stage.Source);
        Assert.Equal("status-file", stage.Actor.Id);
        Assert.Equal(Roles.Chief, stage.Actor.Role);
        Assert.Equal("Status file", stage.Actor.Name);
        Assert.Equal(At, stage.Ts);

        Assert.Equal("santiye-f0", events[1].Piece);
        Assert.Equal("bitti", events[1].Data!["status"]);
        Assert.Equal("santiye-f1", events[2].Piece);
        Assert.Equal("ONAY", events[3].Data!["verdict"]);
        Assert.Equal("santiye-f1", events[3].Piece);
        Assert.Equal("santiye-f1", events[4].Data!["gate"]);
        Assert.Equal("F1 çekirdek", events[4].Data!["label"]);
    }

    [Fact]
    public void Diff_ChangingTheStageEmitsStageChanged()
    {
        var before = StatusFile.Parse("Aşama: F1\n");
        var after = StatusFile.Parse("Aşama: F2\n");

        var events = StatusFile.Diff(before, after, "demo", At);

        var only = Assert.Single(events);
        Assert.Equal(EventKinds.StageChanged, only.Kind);
        Assert.Equal("F2", only.Data!["stage"]);
    }

    [Fact]
    public void Diff_MovingToPendingEmitsStatusThenGate()
    {
        var before = StatusFile.Parse("""
            | id | piece | status |
            |---|---|---|
            | p-1 | Bir | sahada |
            """);
        var after = StatusFile.Parse("""
            | id | piece | status |
            |---|---|---|
            | p-1 | Bir | onay bekliyor |
            """);

        var events = StatusFile.Diff(before, after, "demo", At);

        Assert.Equal([EventKinds.PieceStatus, EventKinds.GatePending], events.Select(e => e.Kind).ToArray());
        Assert.Equal("onay bekliyor", events[0].Data!["status"]);
        Assert.Equal("p-1", events[1].Data!["gate"]);
        Assert.Equal("Bir", events[1].Data!["label"]);
    }

    [Fact]
    public void Diff_LeavingPendingClearsTheGate()
    {
        var before = StatusFile.Parse("""
            | id | piece | status |
            |---|---|---|
            | p-1 | Bir | onay bekliyor |
            """);
        var after = StatusFile.Parse("""
            | id | piece | status |
            |---|---|---|
            | p-1 | Bir | bitti |
            """);

        var events = StatusFile.Diff(before, after, "demo", At);

        Assert.Equal([EventKinds.PieceStatus, EventKinds.GateCleared], events.Select(e => e.Kind).ToArray());
        Assert.Equal("p-1", events[1].Data!["gate"]);
        Assert.Equal("p-1", events[1].Piece);
    }

    [Fact]
    public void Diff_RemovedRowClearsTheGateOnlyWhenItWasPending()
    {
        var before = StatusFile.Parse("""
            | id | piece | status |
            |---|---|---|
            | p-1 | Bir | onay bekliyor |
            | p-2 | Iki | bitti |
            """);
        var after = StatusFile.Parse("""
            | id | piece | status |
            |---|---|---|
            | p-2 | Iki | bitti |
            """);

        var events = StatusFile.Diff(before, after, "demo", At);

        var only = Assert.Single(events);
        Assert.Equal(EventKinds.GateCleared, only.Kind);
        Assert.Equal("p-1", only.Data!["gate"]);
    }

    [Fact]
    public void Diff_ChangingTheReviewerEmitsTheVerdict()
    {
        var before = StatusFile.Parse("""
            | id | status | reviewer |
            |---|---|---|
            | p-1 | bitti | |
            """);
        var after = StatusFile.Parse("""
            | id | status | reviewer |
            |---|---|---|
            | p-1 | bitti | ONAY |
            """);

        var events = StatusFile.Diff(before, after, "demo", At);

        var only = Assert.Single(events);
        Assert.Equal(EventKinds.ReviewVerdict, only.Kind);
        Assert.Equal("p-1", only.Piece);
        Assert.Equal("ONAY", only.Data!["verdict"]);
    }

    [Fact]
    public void Diff_EmptyingTheReviewerEmitsNothing()
    {
        var before = StatusFile.Parse("""
            | id | status | reviewer |
            |---|---|---|
            | p-1 | bitti | ONAY |
            """);
        var after = StatusFile.Parse("""
            | id | status | reviewer |
            |---|---|---|
            | p-1 | bitti | |
            """);

        Assert.Empty(StatusFile.Diff(before, after, "demo", At));
    }

    [Fact]
    public void Diff_SameSnapshotTwiceEmitsNothing()
    {
        var snapshot = StatusFile.Parse(Example);

        Assert.Empty(StatusFile.Diff(snapshot, snapshot, "demo", At));
        Assert.Empty(StatusFile.Diff(snapshot, StatusFile.Parse(Example), "demo", At));
    }
}
