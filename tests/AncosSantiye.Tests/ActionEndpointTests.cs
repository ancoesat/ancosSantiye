using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AncosSantiye.Actions;
using AncosSantiye.Profiles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace AncosSantiye.Tests;

public sealed class ActionEndpointTests : IAsyncLifetime
{
    private readonly TempRepo _repo = new();
    private readonly string _data = Path.Combine(Path.GetTempPath(), "santiye-act-" + Guid.NewGuid());
    private WebApplication _app = null!;
    private HttpClient _http = null!;
    private string _origin = "";

    private async Task Start(bool enabled)
    {
        var profile = new Profile(Actions: new ActionsProfile(enabled, enabled ? [_repo.Dir] : null));
        _app = SiteApp.Build([], _data, "http://127.0.0.1:0", profile);
        await _app.StartAsync();
        _origin = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(_origin) };
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _http?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        _repo.Dispose();
        if (Directory.Exists(_data))
        {
            Directory.Delete(_data, recursive: true);
        }
    }

    private async Task<string?> Token()
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync("/config"));
        return doc.RootElement.TryGetProperty("token", out var token) ? token.GetString() : null;
    }

    private HttpRequestMessage Post(string path, object body, string? origin, string? token, string mediaType = "application/json")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, mediaType),
        };
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        if (token is not null)
        {
            request.Headers.Add(ActionGuard.Header, token);
        }

        return request;
    }

    [Fact]
    public async Task Off_HasNoTokenAndNoEndpoints()
    {
        await Start(enabled: false);

        Assert.Null(await Token());
        foreach (var path in new[] { "/actions/status", "/actions/commit", "/actions/push" })
        {
            using var response = await _http.SendAsync(Post(path, new { repo = 0 }, _origin, "x"));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task On_ConfigNamesTheRepoButNeverItsPath()
    {
        await Start(enabled: true);

        var config = await _http.GetStringAsync("/config");

        Assert.Contains("\"repos\":[\"repo\"]", config);
        Assert.DoesNotContain(_repo.Root.Replace("\\", "\\\\"), config);
    }

    public static TheoryData<string?, bool, string, HttpStatusCode> Doors => new()
    {
        { null, true, "application/json", HttpStatusCode.Forbidden },
        { "http://evil.example", true, "application/json", HttpStatusCode.Forbidden },
        { "null", true, "application/json", HttpStatusCode.Forbidden },
        { "OWN", false, "application/json", HttpStatusCode.Unauthorized },
        { "OWN", true, "text/plain", HttpStatusCode.UnsupportedMediaType },
        { "OWN", true, "application/json", HttpStatusCode.OK },
    };

    [Theory]
    [MemberData(nameof(Doors))]
    public async Task Status_NeedsOwnOriginJsonAndToken(string? origin, bool withToken, string mediaType, HttpStatusCode expected)
    {
        await Start(enabled: true);
        var token = withToken ? await Token() : null;

        using var response = await _http.SendAsync(Post("/actions/status", new { repo = 0 }, origin == "OWN" ? _origin : origin, token, mediaType));

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("/config")]
    [InlineData("/actions/status")]
    public async Task AForeignHost_IsRefusedEvenWithAMatchingOrigin(string path)
    {
        // DNS rebinding: evil.example resolves to 127.0.0.1, so the browser sends that Host and that Origin.
        await Start(enabled: true);
        var token = await Token();
        var port = new Uri(_origin).Port;
        using var request = Post(path, new { repo = 0 }, $"http://evil.example:{port}", token);
        if (path == "/config")
        {
            request.Method = HttpMethod.Get;
            request.Content = null;
        }

        request.Headers.Host = $"evil.example:{port}";
        using var response = await _http.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(token!, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("http://0.0.0.0:5170")]
    [InlineData("http://[::1]:5170")]
    [InlineData("http://192.168.1.5:5170")]
    [InlineData("http://127.0.0.1:0;http://*:5171")]
    public void NonLoopbackUrls_StopTheServer(string urls)
    {
        var ex = Assert.Throws<SiteApp.SetupException>(() => SiteApp.Build([], _data, urls, new Profile()));

        Assert.Contains("loopback", ex.Message);
    }

    [Fact]
    public void ABusyPort_StopsTheServerWithOneLine()
    {
        var holder = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        holder.Start();
        try
        {
            var port = ((System.Net.IPEndPoint)holder.LocalEndpoint).Port;

            var ex = Assert.Throws<SiteApp.SetupException>(() => SiteApp.Build([], _data, $"http://127.0.0.1:{port}", new Profile()));

            Assert.Contains($"Port {port} is already in use", ex.Message);
        }
        finally
        {
            holder.Stop();
        }
    }

    [Fact]
    public async Task WrongToken_IsRefused()
    {
        await Start(enabled: true);
        _repo.Write("a.txt", "a");

        using var response = await _http.SendAsync(Post("/actions/commit", new { repo = 0, paths = new[] { "a.txt" }, message = "m" }, _origin, "0000"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, _repo.CommitCount());
    }

    [Fact]
    public async Task TokenChangesOnEveryStart()
    {
        await Start(enabled: true);
        var first = await Token();
        await DisposeApp();
        await Start(enabled: true);

        Assert.NotEqual(first, await Token());
    }

    private async Task DisposeApp()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Commit_ThroughTheDoor_CommitsAndLogsTheEvent()
    {
        await Start(enabled: true);
        _repo.Write("a.txt", "a");

        using var response = await _http.SendAsync(Post("/actions/commit", new { repo = 0, paths = new[] { "a.txt" }, message = "add a" }, _origin, await Token()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, _repo.CommitCount());
        var log = string.Concat(Directory.EnumerateFiles(Path.Combine(_data), "*.ndjson").Select(File.ReadAllText));
        Assert.Contains("action.commit", log);
    }

    [Fact]
    public async Task Commit_Refused_IsA400WithAMessage()
    {
        await Start(enabled: true);

        using var response = await _http.SendAsync(Post("/actions/commit", new { repo = 0, paths = new[] { "-A" }, message = "m" }, _origin, await Token()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Not in git status", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void AnEnabledProfile_WithANonRepoFolder_StopsTheServer()
    {
        var plain = Path.Combine(_repo.Root, "plain");
        Directory.CreateDirectory(plain);

        var ex = Assert.Throws<SiteApp.SetupException>(() =>
            SiteApp.Build([], _data, "http://127.0.0.1:0", new Profile(Actions: new ActionsProfile(true, [plain]))));

        Assert.Contains("not a git repo", ex.Message);
    }
}
