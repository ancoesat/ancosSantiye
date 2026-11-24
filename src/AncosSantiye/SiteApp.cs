using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AncosSantiye.Adapters;
using Microsoft.Extensions.FileProviders;
using AncosSantiye.Events;
using AncosSantiye.Store;

namespace AncosSantiye;

/// <summary>Builds the web app. Tests call this with their own data folder and port 0.</summary>
public static class SiteApp
{
    public static WebApplication Build(string[] args, string? dataDirectory = null, string? urls = null)
    {
        // Content root is the app folder, not the current directory: an installed tool runs from anywhere.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Loopback only. `--urls` can still change the port, but F7 rejects any non-loopback host.
        var configuredUrls = urls ?? builder.Configuration["urls"];
        builder.WebHost.UseUrls(string.IsNullOrEmpty(configuredUrls) ? ServerOptions.DefaultUrl : configuredUrls);

        // DNS rebinding guard, set in code so it never depends on a config file being found.
        builder.Services.Configure<Microsoft.AspNetCore.HostFiltering.HostFilteringOptions>(o =>
            o.AllowedHosts = ["127.0.0.1", "localhost"]);

        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.DefaultIgnoreCondition = EventJson.Options.DefaultIgnoreCondition);

        var log = new EventLog(dataDirectory ?? builder.Configuration["Santiye:DataDir"] ?? EventLog.DefaultDirectory);
        var hub = new SiteHub(log);
        builder.Services.AddSingleton(log);
        builder.Services.AddSingleton(hub);
        builder.Services.AddSingleton<StatusLineFeed>();

        var app = builder.Build();

        var skipped = hub.Load(DateOnly.FromDateTime(DateTime.UtcNow));
        if (skipped > 0)
        {
            app.Logger.LogWarning("Skipped {Count} unreadable lines in today's event log", skipped);
        }

        // The page and the skins. Both live next to the app, so an installed tool finds them from any folder.
        app.UseDefaultFiles();
        app.UseStaticFiles();
        var skins = Path.Combine(app.Environment.ContentRootPath, "skins");
        if (Directory.Exists(skins))
        {
            app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(skins), RequestPath = "/skins" });
        }

        app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" }));
        app.MapGet("/state", (SiteHub h) => TypedResults.Ok(h.State));
        app.MapGet("/events", (SiteHub h, CancellationToken ct) => TypedResults.ServerSentEvents(Stream(h, ct)));

        // Claude Code hooks. Answer at once: an HTTP hook blocks Claude Code until it returns.
        app.MapPost("/hooks/claude", async (HttpRequest request, SiteHub h, CancellationToken ct) =>
        {
            if (LocalOnly.Reject(request) is { } rejected)
            {
                return rejected;
            }

            using var body = await ReadJson(request, ct);
            if (body is null)
            {
                return TypedResults.BadRequest();
            }

            if (ClaudeHookMapper.Map(body.RootElement, DateTimeOffset.UtcNow) is { } e)
            {
                h.Publish(e);
            }

            return TypedResults.NoContent();
        });

        app.MapPost("/feeds/statusline", async (HttpRequest request, SiteHub h, StatusLineFeed feed, CancellationToken ct) =>
        {
            if (LocalOnly.Reject(request) is { } rejected)
            {
                return rejected;
            }

            using var body = await ReadJson(request, ct);
            if (body is null)
            {
                return TypedResults.BadRequest();
            }

            if (feed.Map(body.RootElement, DateTimeOffset.UtcNow) is { } e)
            {
                h.Publish(e);
            }

            return TypedResults.NoContent();
        });

        return app;
    }

    private static async Task<JsonDocument?> ReadJson(HttpRequest request, CancellationToken ct)
    {
        try
        {
            return await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
        }
        catch (JsonException)
        {
            return null;
        }
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
