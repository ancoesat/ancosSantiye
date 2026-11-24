using AncosSantiye.Events;
using AncosSantiye.Store;

namespace AncosSantiye.Tests;

public sealed class EventLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "santiye-tests-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static readonly DateOnly Day = new(2026, 11, 24);

    [Fact]
    public void AppendThenRead_GivesTheSameEvents()
    {
        var log = new EventLog(_dir);
        var events = new[] { Ev.At(0, EventKinds.SessionStart), Ev.At(1, EventKinds.TaskCreated) };
        foreach (var e in events)
        {
            log.Append(e);
        }

        var read = log.Read(Day);

        Assert.Equal(0, read.Skipped);
        Assert.Equal(events.Select(e => e.Id), read.Events.Select(e => e.Id));
    }

    [Fact]
    public void Read_SkipsBrokenLinesAndKeepsTheRest()
    {
        var log = new EventLog(_dir);
        log.Append(Ev.At(0, EventKinds.SessionStart));
        File.AppendAllText(log.PathFor(Day), "{not json\n{\"kind\":\"tool.start\"}\n\n");
        log.Append(Ev.At(1, EventKinds.TaskCreated));

        var read = log.Read(Day);

        Assert.Equal(2, read.Skipped);
        Assert.Equal(2, read.Events.Count);
    }

    [Fact]
    public void Read_MissingDayIsEmpty()
    {
        var read = new EventLog(_dir).Read(Day);

        Assert.Empty(read.Events);
        Assert.Equal(0, read.Skipped);
    }
}
