using System.Text.Json;
using AncosSantiye.Events;

namespace AncosSantiye.Store;

/// <summary>
/// Append-only NDJSON, one file per UTC day. Replaying a file rebuilds the scene exactly.
/// A line that does not parse is skipped and counted, never fatal.
/// </summary>
public sealed class EventLog(string directory)
{
    private readonly Lock _gate = new();

    public string Directory { get; } = directory;

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ancos-santiye", "events");

    public string PathFor(DateOnly day) => Path.Combine(Directory, $"{day:yyyy-MM-dd}.ndjson");

    public void Append(SiteEvent e)
    {
        var line = JsonSerializer.Serialize(e, EventJson.Options) + "\n";
        lock (_gate)
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.AppendAllText(PathFor(DateOnly.FromDateTime(e.Ts.UtcDateTime)), line);
        }
    }

    public ReadResult Read(DateOnly day)
    {
        var path = PathFor(day);
        if (!File.Exists(path))
        {
            return new ReadResult([], 0);
        }

        var events = new List<SiteEvent>();
        var skipped = 0;
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var e = JsonSerializer.Deserialize<SiteEvent>(line, EventJson.Options);
                if (e is null || e.Actor is null || string.IsNullOrEmpty(e.Kind))
                {
                    skipped++;
                    continue;
                }

                events.Add(e);
            }
            catch (JsonException)
            {
                skipped++;
            }
        }

        return new ReadResult(events, skipped);
    }

    public sealed record ReadResult(IReadOnlyList<SiteEvent> Events, int Skipped);
}
