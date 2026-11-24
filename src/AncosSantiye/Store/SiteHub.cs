using System.Threading.Channels;
using AncosSantiye.Events;
using AncosSantiye.Scene;

namespace AncosSantiye.Store;

/// <summary>
/// Owns the current <see cref="SiteState"/>. Every event goes to the log first, then through the reducer,
/// then out to live subscribers. A slow subscriber drops its oldest events instead of slowing everyone down.
/// </summary>
public sealed class SiteHub(EventLog log)
{
    private const int SubscriberBuffer = 256;

    private readonly Lock _gate = new();
    private readonly List<Channel<SiteEvent>> _subscribers = [];
    private SiteState _state = SiteState.Empty;

    public SiteState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>Rebuilds the state from one day's log. Returns how many lines were skipped as unreadable.</summary>
    public int Load(DateOnly day)
    {
        var result = log.Read(day);
        lock (_gate)
        {
            _state = SiteReducer.Replay(result.Events);
        }

        return result.Skipped;
    }

    public void Publish(SiteEvent e)
    {
        Channel<SiteEvent>[] subscribers;
        lock (_gate)
        {
            log.Append(e);
            _state = SiteReducer.Reduce(_state, e);
            subscribers = [.. _subscribers];
        }

        foreach (var channel in subscribers)
        {
            channel.Writer.TryWrite(e);
        }
    }

    /// <summary>The snapshot and the live feed are taken under one lock, so no event falls between them.</summary>
    public Subscription Subscribe()
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
            snapshot = _state;
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

    public sealed class Subscription(SiteState snapshot, ChannelReader<SiteEvent> reader, Action onDispose) : IDisposable
    {
        public SiteState Snapshot { get; } = snapshot;

        public ChannelReader<SiteEvent> Reader { get; } = reader;

        public void Dispose() => onDispose();
    }
}
