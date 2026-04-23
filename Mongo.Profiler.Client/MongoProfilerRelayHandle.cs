using Microsoft.AspNetCore.Builder;

namespace Mongo.Profiler.Client;

public interface IMongoProfilerRelayHandle : IAsyncDisposable
{
    int Port { get; }
    string Address { get; }
    IMongoProfilerEventSink Sink { get; }
}

internal sealed class MongoProfilerRelayHandle : IMongoProfilerRelayHandle
{
    private WebApplication? _relayApp;
    private readonly MongoProfilerEventChannelBroadcaster _broadcaster;

    public MongoProfilerRelayHandle(
        WebApplication relayApp,
        MongoProfilerEventChannelBroadcaster broadcaster,
        MongoProfilerRelayHostedServiceOptions options)
    {
        _relayApp = relayApp;
        _broadcaster = broadcaster;
        Sink = broadcaster;
        Port = options.Port;
        Address = options.ListenOnAnyIp
            ? $"http://0.0.0.0:{options.Port}"
            : $"http://localhost:{options.Port}";
    }

    public int Port { get; }
    public string Address { get; }
    public IMongoProfilerEventSink Sink { get; }

    public async ValueTask DisposeAsync()
    {
        if (_relayApp is null)
            return;

        // Unblock any in-flight gRPC Subscribe streams so Kestrel can drain and stop.
        _broadcaster.CompleteAllSubscribers();

        await _relayApp.StopAsync();
        await _relayApp.DisposeAsync();
        _relayApp = null;
    }
}
