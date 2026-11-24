using System.Globalization;
using AncosSantiye.Events;

namespace AncosSantiye.Adapters;

/// <summary>One row of the status table, exactly as the status file writes it.</summary>
public sealed record StatusRow(
    string Id,
    string Piece,
    string Lane,
    string EnvRisk,
    string Status,
    string Reviewer,
    int FixRounds,
    string Notes);

/// <summary>What one status file says: its stage line and its rows in file order.</summary>
public sealed record StatusSnapshot(string? Stage, IReadOnlyList<StatusRow> Rows);

/// <summary>
/// Pure parser and differ for the markdown status file a human and Claude keep up to date.
/// No clock, no I/O: the caller passes text in and turns differences into events.
/// </summary>
public static class StatusFile
{
    /// <summary>The actor every event from a status file is credited to.</summary>
    private static readonly Actor StatusActor = new("status-file", Roles.Chief, "Status file");

    private static readonly string[] StageKeys = ["Aşama:", "Asama:", "Stage:"];

    /// <summary>Column names the table is read by, matched case-insensitively after whitespace is collapsed.</summary>
    private static readonly string[] Columns =
    [
        "id", "piece", "lane / model", "env / risk", "status", "reviewer", "fix rounds", "notes",
    ];

    private const string PendingStatus = "onay bekliyor";

    /// <summary>Reads one status file. Never throws on bad input: parts it cannot read come back empty.</summary>
    public static StatusSnapshot Parse(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return new StatusSnapshot(null, Array.Empty<StatusRow>());

        var lines = markdown.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (i == 0 && line.StartsWith('\uFEFF'))
                line = line[1..];
            lines[i] = line;
        }

