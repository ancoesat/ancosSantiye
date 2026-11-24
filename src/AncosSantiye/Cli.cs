using System.Text;
using System.Text.Json;
using AncosSantiye.Adapters;

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
        "Usage: ancos-santiye [--urls <url>] [--profile <file>] | statusline | hooks print | hooks install [--settings <path>] | hooks uninstall [--settings <path>]";

    /// <summary>No arguments, or only server options like --urls. Help flags are not a server start.</summary>
    public static bool IsServerStart(string[] args) =>
        args.Length == 0 || (args[0].StartsWith('-') && args[0] is not ("--help" or "-h"));

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


    private static int Report(HookInstaller.Result result, string verb, TextWriter stdout, TextWriter stderr)
    {
        if (result.Error is not null)
        {
            stderr.WriteLine(result.Error);
            return 1;
        }

        stdout.WriteLine(result.Changed ? $"Hooks {verb}." : "Nothing to change.");
        if (result.BackupPath is not null)
        {
            stdout.WriteLine($"Backup: {result.BackupPath}");
        }

        if (result.StatusLineKept)
        {
            stdout.WriteLine($"You already have a status line, so it was left alone. To show the site link, call `{HookInstaller.StatusLineCommand}` from it.");
        }

        return 0;
    }
}
