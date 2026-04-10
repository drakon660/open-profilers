using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Mongo.Profiler.Client.OtherDI;

public static class MongoProfilerDependencyInjectionExtensions
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
}
