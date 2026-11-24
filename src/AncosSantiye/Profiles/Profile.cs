using System.Text.Json;
using AncosSantiye.Events;

namespace AncosSantiye.Profiles;

/// <summary>Watches a folder for a status file and delivery files (see <c>Adapters/FolderLane</c>).</summary>
public sealed record FolderLaneProfile(string Directory, string StatusFile, string Project, double IntervalSeconds = 1);

public sealed record ActionsProfile(bool Enabled = false);

/// <summary>
/// Everything personal lives here, never in the core: which skin, which folder to watch, whether
/// actions are allowed. No profile means: built-in skin, Claude Code only, read only.
/// </summary>
public sealed record Profile(
    string Skin = "grafxkid",
    string? SkinPath = null,
    FolderLaneProfile? FolderLane = null,
    ActionsProfile? Actions = null)
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
            return profile is null ? (null, $"Profile is empty: {path}") : Validate(profile, path);
        }
        catch (JsonException ex)
        {
            return (null, $"Profile is not valid JSON ({path}): {ex.Message}");
        }
    }

    private static (Profile?, string?) Validate(Profile p, string path)
    {
        if (p.FolderLane is { } lane
            && (string.IsNullOrWhiteSpace(lane.Directory) || string.IsNullOrWhiteSpace(lane.StatusFile) || string.IsNullOrWhiteSpace(lane.Project)))
        {
            return (null, $"Profile {path}: folderLane needs directory, statusFile and project.");
        }

        if (p.FolderLane is { IntervalSeconds: <= 0 or > 60 })
        {
            return (null, $"Profile {path}: folderLane.intervalSeconds must be between 0 and 60.");
        }

        return (p, null);
    }

    private static readonly JsonSerializerOptions Options = new(EventJson.Options)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
