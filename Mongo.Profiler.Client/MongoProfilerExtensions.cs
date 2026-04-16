using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Mongo.Profiler.Client;

public static class MongoProfilerExtensions
{
    public static IServiceCollection AddMongoProfiler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<MongoProfilerEventChannelBroadcaster>();
        services.TryAddSingleton<IMongoProfilerEventSink>(provider =>
            provider.GetRequiredService<MongoProfilerEventChannelBroadcaster>());
        return services;
    }

    public static IServiceCollection AddMongoProfilerPublisher(
        this IServiceCollection services,
        Action<MongoProfilerRelayHostedServiceOptions>? configure = null,
        Func<IMongoProfilerEventSink>? sink = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddMongoProfiler();

        if (sink is not null)
        {
            var broadcaster = (MongoProfilerEventChannelBroadcaster)sink();
            services.AddSingleton(broadcaster);
            services.AddSingleton<IMongoProfilerEventSink>(broadcaster);
        }

        var optionsBuilder = services.AddOptions<MongoProfilerRelayHostedServiceOptions>();
        if (configure is not null)
            optionsBuilder.Configure(configure);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, MongoProfilerGrpcRelayHostedService>());

        return services;
    }

    public static MongoClientSettings UseMongoProfiler(
        this MongoClientSettings settings,
        IMongoProfilerEventSink sink,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sink);
        return settings.SubscribeToMongoQueries(logger, sink);
    }
    
    public static MongoClientSettings UseMongoProfiler(
        this MongoClientSettings settings,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.SubscribeToMongoQueries(logger);
    }

    public static async Task WaitForMongoProfilerSubscriberAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default,
        int pollIntervalMs = 500)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        var broadcaster = serviceProvider.GetService<MongoProfilerEventChannelBroadcaster>();
        if (broadcaster is null)
            return;

        await WaitForSubscriberAsync(broadcaster, cancellationToken, pollIntervalMs);
    }

    public static async Task WaitForMongoProfilerSubscriberAsync(
        this IMongoProfilerRelayHandle relayHandle,
        CancellationToken cancellationToken = default,
        int pollIntervalMs = 500)
    {
        ArgumentNullException.ThrowIfNull(relayHandle);
        if (relayHandle.Sink is not MongoProfilerEventChannelBroadcaster broadcaster)
            return;

        await WaitForSubscriberAsync(broadcaster, cancellationToken, pollIntervalMs);
    }

    private static async Task WaitForSubscriberAsync(
        MongoProfilerEventChannelBroadcaster broadcaster,
        CancellationToken cancellationToken,
        int pollIntervalMs)
    {
        var safePollIntervalMs = Math.Clamp(pollIntervalMs, 50, 10_000);
        while (broadcaster.SubscriberCount == 0 && !cancellationToken.IsCancellationRequested)
            await Task.Delay(safePollIntervalMs, cancellationToken);
    }
}
