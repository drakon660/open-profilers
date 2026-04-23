using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Mongo.Profiler;
using Mongo.Profiler.Client;
using Mongo.Profiler.SampleConsoleApp.Models;
using MongoDB.Driver;

namespace Mongo.Profiler.SampleConsoleApp.Infrastructure;

internal static class SampleHost
{
    public static IHost Build(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.Configure<SampleOptions>(builder.Configuration);
        builder.Services.Configure<MongoProfilerOptions>(builder.Configuration.GetSection("MongoProfiler"));

        var broadcaster = new MongoProfilerEventChannelBroadcaster();
        builder.Services.AddMongoProfilerBroadcaster(broadcaster);
        builder.Services.AddSingleton(serviceProvider =>
        {
            var sampleOptions = serviceProvider.GetRequiredService<IOptions<SampleOptions>>().Value;
            return new GrpcRelayManager(broadcaster, sampleOptions.GrpcPort);
        });

        builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
        {
            var sampleOptions = serviceProvider.GetRequiredService<IOptions<SampleOptions>>().Value;
            var settings = MongoClientSettings.FromConnectionString(sampleOptions.ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromMilliseconds(Math.Clamp(sampleOptions.MongoServerSelectionTimeoutMs, 250, 60_000));
            settings.ConnectTimeout = TimeSpan.FromMilliseconds(Math.Clamp(sampleOptions.MongoConnectTimeoutMs, 250, 60_000));

            settings = settings.UseMongoProfiler(serviceProvider);
            return new MongoClient(settings);
        });

        return builder.Build();
    }
}
