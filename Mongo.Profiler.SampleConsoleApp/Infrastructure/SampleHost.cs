using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mongo.Profiler;
using Mongo.Profiler.Client;
using Mongo.Profiler.SampleConsoleApp.Models;
using MongoDB.Driver;

namespace Mongo.Profiler.SampleConsoleApp.Infrastructure;

internal static class SampleHost
{
    public static IHost Build(string[] args, SampleOptions options)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddMongoProfilerPublisher(relayOptions =>
        {
            relayOptions.Port = options.GrpcPort;
            relayOptions.ListenOnAnyIp = false;
        });

        builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
        {
            var settings = MongoClientSettings.FromConnectionString(options.ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromMilliseconds(Math.Clamp(options.MongoServerSelectionTimeoutMs, 250, 60_000));
            settings.ConnectTimeout = TimeSpan.FromMilliseconds(Math.Clamp(options.MongoConnectTimeoutMs, 250, 60_000));

            var sink = serviceProvider.GetRequiredService<IMongoProfilerEventSink>();
            settings = settings.SubscribeToMongoQueries(
                sink: sink,
                options: new MongoProfilerOptions
                {
                    ApplicationName = "Mongo.Profiler.SampleConsoleApp",
                    IndexAdvisor = new MongoProfilerIndexAdvisorOptions
                    {
                        Enabled = options.EnableIndexAdvisor,
                        SlowQueryThresholdMs = options.IndexAdvisorSlowQueryThresholdMs,
                        MinDocsExaminedForWarning = options.IndexAdvisorMinDocsExaminedForWarning,
                        MaxAnalysesPerFingerprintPerMinute = options.IndexAdvisorMaxAnalysesPerFingerprintPerMinute,
                        ExplainTimeoutMs = options.IndexAdvisorExplainTimeoutMs
                    },
                    Redaction = new MongoProfilerRedactionOptions
                    {
                        MaxStringLength = options.RedactionMaxStringLength,
                        SensitiveKeys = options.RedactionSensitiveKeys
                    }
                });

            return new MongoClient(settings);
        });

        return builder.Build();
    }
}
