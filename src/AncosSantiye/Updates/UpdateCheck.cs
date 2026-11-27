namespace AncosSantiye.Updates;

/// <summary>
/// Asks GitHub for a newer release at start and every 12 hours, unless the profile turns it off
/// (<c>"updateCheck": false</c>). One GET to api.github.com, nothing about this computer is sent.
/// It never installs anything by itself: the page and <c>ancos-santiye update</c> do that when asked.
/// </summary>
public sealed class UpdateCheck(HttpClient http, ILogger<UpdateCheck> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(12);

    public Release? Latest { get; private set; }

    public DateTimeOffset? CheckedAt { get; private set; }

    /// <summary>A newer release than this one, or null.</summary>
    public Release? Newer => Latest is { } r && r.Version > Releases.Current ? r : null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the server come up first; an update check is never urgent.
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        using var timer = new PeriodicTimer(Every);
        do
        {
            Latest = await Releases.Latest(http, stoppingToken) ?? Latest;
            CheckedAt = DateTimeOffset.UtcNow;
            if (Newer is { } newer)
            {
                logger.LogInformation("ancosSantiye {Version} is available. Update from the page or run: ancos-santiye update", Releases.Display(newer.Version));
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
