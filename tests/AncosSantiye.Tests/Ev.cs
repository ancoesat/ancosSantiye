using AncosSantiye.Events;

namespace AncosSantiye.Tests;

/// <summary>Short event builders so tests read like a story.</summary>
internal static class Ev
{
    private static readonly DateTimeOffset Start = new(2026, 11, 24, 20, 0, 0, TimeSpan.Zero);

    public static Actor Chief { get; } = new("chief", Roles.Chief, "Claude Code");

    public static Actor Worker(string id = "agent_1", string name = "Explore") => new(id, Roles.Worker, name);

    public static SiteEvent At(
        int second,
        string kind,
        Actor? actor = null,
        string? piece = null,
        params (string Key, string Value)[] data) =>
        SiteEvent.Create(
            Sources.ClaudeCode,
            "demo",
            actor ?? Chief,
            kind,
            session: "s1",
            piece: piece,
            data: data.Length == 0 ? null : data.ToDictionary(d => d.Key, d => d.Value),
            ts: Start.AddSeconds(second));
}
