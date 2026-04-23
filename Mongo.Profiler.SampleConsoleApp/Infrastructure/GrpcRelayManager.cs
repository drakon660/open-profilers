using Mongo.Profiler.Client;

namespace Mongo.Profiler.SampleConsoleApp.Infrastructure;

internal sealed class GrpcRelayManager : IAsyncDisposable
{
    private readonly MongoProfilerEventChannelBroadcaster _broadcaster;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IMongoProfilerRelayHandle? _handle;

    public GrpcRelayManager(MongoProfilerEventChannelBroadcaster broadcaster, int defaultPort)
    {
        _broadcaster = broadcaster;
        DefaultPort = defaultPort;
    }

    public int DefaultPort { get; }
    public bool IsRunning => _handle is not null;
    public string? Address => _handle?.Address;
    public int? RunningPort => _handle?.Port;

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_handle is not null)
                return false;

            _handle = await MongoProfilerRelay.StartAsync(
                _broadcaster,
                options =>
                {
                    options.Port = DefaultPort;
                    options.ListenOnAnyIp = false;
                },
                cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_handle is null)
                return false;

            await _handle.DisposeAsync();
            _handle = null;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _gate.Dispose();
    }
}
