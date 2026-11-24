namespace AncosSantiye.Events;

/// <summary>Who did something on the site.</summary>
/// <param name="Id">Stable id: "chief" for the main session, the agent id for a subagent.</param>
/// <param name="Role">One of <see cref="Roles"/>.</param>
/// <param name="Name">Display name, for example the subagent type.</param>
public sealed record Actor(string Id, string Role, string? Name = null);

/// <summary>
/// The one event shape every adapter emits. It never carries code, diffs, file contents or secrets:
/// only names, ids, counts and statuses, in <see cref="Data"/> as short strings.
/// </summary>
public sealed record SiteEvent(
    Guid Id,
    DateTimeOffset Ts,
    string Source,
    string Project,
    string? Session,
    Actor Actor,
    string Kind,
    string? Piece,
    IReadOnlyDictionary<string, string>? Data)
{
    /// <summary>Longest value kept in <see cref="Data"/>. Longer values are cut, so nothing big slips in.</summary>
    public const int MaxDataValueLength = 200;

    public static SiteEvent Create(
        string source,
        string project,
        Actor actor,
        string kind,
        string? session = null,
        string? piece = null,
        IReadOnlyDictionary<string, string>? data = null,
        DateTimeOffset? ts = null)
    {
        var at = ts ?? DateTimeOffset.UtcNow;
        return new SiteEvent(Guid.CreateVersion7(at), at, source, project, session, actor, kind, piece, Trim(data));
    }

    private static Dictionary<string, string>? Trim(IReadOnlyDictionary<string, string>? data) =>
        data?.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Length <= MaxDataValueLength ? kv.Value : kv.Value[..MaxDataValueLength]);
}
