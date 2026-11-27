using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using AncosSantiye.Adapters;
using AncosSantiye.Events;

namespace AncosSantiye;

/// <summary>Subcommands of the <c>ancos-santiye</c> tool. No arguments starts the server.</summary>
public static class Cli
{
    public static async Task<int> Run(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
    {
        var url = ServerOptions.DefaultUrl;
        switch (args)
        {
            case ["statusline", ..]:
                await StatusLine(stdin, stdout, url);
                return 0;

            case ["hook"]:
                await Forward(stdin, url + "/hooks/claude");
                return 0;

            // agy sends no event name in its payload, so its hooks.json passes one. It reads stdout as JSON.
            case ["hook", "--from", "agy", "--event", var name] when AgyHookMapper.Events.Contains(name):
                await Forward(stdin, url + "/hooks/agy?event=" + name);
                stdout.WriteLine("{}");
                return 0;

            case ["connect", var tool] when Setup.Connect.Tools.Contains(tool):
                return Report(Setup.Connect.Run(tool, true, Home, url, DateTimeOffset.Now)!, "installed", stdout, stderr, tool);

            case ["disconnect", var tool] when Setup.Connect.Tools.Contains(tool):
                return Report(Setup.Connect.Run(tool, false, Home, url, DateTimeOffset.Now)!, "removed", stdout, stderr, tool);

            case ["doctor"]:
                return await Doctor(stdout, url);

            case ["version"] or ["--version"]:
                stdout.WriteLine(Updates.Releases.Display(Updates.Releases.Current));
                return 0;

            case ["update"]:
                return await Update(stdout, stderr);

            case ["hooks", "print"]:
                stdout.WriteLine(HookInstaller.Print(HookInstaller.Snippet(url)));
                return 0;

            // Exact shapes only: a typo must never fall back to the real settings file.
            case ["hooks", "install"]:
                return Report(HookInstaller.Install(HookInstaller.DefaultSettingsPath, url, DateTimeOffset.Now), "installed", stdout, stderr);

            case ["hooks", "install", "--settings", var path]:
                return Report(HookInstaller.Install(path, url, DateTimeOffset.Now), "installed", stdout, stderr);

            case ["hooks", "uninstall"]:
                return Report(HookInstaller.Uninstall(HookInstaller.DefaultSettingsPath, url, DateTimeOffset.Now), "removed", stdout, stderr);

            case ["hooks", "uninstall", "--settings", var path]:
                return Report(HookInstaller.Uninstall(path, url, DateTimeOffset.Now), "removed", stdout, stderr);

            case ["--help"] or ["-h"] or ["help"]:
                stdout.WriteLine(Usage);
                return 0;

            default:
                stderr.WriteLine(Usage);
                return 2;
        }
    }

    public const string Usage =
        "Usage: ancos-santiye [--urls <url>] [--profile <file>] | doctor | update | version | connect <tool> | disconnect <tool> | statusline | hook | hooks print | hooks install [--settings <path>] | hooks uninstall [--settings <path>]\n" +
        "Tools: claude-code, agy, opencode";

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>No arguments, or only server options like --urls. Help flags are not a server start.</summary>
    public static bool IsServerStart(string[] args) =>
        args.Length == 0 || (args[0].StartsWith('-') && args[0] is not ("--help" or "-h" or "--version"));

    /// <summary>
    /// Checks GitHub, downloads and checks the newer package, then hands over to the update script and exits.
    /// The script stops a running server too; started from here it does not restart one (the page's Update does).
    /// </summary>
    private static async Task<int> Update(TextWriter stdout, TextWriter stderr)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var current = Updates.Releases.Current;
        stdout.WriteLine($"This is ancosSantiye {Updates.Releases.Display(current)}. Asking GitHub for the latest release...");
        if (await Updates.Releases.Latest(http, CancellationToken.None) is not { } latest)
        {
            stderr.WriteLine($"No release found (GitHub could not be reached, or the latest release has no package). See https://github.com/{Updates.Releases.Repo}/releases");
            return 1;
        }

        if (latest.Version <= current)
        {
            stdout.WriteLine("You have the latest version.");
            return 0;
        }

        stdout.WriteLine($"Downloading {latest.PackageName}...");
        var (dir, error) = await Updates.Updater.Download(http, latest, CancellationToken.None);
        if (dir is null)
        {
            stderr.WriteLine(error);
            return 1;
        }

        Updates.Updater.Launch(new Updates.UpdatePlan(dir, latest.Version, Environment.ProcessId, Environment.CurrentDirectory, Restart: null));
        stdout.WriteLine($"Checked against GitHub's SHA-256. The updater takes over now: it stops a running ancosSantiye and installs {Updates.Releases.Display(latest.Version)}.");
        stdout.WriteLine($"Release notes: {latest.PageUrl}");
        return 0;
    }

