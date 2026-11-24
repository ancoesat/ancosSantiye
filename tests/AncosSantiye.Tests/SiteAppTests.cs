using System.Net;
using System.Text.Json;
using AncosSantiye.Events;
using AncosSantiye.Store;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace AncosSantiye.Tests;

/// <summary>Starts the real server on a free loopback port. No test host package needed.</summary>
public sealed class SiteAppTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "santiye-app-" + Guid.NewGuid());
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        _app = SiteApp.Build([], _dir, "http://127.0.0.1:0");
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("/", "text/html", "<canvas id=\"site\"")]
    [InlineData("/js/main.js", "text/javascript", "loadSkin")]
    [InlineData("/skin/skin.json", "application/json", "\"characters\"")]
    public async Task ServesThePageAndTheSkin(string path, string mediaType, string contains)
    {
        using var response = await _http.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(contains, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Server_ListensOnLoopbackOnly()
    {
        var address = _http.BaseAddress!;

        Assert.True(IPAddress.Parse(address.Host).Equals(IPAddress.Loopback));
    }

    [Fact]
    public async Task State_ReflectsPublishedEvents()
    {
        _app.Services.GetRequiredService<SiteHub>().Publish(Ev.At(0, EventKinds.StageChanged, data: ("stage", "F1")));

        using var doc = JsonDocument.Parse(await _http.GetStringAsync("/state"));

        Assert.Equal("F1", doc.RootElement.GetProperty("stage").GetString());
    }

    [Fact]
    public async Task Events_SendsASnapshotThenLiveEvents()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await _http.GetAsync("/events", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));
        var (firstType, _) = await NextSseItem(reader, cts.Token);
        Assert.Equal("snapshot", firstType);

        _app.Services.GetRequiredService<SiteHub>().Publish(Ev.At(1, EventKinds.TaskCreated));
        var (secondType, data) = await NextSseItem(reader, cts.Token);

        Assert.Equal("event", secondType);
        Assert.Equal("task.created", JsonDocument.Parse(data).RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Restart_ReplaysTodaysLog()
    {
        var hub = _app.Services.GetRequiredService<SiteHub>();
        hub.Publish(SiteEvent.Create(Sources.ClaudeCode, "demo", Ev.Chief, EventKinds.StageChanged, data: new Dictionary<string, string> { ["stage"] = "F2" }));

        var fresh = new SiteHub(new EventLog(_dir));
        fresh.Load(DateOnly.FromDateTime(DateTime.UtcNow));

        Assert.Equal("F2", fresh.State.Stage);
        await Task.CompletedTask;
    }

    private const string Hook = """{"hook_event_name":"SubagentStart","cwd":"/p/demo","agent_id":"agent_1","agent_type":"Explore"}""";

    private static StringContent Json(string body) => new(body, System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task ForeignHostHeader_IsRejected()
    {
        // DNS rebinding: a page on evil.example resolves to 127.0.0.1 and reads /state same-origin.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/state");
        request.Headers.Host = "evil.example";

        using var response = await _http.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void HostFilter_IsSetInCodeNotOnlyInAConfigFile()
    {
        var options = _app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.HostFiltering.HostFilteringOptions>>().Value;

        Assert.Equal(["127.0.0.1", "localhost"], options.AllowedHosts);
    }

    [Fact]
    public async Task HookPost_PublishesAndAnswersNoContent()
    {
        using var response = await _http.PostAsync("/hooks/claude", Json(Hook));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(_app.Services.GetRequiredService<SiteHub>().State.Actors.ContainsKey("agent_1"));
    }

    [Fact]
    public async Task HookPost_FromABrowserPage_IsRejected()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/hooks/claude") { Content = Json(Hook) };
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await _http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, _app.Services.GetRequiredService<SiteHub>().State.Version);
    }

    [Fact]
    public async Task HookPost_NotJson_IsRejected()
    {
        using var response = await _http.PostAsync("/hooks/claude", new StringContent(Hook, System.Text.Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(0, _app.Services.GetRequiredService<SiteHub>().State.Version);
    }

    [Fact]
    public async Task HookPost_BrokenJson_IsABadRequest()
    {
        using var response = await _http.PostAsync("/hooks/claude", Json("{nope"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StatusLinePost_UpdatesUsageOnceForSameValues()
    {
        const string status = """{"session_id":"s","context_window":{"used_percentage":12}}""";
        var hub = _app.Services.GetRequiredService<SiteHub>();

        (await _http.PostAsync("/feeds/statusline", Json(status))).Dispose();
        (await _http.PostAsync("/feeds/statusline", Json(status))).Dispose();

        Assert.Equal("12", hub.State.Usage["contextUsedPercentage"]);
        Assert.Equal(1, hub.State.Version);
    }

    private static async Task<(string? Type, string Data)> NextSseItem(StreamReader reader, CancellationToken ct)
    {
        string? type = null;
        var data = "";
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0 && (type is not null || data.Length > 0))
            {
                return (type, data);
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                type = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data += line["data: ".Length..];
            }
        }

        throw new EndOfStreamException();
    }
}
