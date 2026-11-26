using System.Collections.Immutable;
using System.Threading.Channels;
using AncosSantiye.Events;
using AncosSantiye.Scene;

namespace AncosSantiye.Store;

/// <summary>One line in the project picker, with the gates that wait in it (so alerts work for every project).</summary>
public sealed record ProjectInfo(string Name, DateTimeOffset LastSeen, int Workers, IReadOnlyList<GateState> Gates);

/// <summary>
/// Owns one <see cref="SiteState"/> per project. Every event goes to the log first, then through the reducer
/// of its own project, then out to live subscribers. A slow subscriber drops its oldest events instead of
/// slowing everyone down. Project names match without regard to case ("kbyte" and "KBYTE" are one project).
/// </summary>
public sealed class SiteHub(EventLog log, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private const int SubscriberBuffer = 256;

    /// <summary>How many recent events per project the page gets when it opens.</summary>
    public const int RecentCount = 20;

    private readonly Lock _gate = new();
    private readonly List<Channel<SiteEvent>> _subscribers = [];
    private ImmutableDictionary<string, (SiteState State, DateTimeOffset LastSeen)> _projects =
        ImmutableDictionary.Create<string, (SiteState, DateTimeOffset)>(StringComparer.OrdinalIgnoreCase);

    private ImmutableDictionary<string, ImmutableQueue<SiteEvent>> _recent =
        ImmutableDictionary.Create<string, ImmutableQueue<SiteEvent>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The last events of one project (null: the latest project), oldest first.</summary>
    public IReadOnlyList<SiteEvent> RecentOf(string? project)
    {
        lock (_gate)
        {
            var name = project ?? LatestLocked();
            return name is not null && _recent.TryGetValue(name, out var queue) ? [.. queue] : [];
        }
    }

    /// <summary>The most recently active project's state. Empty when nothing has happened yet.</summary>
    public SiteState State => StateOf(null);

    /// <summary>One project's state; null means the most recently active project.</summary>
    public SiteState StateOf(string? project)
    {
        lock (_gate)
        {
            var name = project ?? LatestLocked();
            return name is not null && _projects.TryGetValue(name, out var entry) ? Served(entry.State) : SiteState.Empty;
        }
    }

    private SiteState Served(SiteState state) => Silence.Drop(state, _clock.GetUtcNow());

    /// <summary>Most recently active first.</summary>
    public IReadOnlyList<ProjectInfo> Projects
    {
        get
        {
            lock (_gate)
            {
                return _projects
                    .OrderByDescending(p => p.Value.LastSeen)
                    .ThenBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => new ProjectInfo(p.Key, p.Value.LastSeen, Served(p.Value.State).Actors.Count, [.. p.Value.State.Gates.Values]))
                    .ToArray();
            }
        }
    }

    /// <summary>
    /// Rebuilds every project from one day's log. <paramref name="keep"/> can leave events out of the
    /// replay (they stay in the log). Returns how many lines were skipped as unreadable.
    /// </summary>
    public int Load(DateOnly day, Func<SiteEvent, bool>? keep = null)
    {
        var result = log.Read(day);
        lock (_gate)
        {
            _projects = _projects.Clear();
            _recent = _recent.Clear();
            foreach (var e in result.Events)
            {
                if (keep is null || keep(e))
                {
                    ApplyLocked(e);
                }
            }
        }

        return result.Skipped;
    }

    public void Publish(SiteEvent e)
    {
        Channel<SiteEvent>[] subscribers;
        lock (_gate)
        {
            log.Append(e);
            ApplyLocked(e);
            subscribers = [.. _subscribers];
        }

        foreach (var channel in subscribers)
        {
            channel.Writer.TryWrite(e);
        }
    }

    /// <summary>
    /// The snapshot (of one project, or the latest) and the live feed are taken under one lock, so no event
    /// falls between them. The feed carries every project: the page needs them to keep its picker current.
    /// </summary>
    public Subscription Subscribe(string? project = null)
    {
        var channel = Channel.CreateBounded<SiteEvent>(new BoundedChannelOptions(SubscriberBuffer)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        SiteState snapshot;
        lock (_gate)
        {
            _subscribers.Add(channel);
            var name = project ?? LatestLocked();
            snapshot = name is not null && _projects.TryGetValue(name, out var entry) ? Served(entry.State) : SiteState.Empty;
        }

        return new Subscription(snapshot, channel.Reader, () =>
        {
            lock (_gate)
            {
                _subscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        });
    }

    private void ApplyLocked(SiteEvent e)
    {
        (SiteState State, DateTimeOffset LastSeen) old = _projects.TryGetValue(e.Project, out var entry) ? entry : (SiteState.Empty, DateTimeOffset.MinValue);
        var next = SiteReducer.Reduce(old.State, e);
        _projects = _projects.SetItem(e.Project, (next, e.Ts > old.LastSeen ? e.Ts : old.LastSeen));

        var queue = (_recent.TryGetValue(e.Project, out var q) ? q : ImmutableQueue<SiteEvent>.Empty).Enqueue(e);
        if (queue.Count() > RecentCount)
        {
            queue = queue.Dequeue();
        }

        _recent = _recent.SetItem(e.Project, queue);
    }

    private string? LatestLocked() =>
        _projects.IsEmpty
            ? null
            : _projects.OrderByDescending(p => p.Value.LastSeen).ThenBy(p => p.Key, StringComparer.Ordinal).First().Key;

    public sealed class Subscription(SiteState snapshot, ChannelReader<SiteEvent> reader, Action onDispose) : IDisposable
    {
        public SiteState Snapshot { get; } = snapshot;

        public ChannelReader<SiteEvent> Reader { get; } = reader;

        public void Dispose() => onDispose();
    }
}
