using System.Reflection;
using System.Text.Json;

namespace AncosSantiye.Updates;

/// <summary>A release on GitHub with its tool package, as far as the updater trusts it.</summary>
public sealed record Release(Version Version, string Tag, string PageUrl, string PackageUrl, string PackageName, string Sha256);

/// <summary>
/// Releases of this app on GitHub. The only source the updater installs from: the repo is fixed in code,
/// a package must come from that repo's own release downloads, and GitHub's SHA-256 of it must be there.
/// </summary>
public static class Releases
{
    public const string Repo = "ancoesat/ancosSantiye";
    public const string LatestUrl = $"https://api.github.com/repos/{Repo}/releases/latest";
    public const string DownloadPrefix = $"https://github.com/{Repo}/releases/download/";

    /// <summary>The running version, without the build's commit suffix ("0.4.0+abc123" is 0.4.0).</summary>
    public static Version Current { get; } = Parse(
        typeof(Releases).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion) ?? new Version(0, 0, 0);

    public static string Display(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

    /// <summary>"v0.4.1", "0.4.1" or "0.4.1+abc" to a version; null for anything else (prereleases included).</summary>
    public static Version? Parse(string? text)
    {
        var value = (text ?? "").Trim().TrimStart('v', 'V').Split('+')[0];
        return value.Count(c => c == '.') == 2 && Version.TryParse(value, out var v) && v.Build >= 0 ? v : null;
    }

    /// <summary>
    /// The latest release from GitHub's JSON, or null when it is not one the updater may use: a draft, a
    /// prerelease, a tag that is not a plain version, or no package from this repo with GitHub's SHA-256.
    /// </summary>
    public static Release? FromJson(JsonElement release)
    {
        if (release.ValueKind != JsonValueKind.Object
            || Bool(release, "draft") || Bool(release, "prerelease")
            || Text(release, "tag_name") is not { } tag || Parse(tag) is not { } version)
        {
            return null;
        }

        var name = $"AncosSantiye.{Display(version)}.nupkg";
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            if (Text(asset, "name") == name
                && Text(asset, "browser_download_url") is { } url
                && url.StartsWith(DownloadPrefix, StringComparison.Ordinal)
                && Text(asset, "digest") is { } digest
                && digest.StartsWith("sha256:", StringComparison.Ordinal)
                && digest.Length == 7 + 64
                && digest[7..].All(Uri.IsHexDigit))
            {
                var page = Text(release, "html_url") is { } html && html.StartsWith($"https://github.com/{Repo}/", StringComparison.Ordinal)
                    ? html
                    : $"https://github.com/{Repo}/releases";
                return new Release(version, tag, page, url, name, digest[7..].ToLowerInvariant());
            }
        }

        return null;
    }

    /// <summary>Asks GitHub for the latest release. Null when there is none we can use or GitHub cannot be reached.</summary>
    public static async Task<Release?> Latest(HttpClient http, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
            request.Headers.UserAgent.ParseAdd($"ancosSantiye/{Display(Current)}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
            return FromJson(doc.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
