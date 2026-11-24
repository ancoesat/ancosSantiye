using AncosSantiye.Events;
using AncosSantiye.Profiles;
using AncosSantiye.Store;

namespace AncosSantiye.Adapters;

/// <summary>
/// Polls one folder: the status file (changes become events) and delivery files (new ones become events).
/// Polling, not a file watcher: watchers miss events on network drives and WSL mounts, and one stat a second is free.
/// </summary>
public sealed class FolderLane(FolderLaneProfile profile, SiteHub hub, ILogger<FolderLane> logger) : BackgroundService
{
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private StatusSnapshot? _last;
    private DateTime _lastWrite;
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
        ReadStatus(now);
        ReadDeliveries(now);
        _first = false;
    }

    private void ReadStatus(DateTimeOffset now)
    {
        var path = Path.Combine(profile.Directory, profile.StatusFile);
        if (!File.Exists(path))
        {
            return;
        }

        var write = File.GetLastWriteTimeUtc(path);
        if (write == _lastWrite)
        {
            return;
        }

        var snapshot = StatusFile.Parse(File.ReadAllText(path));

        // A half-written file reads as an empty table, which would clear every gate. Keep the old
        // view and leave the time alone, so the next tick reads the finished file.
        if (snapshot.Rows.Count == 0 && _last is { Rows.Count: > 0 })
        {
            return;
        }

        foreach (var e in StatusFile.Diff(_last, snapshot, profile.Project, now))
        {
            hub.Publish(e);
        }

        _last = snapshot;
        _lastWrite = write;
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
                || !string.Equals(info!.Project, profile.Project, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Files already there at start are old news; only new arrivals walk onto the site.
            if (!_first)
            {
                hub.Publish(DeliveryFile.ToEvent(info with { Project = profile.Project }, now));
            }
        }
    }
}
