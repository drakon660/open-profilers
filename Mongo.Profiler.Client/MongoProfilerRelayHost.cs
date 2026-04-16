using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mongo.Profiler.Grpc;

namespace Mongo.Profiler.Client;

internal static class MongoProfilerRelayHost
{
    public static async Task<WebApplication?> StartAsync(
        MongoProfilerEventChannelBroadcaster broadcaster,
        MongoProfilerRelayHostedServiceOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(broadcaster);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
            return null;

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            if (options.ListenOnAnyIp)
            {
                serverOptions.ListenAnyIP(options.Port, listenOptions => { listenOptions.Protocols = HttpProtocols.Http2; });
            }
            else
            {
                serverOptions.ListenLocalhost(options.Port, listenOptions => { listenOptions.Protocols = HttpProtocols.Http2; });
            }
        });

        builder.Services.AddMongoProfilerGrpc();
        // The relay web host has its own DI container. Replace default registrations so
        // the gRPC subscriber and the producer app share the same broadcaster instance.
        builder.Services.Replace(ServiceDescriptor.Singleton<MongoProfilerEventChannelBroadcaster>(broadcaster));
        builder.Services.Replace(ServiceDescriptor.Singleton<IMongoProfilerEventSink>(broadcaster));

        var relayApp = builder.Build();
        relayApp.MapMongoProfilerChannelSubscriberToGrpcStream();
        await relayApp.StartAsync(cancellationToken);
        return relayApp;
    }
}
