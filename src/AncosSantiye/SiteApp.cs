using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using AncosSantiye.Events;
using AncosSantiye.Store;

namespace AncosSantiye;

/// <summary>Builds the web app. Tests call this with their own data folder and port 0.</summary>
public static class SiteApp
{
    public static WebApplication Build(string[] args, string? dataDirectory = null, string? urls = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Loopback only. `--urls` can still change the port, but F7 rejects any non-loopback host.
        var configuredUrls = urls ?? builder.Configuration["urls"];
        builder.WebHost.UseUrls(string.IsNullOrEmpty(configuredUrls) ? ServerOptions.DefaultUrl : configuredUrls);

        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.DefaultIgnoreCondition = EventJson.Options.DefaultIgnoreCondition);

        var log = new EventLog(dataDirectory ?? builder.Configuration["Santiye:DataDir"] ?? EventLog.DefaultDirectory);
        var hub = new SiteHub(log);
        builder.Services.AddSingleton(log);
        builder.Services.AddSingleton(hub);

        var app = builder.Build();

        var skipped = hub.Load(DateOnly.FromDateTime(DateTime.UtcNow));
        if (skipped > 0)
        {
            app.Logger.LogWarning("Skipped {Count} unreadable lines in today's event log", skipped);
        }

        app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" }));
        app.MapGet("/state", (SiteHub h) => TypedResults.Ok(h.State));
        app.MapGet("/events", (SiteHub h, CancellationToken ct) => TypedResults.ServerSentEvents(Stream(h, ct)));

        return app;
    }

    /// <summary>First a "snapshot" of the whole site, then every new "event" as it happens.</summary>
    private static async IAsyncEnumerable<SseItem<object>> Stream(
        SiteHub hub,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var subscription = hub.Subscribe();
        yield return new SseItem<object>(subscription.Snapshot, "snapshot");

        await foreach (var e in subscription.Reader.ReadAllAsync(ct))
        {
            yield return new SseItem<object>(e, "event");
        }
    }
}
