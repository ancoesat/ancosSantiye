using System.Text.Json;
using AncosSantiye.Events;

namespace AncosSantiye.Profiles;

/// <summary>
/// Watches a folder for status files and delivery files (see <c>Adapters/FolderLane</c>).
/// One file and its project (<c>"rovans-durum.md"</c>, <c>"rovans"</c>), or every file that matches one
/// <c>*</c> (<c>"*-durum.md"</c>), where the part under the <c>*</c> names the project: <c>kbyte-durum.md</c>
/// is the project <c>kbyte</c>, the same name a session gets from its folder.
/// </summary>
public sealed record FolderLaneProfile(string Directory, string StatusFile, string? Project = null, double IntervalSeconds = 1)
{
    public bool IsPattern => StatusFile.Contains('*');

    /// <summary>The project a status file belongs to, or null when the file is not one this lane reads.</summary>
    public string? ProjectOf(string fileName)
    {
        if (!IsPattern)
        {
            return string.Equals(fileName, StatusFile, StringComparison.OrdinalIgnoreCase) ? Project : null;
        }

        var star = StatusFile.IndexOf('*');
        var (prefix, suffix) = (StatusFile[..star], StatusFile[(star + 1)..]);
        var project = fileName.Length > prefix.Length + suffix.Length
                      && fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                      && fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[prefix.Length..^suffix.Length]
            : null;
        return string.IsNullOrWhiteSpace(project) ? null : project;
    }

    /// <summary>Whether this lane reads a status file for <paramref name="project"/> right now.</summary>
    public bool Watches(string project) =>
        IsPattern
            ? File.Exists(Path.Combine(Directory, StatusFile.Replace("*", project, StringComparison.Ordinal)))
            : string.Equals(project, Project, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Commit and push from the screen. Off unless enabled, and only for the repos listed here.</summary>
public sealed record ActionsProfile(bool Enabled = false, string[]? Repos = null);

/// <summary>
/// Everything personal lives here, never in the core: which skin, which folder to watch, whether
/// actions are allowed. No profile means: built-in skin, Claude Code only, read only.
/// </summary>
public sealed record Profile(
    string Skin = "grafxkid",
    string? SkinPath = null,
    FolderLaneProfile? FolderLane = null,
    ActionsProfile? Actions = null,
    bool UpdateCheck = true)
{
    public static Profile Default { get; } = new();

    public bool ActionsEnabled => Actions?.Enabled == true;

    /// <summary>Reads a profile file. A missing or broken file is an error with a clear message, never a silent default.</summary>
    public static (Profile? Profile, string? Error) Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return (Default, null);
        }

        if (!File.Exists(path))
        {
            return (null, $"Profile not found: {Path.GetFullPath(path)}");
        }

        try
        {
            var profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), Options);
            return profile is null ? (null, $"Profile is empty: {path}") : Validate(Anchor(profile, Path.GetDirectoryName(Path.GetFullPath(path))!), path);
        }
        catch (JsonException ex)
        {
            return (null, $"Profile is not valid JSON ({path}): {ex.Message}");
        }
    }

    /// <summary>Relative folders in a profile mean "next to the profile file", wherever the server starts from.</summary>
    private static Profile Anchor(Profile p, string dir)
    {
        string? At(string? folder) => string.IsNullOrWhiteSpace(folder) ? folder : Path.GetFullPath(folder, dir);

        return p with
        {
            SkinPath = At(p.SkinPath),
            FolderLane = p.FolderLane is { } lane ? lane with { Directory = At(lane.Directory)! } : null,
            Actions = p.Actions is { } a ? a with { Repos = a.Repos?.Select(r => At(r)!).ToArray() } : null,
        };
    }

    private static (Profile?, string?) Validate(Profile p, string path)
    {
        if (p.FolderLane is { } lane
            && (string.IsNullOrWhiteSpace(lane.Directory) || string.IsNullOrWhiteSpace(lane.StatusFile)
                || (!lane.IsPattern && string.IsNullOrWhiteSpace(lane.Project))))
        {
            return (null, $"Profile {path}: folderLane needs directory, statusFile and project (or a statusFile with one * such as \"*-durum.md\").");
        }

        if (p.FolderLane is { } pattern
            && (pattern.StatusFile.Count(c => c == '*') > 1 || pattern.StatusFile.IndexOfAny(['/', '\\', '?']) >= 0))
        {
            return (null, $"Profile {path}: folderLane.statusFile is a file name in the folder, with at most one *.");
        }

        if (p.FolderLane is { IntervalSeconds: <= 0 or > 60 })
        {
            return (null, $"Profile {path}: folderLane.intervalSeconds must be between 0 and 60.");
        }

        if (p.ActionsEnabled && (p.Actions!.Repos is not { Length: > 0 } repos || repos.Any(string.IsNullOrWhiteSpace)))
        {
            return (null, $"Profile {path}: actions.enabled needs actions.repos, the repo folders the screen may commit in.");
        }

        return (p, null);
    }

    private static readonly JsonSerializerOptions Options = new(EventJson.Options)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
