using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
        var broadcaster = _serviceProvider.GetRequiredService<MongoProfilerEventChannelBroadcaster>();
        _relayApp = await MongoProfilerRelayHost.StartAsync(broadcaster, options, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_relayApp is null)
            return;

        // Unblock any in-flight gRPC Subscribe streams so Kestrel can drain and stop.
        var broadcaster = _serviceProvider.GetService<MongoProfilerEventChannelBroadcaster>();
        broadcaster?.CompleteAllSubscribers();

        await _relayApp.StopAsync(cancellationToken);
        await _relayApp.DisposeAsync();
        _relayApp = null;
    }
}
