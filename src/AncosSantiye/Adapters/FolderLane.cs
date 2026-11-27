using AncosSantiye.Events;
using AncosSantiye.Profiles;
using AncosSantiye.Store;

namespace AncosSantiye.Adapters;

/// <summary>
/// Polls one folder: the status file, or every status file that matches the profile's <c>*</c> (changes become
/// events, each for its own project), and delivery files of those projects (new ones become events).
/// Polling, not a file watcher: watchers miss events on network drives and WSL mounts, and one stat a second is free.
/// </summary>
public sealed class FolderLane(FolderLaneProfile profile, SiteHub hub, ILogger<FolderLane> logger) : BackgroundService
{
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per status file: what it said last time and when it was written.</summary>
    private readonly Dictionary<string, (StatusSnapshot Snapshot, DateTime Write)> _status = new(StringComparer.OrdinalIgnoreCase);
    private bool _first = true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(profile.IntervalSeconds));
        do
        {
            try
            {
                Tick(DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Someone is writing the file right now. The next tick reads it again.
                logger.LogDebug(ex, "Folder lane read failed, retrying next tick");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One poll. Public so tests can drive it without a clock.</summary>
    public void Tick(DateTimeOffset now)
    {
        foreach (var (path, project) in StatusFiles())
        {
            ReadStatus(path, project, now);
        }

        ReadDeliveries(now);
        _first = false;
    }

    private IEnumerable<(string Path, string Project)> StatusFiles()
    {
        if (!profile.IsPattern)
        {
            var path = Path.Combine(profile.Directory, profile.StatusFile);
            return File.Exists(path) ? [(path, profile.Project!)] : [];
        }

        if (!Directory.Exists(profile.Directory))
        {
            return [];
        }

        // Matched again by ProjectOf: Windows' own matching is loose (8.3 names, longer extensions).
        return Directory.EnumerateFiles(profile.Directory, profile.StatusFile)
            .Select(path => (path, project: profile.ProjectOf(Path.GetFileName(path))))
            .Where(f => f.project is not null)
            .Select(f => (f.path, f.project!))
            .ToArray();
    }

    private void ReadStatus(string path, string project, DateTimeOffset now)
    {
        var write = File.GetLastWriteTimeUtc(path);
        _status.TryGetValue(path, out var last);
        if (write == last.Write)
        {
            return;
        }

        var snapshot = StatusFile.Parse(File.ReadAllText(path));

        // A half-written file reads as an empty table, which would clear every gate. Keep the old
        // view and leave the time alone, so the next tick reads the finished file.
        if (snapshot.Rows.Count == 0 && last.Snapshot is { Rows.Count: > 0 })
        {
            return;
        }

        foreach (var e in StatusFile.Diff(last.Snapshot, snapshot, project, now))
        {
            hub.Publish(e);
        }

        _status[path] = (snapshot, write);
    }

    private void ReadDeliveries(DateTimeOffset now)
    {
        if (!Directory.Exists(profile.Directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(profile.Directory, "*.md"))
        {
            var name = Path.GetFileName(path);
            if (!_seen.Add(name)
                || !DeliveryFile.TryParse(name, out var info)
                || !profile.Watches(info!.Project))
            {
                continue;
            }

            // Files already there at start are old news; only new arrivals walk onto the site.
            if (!_first)
            {
                var project = profile.IsPattern ? info.Project : profile.Project!;
                hub.Publish(DeliveryFile.ToEvent(info with { Project = project }, now));
            }
        }
    }
}
