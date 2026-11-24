using System.Text.RegularExpressions;
using AncosSantiye.Events;

namespace AncosSantiye.Adapters;

/// <summary>A delivery file name taken apart: which project, which piece, which supplier lane.</summary>
public sealed record DeliveryInfo(string Project, string PieceId, string Lane);

/// <summary>
/// Pure helpers for delivery file names. The name is all they need: the caller does the folder work.
/// A delivery file is named project-NNN-lane.md, for example kasa-api-009-gemini.md.
/// </summary>
public static class DeliveryFile
{
    private const string Gemini = "gemini";

    private static readonly Regex Name = new(
        @"\A(?<project>.+)-(?<number>\d{3,})-(?<lane>gemini|studio)\.md\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Reads a bare file name. False for anything that is not a delivery file name.</summary>
    public static bool TryParse(string fileName, out DeliveryInfo? info)
    {
        info = null;
        if (string.IsNullOrEmpty(fileName))
            return false;

        if (fileName.Contains('/') || fileName.Contains('\\'))
            return false;

        var match = Name.Match(fileName);
        if (!match.Success)
            return false;

        var project = match.Groups["project"].Value;
        var number = match.Groups["number"].Value;
        var lane = match.Groups["lane"].Value.ToLowerInvariant();
        info = new DeliveryInfo(project, project + "-" + number, lane);
        return true;
    }

    /// <summary>The event a delivery file stands for. Pure: the time comes from the caller.</summary>
    public static SiteEvent ToEvent(DeliveryInfo info, DateTimeOffset at) =>
        SiteEvent.Create(
            Sources.Folder,
            info.Project,
            new Actor(
                "supplier-" + info.Lane,
                Roles.Supplier,
                string.Equals(info.Lane, Gemini, StringComparison.OrdinalIgnoreCase) ? "Gemini" : "AI Studio"),
            EventKinds.DeliveryArrived,
            piece: info.PieceId,
            ts: at);
}
