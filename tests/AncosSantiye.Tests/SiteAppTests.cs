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