    /// <summary>
    /// Runs on every status line refresh, so it must be quick and must never fail loudly:
    /// forward the JSON with a short timeout, then always print a line.
    /// </summary>
    private static async Task StatusLine(TextReader stdin, TextWriter stdout, string url)
    {
        var input = await stdin.ReadToEndAsync();
        var up = false;
        try
        {
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromMilliseconds(500) };
            using var content = new StringContent(input, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(url + "/feeds/statusline", content);
            up = response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            up = false;
        }

        JsonElement status = default;
        try
        {
            status = JsonDocument.Parse(input).RootElement;
        }
        catch (JsonException)
        {
        }

        stdout.WriteLine(StatusLineFeed.Line(status, url, up));
    }


    /// <summary>
    /// For hook events that Claude Code only runs as a command (SessionStart): pass the payload on to the
    /// server's hook endpoint. Silent and always exit 0, so a server that is down never bothers Claude Code.
    /// </summary>
    private static async Task Forward(TextReader stdin, string endpoint)
    {
        var input = await stdin.ReadToEndAsync();
        try
        {
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromMilliseconds(1500) };
            using var content = new StringContent(input, Encoding.UTF8, "application/json");
            using var _ = await http.PostAsync(endpoint, content);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }
    }

    /// <summary>
    /// The same report as the setup page. Asks the running server first (it also knows the last events);
    /// without a server it looks on its own and says so.
    /// </summary>
    private static async Task<int> Doctor(TextWriter stdout, string url)
    {
        IReadOnlyList<Setup.ToolReport>? reports = null;
        try
        {
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
            reports = await http.GetFromJsonAsync<List<Setup.ToolReport>>(url + "/diagnose", EventJson.Options);
            stdout.WriteLine($"ancosSantiye server: running at {url}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            stdout.WriteLine($"ancosSantiye server: not running at {url} (start it with: ancos-santiye)");
        }

        reports ??= Setup.Diagnose.Detect(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable("PATH"), url);
        stdout.WriteLine();
        foreach (var r in reports.Where(r => r.Found))
        {
            var last = r.LastEvent is { } at ? $", last event {at.ToLocalTime():yyyy-MM-dd HH:mm}" : "";
            stdout.WriteLine($"{r.Name}: {r.Status}{last}");
            stdout.WriteLine($"  found by: {string.Join(", ", r.FoundBy)}");
            foreach (var note in r.Notes)
            {
                stdout.WriteLine($"  - {note}");
            }
        }

        var missing = reports.Where(r => !r.Found).Select(r => r.Name).ToList();
        if (missing.Count > 0)
        {
            stdout.WriteLine();
            stdout.WriteLine($"Not found: {string.Join(", ", missing)}");
        }

        return 0;
    }

    private static int Report(HookInstaller.Result result, string verb, TextWriter stdout, TextWriter stderr, string tool = "claude-code")
    {
        if (result.Error is not null)
        {
            stderr.WriteLine(result.Error);
            return 1;
        }

        stdout.WriteLine(result.Changed ? $"{tool}: {verb}." : $"{tool}: nothing to change.");
        if (result.Changed && tool == "opencode" && verb == "installed")
        {
            stdout.WriteLine("Restart OpenCode (terminal or desktop app) once so it loads the plugin.");
        }
        if (result.BackupPath is not null)
        {
            stdout.WriteLine($"Backup: {result.BackupPath}");
        }

        if (result.StatusLineKept)
        {
            stdout.WriteLine();
            stdout.WriteLine("NOTE: you already have a status line, so it was left alone.");
            stdout.WriteLine("The Budget panel stays empty until your status line also sends its data. Either:");
            stdout.WriteLine($"  replace it in settings.json:  \"statusLine\": {{ \"type\": \"command\", \"command\": \"{HookInstaller.StatusLineCommand}\" }}");
            stdout.WriteLine($"  or pass its input on from your own script:  echo \"$input\" | {HookInstaller.StatusLineCommand} > /dev/null");
        }

        return 0;
    }
}