        return new StatusSnapshot(FindStage(lines), FindRows(lines));
    }

    /// <summary>
    /// Turns the difference between two snapshots into events, in a fixed order.
    /// A null <paramref name="before"/> means every row is new.
    /// </summary>
    public static IReadOnlyList<SiteEvent> Diff(
        StatusSnapshot? before,
        StatusSnapshot after,
        string project,
        DateTimeOffset at)
    {
        var events = new List<SiteEvent>();

        var oldRows = new Dictionary<string, StatusRow>(StringComparer.Ordinal);
        if (before is not null)
        {
            foreach (var row in before.Rows)
                oldRows[row.Id] = row;
        }

        if (after.Stage is not null && !string.Equals(after.Stage, before?.Stage, StringComparison.Ordinal))
            events.Add(Make(project, at, EventKinds.StageChanged, null, One("stage", after.Stage)));

        foreach (var row in after.Rows)
        {
            var isNew = !oldRows.TryGetValue(row.Id, out var old);
            var oldStatus = old?.Status ?? string.Empty;
            var oldReviewer = old?.Reviewer ?? string.Empty;

            if (isNew || !string.Equals(row.Status, oldStatus, StringComparison.Ordinal))
                events.Add(Make(project, at, EventKinds.PieceStatus, row.Id, One("status", row.Status)));

            if (row.Reviewer.Length > 0 && !string.Equals(row.Reviewer, oldReviewer, StringComparison.Ordinal))
                events.Add(Make(project, at, EventKinds.ReviewVerdict, row.Id, One("verdict", row.Reviewer)));

            var wasPending = IsPending(oldStatus);
            var isPending = IsPending(row.Status);
            if (isPending && !wasPending)
                events.Add(Make(project, at, EventKinds.GatePending, row.Id, Two("gate", row.Id, "label", row.Piece)));
            else if (wasPending && !isPending)
                events.Add(Make(project, at, EventKinds.GateCleared, row.Id, One("gate", row.Id)));
        }

        if (before is not null)
        {
            var present = after.Rows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var row in before.Rows)
            {
                if (!present.Contains(row.Id) && IsPending(row.Status))
                    events.Add(Make(project, at, EventKinds.GateCleared, row.Id, One("gate", row.Id)));
            }
        }

        return events;
    }

    private static SiteEvent Make(
        string project,
        DateTimeOffset at,
        string kind,
        string? piece,
        IReadOnlyDictionary<string, string> data) =>
        SiteEvent.Create(
            Sources.Folder,
            project,
            StatusActor,
            kind,
            piece: piece,
            data: data,
            ts: at);

    private static Dictionary<string, string> One(string key, string value) =>
        new() { [key] = value };

    private static Dictionary<string, string> Two(string key1, string value1, string key2, string value2) =>
        new() { [key1] = value1, [key2] = value2 };

    private static string? FindStage(string[] lines)
    {
        foreach (var line in lines)
        {
            var text = line.TrimStart();
            foreach (var key in StageKeys)
            {
                if (!text.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = text[key.Length..].Trim();

                // An empty value would only produce an event the scene cannot use, so it counts as no stage.
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }

    private static IReadOnlyList<StatusRow> FindRows(string[] lines)
    {
        // A markdown table is a header row, a separator row, then the body. That shape keeps prose out.
        for (var i = 0; i + 1 < lines.Length; i++)
        {
            if (!IsTableLine(lines[i]) || !IsSeparatorRow(lines[i + 1]))
                continue;

            var map = MapColumns(Cells(lines[i]));
            if (map is null)
                continue;

            var rows = new List<StatusRow>();
            var index = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var j = i + 2; j < lines.Length && IsTableLine(lines[j]); j++)
            {
                var cells = Cells(lines[j]);
                if (IsSeparator(cells))
                    continue;

                var row = BuildRow(map, cells);
                if (row is null)
                    continue;

                if (index.TryGetValue(row.Id, out var slot))
                    rows[slot] = row;
                else
                {
                    index[row.Id] = rows.Count;
                    rows.Add(row);
                }
            }

            return rows;
        }

        return Array.Empty<StatusRow>();
    }

    /// <summary>Maps column name to cell index. Null when the header lacks an id or a status column.</summary>
    private static Dictionary<string, int>? MapColumns(List<string> header)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < header.Count; i++)
        {
            var name = Collapse(header[i]);
            foreach (var column in Columns)
            {
                if (!map.ContainsKey(column) && string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                    map[column] = i;
            }
        }

        return map.ContainsKey("id") && map.ContainsKey("status") ? map : null;
    }

    private static StatusRow? BuildRow(Dictionary<string, int> map, List<string> cells)
    {
        var id = Cell(map, cells, "id");
        if (id.Length == 0)
            return null;

        return new StatusRow(
            id,
            Cell(map, cells, "piece"),
            Cell(map, cells, "lane / model"),
            Cell(map, cells, "env / risk"),
            Cell(map, cells, "status"),
            Cell(map, cells, "reviewer"),
            FixRounds(Cell(map, cells, "fix rounds")),
            Cell(map, cells, "notes"));
    }

    private static string Cell(Dictionary<string, int> map, List<string> cells, string column) =>
        map.TryGetValue(column, out var i) && i < cells.Count ? cells[i] : string.Empty;

    private static int FixRounds(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rounds) ? rounds : 0;

    private static bool IsPending(string status) =>
        string.Equals(status.Trim(), PendingStatus, StringComparison.OrdinalIgnoreCase);

    private static bool IsTableLine(string line) => line.TrimStart().StartsWith('|');

    private static bool IsSeparatorRow(string line) => IsTableLine(line) && IsSeparator(Cells(line));

    private static bool IsSeparator(List<string> cells) =>
        cells.Count > 0 && cells.All(c => c.Length > 0 && c.All(ch => ch == '-' || ch == ':'));

    /// <summary>Splits one table line into trimmed cells, without the outer pipes.</summary>
    private static List<string> Cells(string line)
    {
        var text = line.Trim();
        if (text.StartsWith('|'))
            text = text[1..];
        if (text.EndsWith('|'))
            text = text[..^1];

        return text.Split('|').Select(c => c.Trim()).ToList();
    }

    private static string Collapse(string cell) =>
        string.Join(' ', cell.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
