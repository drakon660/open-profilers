using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Mongo.Profiler.Client;

public static class MongoProfilerExtensions
{
    /// <summary>
    /// Conventional configuration section name bound to <see cref="MongoProfilerOptions"/>
    /// when the registration helpers run. Override by calling
    /// <c>services.Configure&lt;MongoProfilerOptions&gt;(...)</c> after registration.
    /// </summary>
    public const string DefaultConfigurationSection = "MongoProfiler";

    private static IServiceCollection AddMongoProfiler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<MongoProfilerEventChannelBroadcaster>();
        services.TryAddSingleton<IMongoProfilerEventSink>(provider =>
            provider.GetRequiredService<MongoProfilerEventChannelBroadcaster>());
        services.AddOptions<MongoProfilerOptions>().BindConfiguration(DefaultConfigurationSection);
        return services;
    }

    public static IServiceCollection AddMongoProfilerBroadcaster(
        this IServiceCollection services,
        MongoProfilerEventChannelBroadcaster broadcaster)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(broadcaster);

        services.AddSingleton(broadcaster);
        services.AddSingleton<IMongoProfilerEventSink>(broadcaster);
        services.AddOptions<MongoProfilerOptions>().BindConfiguration(DefaultConfigurationSection);
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
        IServiceProvider serviceProvider,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        var sink = serviceProvider.GetRequiredService<IMongoProfilerEventSink>();
        var profilerOptions = serviceProvider
            .GetService<IOptions<MongoProfilerOptions>>()?.Value ?? new MongoProfilerOptions();
        return settings.SubscribeToMongoQueries(logger, sink, profilerOptions);
    }

    public static MongoClientSettings UseMongoProfiler(
        this MongoClientSettings settings,
        IServiceProvider serviceProvider,
        IMongoProfilerEventSink sink,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(sink);
        var profilerOptions = serviceProvider
            .GetService<IOptions<MongoProfilerOptions>>()?.Value ?? new MongoProfilerOptions();
        return settings.SubscribeToMongoQueries(logger, sink, profilerOptions);
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
