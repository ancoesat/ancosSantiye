using System.Text.Json;
using AncosSantiye.Events;

namespace AncosSantiye.Tests;

public class SiteEventTests
{
    [Fact]
    public void Create_CutsLongDataValues()
    {
        var e = Ev.At(0, EventKinds.ToolStart, data: ("tool", new string('x', 5000)));

        Assert.Equal(SiteEvent.MaxDataValueLength, e.Data!["tool"].Length);
    }

    [Fact]
    public void Create_CutsEveryLongString()
    {
        var big = new string('y', 5000);
        var e = SiteEvent.Create(big, big, new Actor(big, big, big), big, session: big, piece: big);

        Assert.All(
            new[] { e.Source, e.Project, e.Session!, e.Actor.Id, e.Actor.Role, e.Actor.Name!, e.Kind, e.Piece! },
            s => Assert.Equal(SiteEvent.MaxDataValueLength, s.Length));
    }

    [Fact]
    public void Create_IdsSortByTime()
    {
        var first = Ev.At(0, EventKinds.SessionStart);
        var second = Ev.At(1, EventKinds.SessionStart);

        Assert.True(string.CompareOrdinal(first.Id.ToString(), second.Id.ToString()) < 0);
        Assert.Equal(7, first.Id.Version);
    }

    [Fact]
    public void Json_UsesTheSchemaFieldNames()
    {
        var json = JsonSerializer.Serialize(Ev.At(0, EventKinds.ToolStart, Ev.Worker(), "demo-001", ("tool", "Grep")), EventJson.Options);
        using var doc = JsonDocument.Parse(json);
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(["id", "ts", "source", "project", "session", "actor", "kind", "piece", "data"], names);
        Assert.Equal("worker", doc.RootElement.GetProperty("actor").GetProperty("role").GetString());
    }
}
