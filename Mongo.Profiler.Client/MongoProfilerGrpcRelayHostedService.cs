using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Mongo.Profiler.Grpc;

namespace Mongo.Profiler.Client;

internal sealed class MongoProfilerGrpcRelayHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<MongoProfilerRelayHostedServiceOptions> _options;
    private WebApplication? _relayApp;

    public MongoProfilerGrpcRelayHostedService(
        IServiceProvider serviceProvider,
        IOptions<MongoProfilerRelayHostedServiceOptions> options)
    {
        _serviceProvider = serviceProvider;
        _options = options;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        if (!options.Enabled)
            return;

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

        var broadcaster = _serviceProvider.GetRequiredService<MongoProfilerEventChannelBroadcaster>();
        builder.Services.AddMongoProfilerGrpc();
        // The relay web host has its own DI container. Replace default registrations so
        // the gRPC subscriber and the producer app share the same broadcaster instance.
        builder.Services.Replace(ServiceDescriptor.Singleton<MongoProfilerEventChannelBroadcaster>(broadcaster));
        builder.Services.Replace(ServiceDescriptor.Singleton<IMongoProfilerEventSink>(broadcaster));

        _relayApp = builder.Build();
        _relayApp.MapMongoProfilerChannelSubscriberToGrpcStream();
        await _relayApp.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_relayApp is null)
            return;

        await _relayApp.StopAsync(cancellationToken);
        await _relayApp.DisposeAsync();
        _relayApp = null;
    }
}
