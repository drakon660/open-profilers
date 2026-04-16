namespace Mongo.Profiler.Client;

public static class MongoProfilerRelay
{
    public static Task<IMongoProfilerRelayHandle> StartAsync(
        Action<MongoProfilerRelayHostedServiceOptions>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var broadcaster = new MongoProfilerEventChannelBroadcaster();
        return StartAsync(broadcaster, configure, cancellationToken);
    }

    public static async Task<IMongoProfilerRelayHandle> StartAsync(
        MongoProfilerEventChannelBroadcaster broadcaster,
        Action<MongoProfilerRelayHostedServiceOptions>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(broadcaster);

        var options = new MongoProfilerRelayHostedServiceOptions();
        configure?.Invoke(options);

        var relayApp = await MongoProfilerRelayHost.StartAsync(broadcaster, options, cancellationToken);
        if (relayApp is null)
            throw new InvalidOperationException("Mongo profiler relay is disabled and cannot be started.");

        return new MongoProfilerRelayHandle(relayApp, broadcaster, options);
    }
}
