using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Mongo.Profiler;

public sealed class MongoProfilerEventChannelBroadcaster : IMongoProfilerEventSink
{
    private readonly ConcurrentDictionary<Guid, Channel<MongoProfilerQueryEvent>> _subscribers = new();
    public int SubscriberCount => _subscribers.Count;

    public void Publish(MongoProfilerQueryEvent queryEvent)
    {
        foreach (var subscriber in _subscribers.Values)
        {
            subscriber.Writer.TryWrite(queryEvent);
        }
    }

    public async IAsyncEnumerable<MongoProfilerQueryEvent> Subscribe([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var subscriberId = Guid.NewGuid();
        var channel = Channel.CreateBounded<MongoProfilerQueryEvent>(new BoundedChannelOptions(2_000)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _subscribers[subscriberId] = channel;

        try
        {
            await foreach (var queryEvent in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return queryEvent;
            }
        }
        finally
        {
            _subscribers.TryRemove(subscriberId, out _);
            channel.Writer.TryComplete();
        }
    }
}
