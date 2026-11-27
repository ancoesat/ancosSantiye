using System.Net;
using System.Text;
using System.Text.Json;
using AncosSantiye.Actions;
using AncosSantiye.Setup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace AncosSantiye.Tests;

/// <summary>Connect buttons and the agy and OpenCode hook endpoints, on a real server with a temp home folder.</summary>
public sealed class SetupEndpointTests : IAsyncLifetime
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "santiye-sethome-" + Guid.NewGuid());
    private readonly string _data = Path.Combine(Path.GetTempPath(), "santiye-setdata-" + Guid.NewGuid());
    private WebApplication _app = null!;
    private HttpClient _http = null!;
    private string _origin = "";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_home);
        _app = SiteApp.Build(["--Santiye:Home", _home], _data, "http://127.0.0.1:0");
        await _app.StartAsync();
        _origin = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        _http = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(_origin) };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        foreach (var dir in new[] { _home, _data })
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private async Task<string> Token()
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync("/config"));
        return doc.RootElement.GetProperty("setupToken").GetString()!;
    }

    private async Task<HttpResponseMessage> Post(string path, string json, string? origin, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        if (token is not null)
        {
            request.Headers.Add(ActionGuard.Header, token);
        }

        return await _http.SendAsync(request);
    }

    [Fact]
    public async Task Connect_WritesIntoTheConfiguredHome()
    {
        using var response = await Post("/setup/connect", """{"tool":"opencode"}""", _origin, await Token());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(File.Exists(OpenCodePlugin.PathIn(_home)));
        Assert.Contains($"{ServerOptions.DefaultUrl}/hooks/opencode", await File.ReadAllTextAsync(OpenCodePlugin.PathIn(_home)));

        using var off = await Post("/setup/disconnect", """{"tool":"opencode"}""", _origin, await Token());
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.False(File.Exists(OpenCodePlugin.PathIn(_home)));
    }

    [Fact]
    public async Task Connect_BackupPathStartsAtHome()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AgyHooks.PathIn(_home))!);
        await File.WriteAllTextAsync(AgyHooks.PathIn(_home), "{}");

        using var response = await Post("/setup/connect", """{"tool":"agy"}""", _origin, await Token());
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.StartsWith("~", doc.RootElement.GetProperty("backup").GetString());
    }

    [Theory]
    [InlineData(false, true, HttpStatusCode.Forbidden)]
    [InlineData(true, false, HttpStatusCode.Unauthorized)]
    public async Task Connect_NeedsOurOriginAndToken(bool withOrigin, bool withToken, HttpStatusCode expected)
    {
        using var response = await Post("/setup/connect", """{"tool":"agy"}""", withOrigin ? _origin : null, withToken ? await Token() : null);

        Assert.Equal(expected, response.StatusCode);
        Assert.False(File.Exists(AgyHooks.PathIn(_home)));
    }

    [Fact]
    public async Task Connect_WritesTheFixedAddress_NotTheRequestsHost()
    {
        // A local caller with the token picks its own Host and a matching Origin (the host filter ignores the port).
        var port = new Uri(_origin).Port + 1;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/setup/connect")
        {
            Content = new StringContent("""{"tool":"claude-code"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Host = $"127.0.0.1:{port}";
        request.Headers.Add("Origin", $"http://127.0.0.1:{port}");
        request.Headers.Add(ActionGuard.Header, await Token());

        using var response = await _http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await File.ReadAllTextAsync(Path.Combine(_home, ".claude", "settings.json"));
        Assert.Contains($"{ServerOptions.DefaultUrl}/hooks/claude", settings);
        Assert.DoesNotContain($":{port}", settings);
    }

    [Fact]
    public async Task Connect_NotJson_IsRefused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/setup/connect")
        {
            Content = new StringContent("""{"tool":"agy"}""", Encoding.UTF8, "text/plain"),
        };
        request.Headers.Add("Origin", _origin);
        request.Headers.Add(ActionGuard.Header, await Token());

        using var response = await _http.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Connect_AFileThatCannotBeWritten_IsAnError_NotACrash()
    {
        // A folder where the plugin file should be: reading it fails like a locked or unreadable file.
        Directory.CreateDirectory(OpenCodePlugin.PathIn(_home));

        using var response = await Post("/setup/connect", """{"tool":"opencode"}""", _origin, await Token());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Nothing was changed", await response.Content.ReadAsStringAsync());
        using var diagnose = await _http.GetAsync("/diagnose");
        Assert.Equal(HttpStatusCode.OK, diagnose.StatusCode);
    }

    [Fact]
    public async Task Version_IsShown_AndTestsNeverCheckGitHub()
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync("/version"));

        Assert.Equal(AncosSantiye.Updates.Releases.Display(AncosSantiye.Updates.Releases.Current), doc.RootElement.GetProperty("current").GetString());
        Assert.False(doc.RootElement.GetProperty("checks").GetBoolean());
        Assert.False(doc.RootElement.TryGetProperty("latest", out var latest) && latest.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task Update_NeedsTheSetupDoor_AndANewerVersion()
    {
        using var noToken = await Post("/setup/update", "{}", _origin, null);
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);

        using var foreign = await Post("/setup/update", "{}", "http://evil.example", await Token());
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        using var nothing = await Post("/setup/update", "{}", _origin, await Token());
        Assert.Equal(HttpStatusCode.BadRequest, nothing.StatusCode);
        Assert.Contains("no newer version", await nothing.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Connect_AForeignOrigin_IsRefused()
    {
        using var response = await Post("/setup/connect", """{"tool":"agy"}""", "http://evil.example", await Token());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"tool":"codex"}""")]
    [InlineData("""{}""")]
    [InlineData("not json")]
    public async Task Connect_UnknownTool_IsABadRequest(string json)
    {
        using var response = await Post("/setup/connect", json, _origin, await Token());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AgyHook_ReachesTheSite_AndDiagnose()
    {
        using var response = await Post("/hooks/agy?event=PreToolUse",
            """{"conversationId":"c1","workspacePaths":["/w/rovans"],"toolCall":{"name":"view_file","args":{"AbsolutePath":"/w/rovans/a.ts"}}}""", null, null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var state = await _http.GetStringAsync("/state?project=rovans");
        Assert.Contains("Reading a.ts", state);
        var diagnose = await _http.GetStringAsync("/diagnose");
        using var doc = JsonDocument.Parse(diagnose);
        var agy = doc.RootElement.EnumerateArray().Single(t => t.GetProperty("id").GetString() == "agy");
        Assert.True(agy.TryGetProperty("lastEvent", out _));
    }

    [Fact]
    public async Task OpenCodeHook_ReachesTheSite()
    {
        using var response = await Post("/hooks/opencode", """{"kind":"tool.start","session":"s1","project":"kbyte","tool":"read","file":"x/Api.cs"}""", null, null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Contains("Reading Api.cs", await _http.GetStringAsync("/state?project=kbyte"));
    }

    [Theory]
    [InlineData("/hooks/agy?event=Stop")]
    [InlineData("/hooks/opencode")]
    public async Task HookEndpoints_RefuseBrowserPages(string path)
    {
        using var response = await Post(path, """{"kind":"agent.start","session":"s1","conversationId":"c"}""", "http://evil.example", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
