using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AncosSantiye.Actions;
using AncosSantiye.Adapters;
using Microsoft.Extensions.FileProviders;
using AncosSantiye.Events;
using AncosSantiye.Profiles;
using AncosSantiye.Store;

namespace AncosSantiye;

/// <summary>Builds the web app. Tests call this with their own data folder and port 0.</summary>
public static class SiteApp
{
    /// <summary>Thrown when the profile or its skin cannot be used. Program prints the message and exits.</summary>
    public sealed class SetupException(string message) : Exception(message);

    public static WebApplication Build(string[] args, string? dataDirectory = null, string? urls = null, Profile? profile = null)
    {
        // Content root is the app folder, not the current directory: an installed tool runs from anywhere.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Loopback only. `--urls` may change the port, never the host: with actions on, a LAN address
        // would let any machine on the network read the token and commit.
        var configuredUrls = urls ?? builder.Configuration["urls"];
        var serverUrls = string.IsNullOrEmpty(configuredUrls) ? ServerOptions.DefaultUrl : configuredUrls;
        foreach (var url in serverUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host is not ("127.0.0.1" or "localhost"))
            {
                throw new SetupException($"Only loopback addresses are allowed (127.0.0.1, localhost): {url}");
            }

            if (uri.Port != 0 && PortInUse(uri.Port))
            {
                throw new SetupException(
                    $"Port {uri.Port} is already in use. If ancos-santiye is already running, just open http://127.0.0.1:{uri.Port}. " +
                    "Otherwise stop the program that holds the port.");
            }
        }

        builder.WebHost.UseUrls(serverUrls);

        // DNS rebinding guard, set in code so it never depends on a config file being found.
        builder.Services.Configure<Microsoft.AspNetCore.HostFiltering.HostFilteringOptions>(o =>
            o.AllowedHosts = ["127.0.0.1", "localhost"]);

        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.DefaultIgnoreCondition = EventJson.Options.DefaultIgnoreCondition);

        if (profile is null)
        {
            var (loaded, error) = Profile.Load(builder.Configuration["profile"]);
            profile = loaded ?? throw new SetupException(error!);
        }

        var skinRoot = profile.SkinPath ?? Path.Combine(builder.Environment.ContentRootPath, "skins", profile.Skin);
        if (!File.Exists(Path.Combine(skinRoot, "skin.json")))
        {
            throw new SetupException($"Skin not found: {skinRoot} has no skin.json");
        }

        var repos = new List<string>();
        foreach (var repo in profile.ActionsEnabled ? profile.Actions!.Repos! : [])
        {
            var full = Path.GetFullPath(repo);
            if (!Directory.Exists(Path.Combine(full, ".git")) && !File.Exists(Path.Combine(full, ".git")))
            {
                throw new SetupException($"actions.repos: not a git repo: {full}");
            }

            repos.Add(full);
        }

        var log = new EventLog(dataDirectory ?? builder.Configuration["Santiye:DataDir"] ?? EventLog.DefaultDirectory);
        var hub = new SiteHub(log);
        builder.Services.AddSingleton(log);
        builder.Services.AddSingleton(hub);
        builder.Services.AddSingleton<StatusLineFeed>();
        if (profile.FolderLane is { } lane)
        {
            builder.Services.AddSingleton(lane);
            builder.Services.AddHostedService<FolderLane>();
        }

        var app = builder.Build();

        var skipped = hub.Load(DateOnly.FromDateTime(DateTime.UtcNow));
        if (skipped > 0)
        {
            app.Logger.LogWarning("Skipped {Count} unreadable lines in today's event log", skipped);
        }

        // The page lives next to the app. The active skin (built in or the profile's own folder) is always at /skin.
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(Path.GetFullPath(skinRoot)), RequestPath = "/skin" });

        // What the page may show. The token is only here when actions are on, and only this page can read it:
        // another site gets no CORS header, and the host filter stops DNS rebinding.
        var guard = ActionGuard.Create();
        var actions = profile.ActionsEnabled ? new GitActions(repos) : null;
        app.MapGet("/config", () => TypedResults.Ok(actions is null
            ? new { actionsEnabled = false, repos = (IReadOnlyList<string>)[], token = (string?)null }
            : new { actionsEnabled = true, repos = actions.Names, token = (string?)guard.Token }));

        if (actions is not null)
        {
            MapActions(app, actions, guard);
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

    /// <summary>A one line answer instead of Kestrel's stack trace when a second copy starts.</summary>
    private static bool PortInUse(int port)
    {
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port) { ExclusiveAddressUse = OperatingSystem.IsWindows() };
        try
        {
            probe.Start();
            return false;
        }
        catch (System.Net.Sockets.SocketException ex) when (ex.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
        {
            return true;
        }
        finally
        {
            probe.Stop();
        }
    }

    private sealed record RepoRequest(int Repo);

    private sealed record CommitRequest(int Repo, string[]? Paths, string? Message);

    /// <summary>Commit and push. Only mapped when the profile turns actions on: otherwise these paths are 404.</summary>
    private static void MapActions(WebApplication app, GitActions actions, ActionGuard guard)
    {
        app.MapPost("/actions/status", async (HttpRequest request, CancellationToken ct) =>
        {
            if (guard.Reject(request) is { } rejected)
            {
                return rejected;
            }

            if (await Read<RepoRequest>(request, ct) is not { } body || actions.RepoAt(body.Repo) is not { } repo)
            {
                return TypedResults.BadRequest(new { ok = false, message = "Unknown repo." });
            }

            return await actions.Status(repo) is { } files
                ? TypedResults.Ok(new { ok = true, files })
                : TypedResults.BadRequest(new { ok = false, message = "git status failed in this folder." });
        });

        app.MapPost("/actions/commit", async (HttpRequest request, SiteHub hub, CancellationToken ct) =>
        {
            if (guard.Reject(request) is { } rejected)
            {
                return rejected;
            }

            if (await Read<CommitRequest>(request, ct) is not { } body)
            {
                return TypedResults.BadRequest(new { ok = false, message = "Bad request." });
            }

            return Answer(await actions.Commit(body.Repo, body.Paths, body.Message, DateTimeOffset.UtcNow), hub);
        });

        app.MapPost("/actions/push", async (HttpRequest request, SiteHub hub, CancellationToken ct) =>
        {
            if (guard.Reject(request) is { } rejected)
            {
                return rejected;
            }

            if (await Read<RepoRequest>(request, ct) is not { } body)
            {
                return TypedResults.BadRequest(new { ok = false, message = "Bad request." });
            }

            return Answer(await actions.Push(body.Repo, DateTimeOffset.UtcNow), hub);
        });
    }

    private static IResult Answer(ActionOutcome? outcome, SiteHub hub)
    {
        if (outcome is null)
        {
            return TypedResults.Conflict(new { ok = false, message = "Another git action is running. Try again in a moment." });
        }

        if (outcome.Event is { } e)
        {
            hub.Publish(e);
        }

        return outcome.Ok
            ? TypedResults.Ok(new { ok = true, message = outcome.Message })
            : TypedResults.BadRequest(new { ok = false, message = outcome.Message });
    }

    private static async Task<T?> Read<T>(HttpRequest request, CancellationToken ct)
        where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(request.Body, EventJson.Options, ct);
        }
        catch (JsonException)
        {
            return null;
        }
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
