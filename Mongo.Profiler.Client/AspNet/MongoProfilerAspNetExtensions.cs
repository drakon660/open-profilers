using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Microsoft.AspNetCore.Routing;
using Mongo.Profiler.Grpc;

namespace Mongo.Profiler.Client.AspNet;

public static class MongoProfilerAspNetExtensions
{
    public static WebApplicationBuilder AddMongoProfiler(
        this WebApplicationBuilder builder,
        Action<MongoProfilerRelayHostedServiceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var profilerOptions = new MongoProfilerRelayHostedServiceOptions();
        configure?.Invoke(profilerOptions);

        builder.Services.AddGrpc();
        builder.Services.AddMongoProfilerChannel();
        if (!profilerOptions.Enabled)
            return builder;

        // If user already configured explicit Kestrel endpoints, do not override.
        if (builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
            return builder;

        var urlsValue = builder.Configuration["ASPNETCORE_URLS"] ?? builder.Configuration["urls"] ?? string.Empty;
        var configuredUrls = urlsValue
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static value => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null)
            .Where(static uri => uri is not null)
            .Cast<Uri>()
            .ToArray();

        builder.WebHost.ConfigureKestrel(options =>
        {
            var boundPorts = new HashSet<int>();

            foreach (var url in configuredUrls)
            {
                if (!boundPorts.Add(url.Port))
                    continue;

                if (string.Equals(url.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                    options.ListenLocalhost(url.Port, listenOptions => { listenOptions.Protocols = HttpProtocols.Http1AndHttp2; });
                else
                    options.ListenAnyIP(url.Port, listenOptions => { listenOptions.Protocols = HttpProtocols.Http1AndHttp2; });
            }

            if (boundPorts.Contains(profilerOptions.Port))
                return;

            if (profilerOptions.ListenOnAnyIp)
                options.ListenAnyIP(profilerOptions.Port, listenOptions => { listenOptions.Protocols = HttpProtocols.Http2; });
            else
                options.ListenLocalhost(profilerOptions.Port, listenOptions => { listenOptions.Protocols = HttpProtocols.Http2; });
        });

        return builder;
    }
    
    public static MongoClientSettings UseMongoProfiler(
        this MongoClientSettings settings,
        IServiceProvider serviceProvider,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        var sink = serviceProvider.GetRequiredService<IMongoProfilerEventSink>();
        return settings.SubscribeToMongoQueries(logger, sink);
    }

    public static IEndpointRouteBuilder MapMongoProfiler(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapMongoProfilerChannelSubscriberToGrpcStream();
    }
}
