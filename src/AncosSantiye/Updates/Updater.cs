using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace AncosSantiye.Updates;

/// <summary>What the update script needs: where the package is, which version, who to wait for, how to restart.</summary>
public sealed record UpdatePlan(string PackageDir, Version Version, int WaitForPid, string WorkDir, IReadOnlyList<string>? Restart);

/// <summary>
/// Installs a release. A running tool cannot replace its own files (Windows locks them), so the work is
/// split: this process downloads and checks the package, writes a short script next to it, starts the
/// script and exits. The script waits for it, stops any other running ancosSantiye, runs
/// <c>dotnet tool update</c> from the downloaded package and, when asked, starts the server again.
/// </summary>
public static class Updater
{
    private const long MaxPackageBytes = 50 * 1024 * 1024;

    /// <summary>Downloads the release's package into its own temp folder and checks it. Returns the folder, or an error.</summary>
    public static async Task<(string? Dir, string? Error)> Download(HttpClient http, Release release, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ancos-santiye-update-{Releases.Display(release.Version)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, release.PackageName);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, release.PackageUrl);
            request.Headers.UserAgent.ParseAdd($"ancosSantiye/{Releases.Display(Releases.Current)}");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                return (null, $"GitHub answered {(int)response.StatusCode} for the package.");
            }

            if (response.Content.Headers.ContentLength > MaxPackageBytes)
            {
                return (null, "The package is larger than any ancosSantiye package should be.");
            }

            await using (var file = File.Create(path))
            await using (var body = await response.Content.ReadAsStreamAsync(ct))
            {
                await CopyCapped(body, file, ct);
            }

            return Check(path, release) is { } error ? (null, error) : (dir, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException)
        {
            return (null, $"Download failed: {ex.Message}");
        }
    }

    /// <summary>The package must match GitHub's SHA-256 and be AncosSantiye at exactly the release's version.</summary>
    public static string? Check(string path, Release release)
    {
        using (var stream = File.OpenRead(path))
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (hash != release.Sha256)
            {
                return "The downloaded package does not match GitHub's SHA-256. Nothing was installed.";
            }
        }

        using var zip = ZipFile.OpenRead(path);
        var nuspec = zip.Entries.FirstOrDefault(e => e.FullName.Equals("AncosSantiye.nuspec", StringComparison.OrdinalIgnoreCase));
        if (nuspec is null)
        {
            return "The download is not an ancosSantiye package. Nothing was installed.";
        }

        using var reader = nuspec.Open();
        var metadata = XDocument.Load(reader).Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");
        string? Field(string name) => metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
        return Field("id") == "AncosSantiye" && Releases.Parse(Field("version")) == release.Version
            ? null
            : "The package says it is something else than this release. Nothing was installed.";
    }

    /// <summary>The script for this computer. Pure, so the quoting is tested.</summary>
    public static string Script(UpdatePlan plan, bool windows) => windows ? PowerShell(plan) : Shell(plan);

    /// <summary>Writes the script next to the package and starts it on its own; the caller should exit soon after.</summary>
    public static string Launch(UpdatePlan plan)
    {
        var windows = OperatingSystem.IsWindows();
        var script = Path.Combine(plan.PackageDir, windows ? "update.ps1" : "update.sh");
        File.WriteAllText(script, Script(plan, windows));
        var start = windows
            ? new ProcessStartInfo("powershell.exe") { UseShellExecute = true }
            : new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        if (windows)
        {
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script })
            {
                start.ArgumentList.Add(arg);
            }
        }
        else
        {
            // Its own session, output to a log: the script must outlive this process and its terminal.
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add($"nohup /bin/sh {Sh(script)} > {Sh(Path.Combine(plan.PackageDir, "update.log"))} 2>&1 &");
        }

        Process.Start(start)?.Dispose();
        return script;
    }

    private static string PowerShell(UpdatePlan p)
    {
        var restart = p.Restart is null
            ? "Write-Host 'Start it again with: ancos-santiye'"
            : p.Restart.Count == 0
                ? "Start-Process ancos-santiye"
                : $"Start-Process ancos-santiye -ArgumentList @({string.Join(", ", p.Restart.Select(a => Ps(Quoted(a))))})";
        return $$"""
            # ancosSantiye updater, written by ancos-santiye. Safe to delete.
            $ErrorActionPreference = 'Stop'
            Start-Transcript -Path (Join-Path $PSScriptRoot 'update.log') -Append | Out-Null
            try {
                Write-Host 'Updating ancosSantiye to {{Releases.Display(p.Version)}}...'
                Wait-Process -Id {{p.WaitForPid}} -Timeout 30 -ErrorAction SilentlyContinue
                Get-Process ancos-santiye -ErrorAction SilentlyContinue | Stop-Process -Force
                Start-Sleep -Milliseconds 500
                & dotnet tool update -g AncosSantiye --add-source {{Ps(p.PackageDir)}} --version {{Releases.Display(p.Version)}} --ignore-failed-sources
                if ($LASTEXITCODE -ne 0) { throw "dotnet tool update failed with exit code $LASTEXITCODE" }
                Write-Host 'ancosSantiye {{Releases.Display(p.Version)}} is installed.'
                Set-Location -LiteralPath {{Ps(p.WorkDir)}}
                {{restart}}
                Start-Sleep -Seconds 3
            } catch {
                Write-Host $_
                Read-Host 'The update failed. Press Enter to close this window'
            } finally {
                Stop-Transcript | Out-Null
            }

            """;
    }

    private static string Shell(UpdatePlan p)
    {
        var restart = p.Restart is null
            ? "echo 'Start it again with: ancos-santiye'"
            : $"nohup ancos-santiye {string.Join(" ", p.Restart.Select(Sh))} > /dev/null 2>&1 &";
        return $$"""
            #!/bin/sh
            # ancosSantiye updater, written by ancos-santiye. Safe to delete.
            echo 'Updating ancosSantiye to {{Releases.Display(p.Version)}}...'
            while kill -0 {{p.WaitForPid}} 2>/dev/null; do sleep 0.2; done
            pkill -x ancos-santiye 2>/dev/null
            sleep 0.5
            dotnet tool update -g AncosSantiye --add-source {{Sh(p.PackageDir)}} --version {{Releases.Display(p.Version)}} --ignore-failed-sources || { echo 'The update failed.'; exit 1; }
            echo 'ancosSantiye {{Releases.Display(p.Version)}} is installed.'
            cd {{Sh(p.WorkDir)}} || exit 1
            {{restart}}

            """;
    }

    /// <summary>A PowerShell single-quoted string: only ' needs doubling.</summary>
    internal static string Ps(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>A POSIX shell single-quoted string: ' becomes '\''.</summary>
    internal static string Sh(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    /// <summary>Start-Process joins its argument list with spaces, so an argument with spaces needs its own quotes inside.</summary>
    private static string Quoted(string arg) => arg.Contains(' ') || arg.Contains('"') ? "\"" + arg.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : arg;

    private static async Task CopyCapped(Stream from, Stream to, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await from.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > MaxPackageBytes)
            {
                throw new InvalidDataException("The package is larger than any ancosSantiye package should be.");
            }

            await to.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
}
