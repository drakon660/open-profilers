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

    public MongoProfilerRelayHandle(
        WebApplication relayApp,
        MongoProfilerEventChannelBroadcaster broadcaster,
        MongoProfilerRelayHostedServiceOptions options)
    {
        _relayApp = relayApp;
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

        await _relayApp.StopAsync();
        await _relayApp.DisposeAsync();
        _relayApp = null;
    }
}
