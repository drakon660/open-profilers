using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Mongo.Profiler;
using Mongo.Profiler.Grpc.Services;

namespace Mongo.Profiler.Grpc;

public static class MongoProfilerGrpcServiceCollectionExtensions
{
    public static IServiceCollection AddMongoProfilerGrpc(this IServiceCollection services)
    {
        services.AddGrpc();
        services.AddMongoProfilerChannel();
        return services;
    }

    public static IServiceCollection AddMongoProfilerChannel(this IServiceCollection services)
    {
        services.AddSingleton<MongoProfilerEventChannelBroadcaster>();
        services.AddSingleton<IMongoProfilerEventSink>(provider => provider.GetRequiredService<MongoProfilerEventChannelBroadcaster>());
        return services;
    }

    public static IEndpointRouteBuilder MapMongoProfilerChannelSubscriberToGrpcStream(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGrpcService<ProfilerStreamService>();
        return endpoints;
    }
}
