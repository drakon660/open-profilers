using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mongo.Profiler;
using Mongo.Profiler.Client;
using MongoDB.Bson;
using MongoDB.Driver;

var options = LoadOptions();
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
    settings = settings.UseMongoProfiler(sink);
    
    return new MongoClient(settings);
});

using var host = builder.Build();
await host.StartAsync();

Console.WriteLine($"Mongo profiler relay listening on localhost:{options.GrpcPort}.");
Console.WriteLine("Waiting for at least one viewer subscriber...");
await host.Services.WaitForMongoProfilerSubscriberAsync();
Console.WriteLine("Subscriber connected. Running sample query.");

var client = host.Services.GetRequiredService<IMongoClient>();
var orders = client.GetDatabase(options.DatabaseName).GetCollection<BsonDocument>(options.CollectionName);

try
{
    var count = await orders.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
    Console.WriteLine($"Collection '{options.DatabaseName}.{options.CollectionName}' count: {count}.");
}
catch (Exception exception)
{
    Console.WriteLine($"Sample query failed: {exception.GetType().Name}: {exception.Message}");
    Console.WriteLine("Relay is still running — check the viewer for connectivity events.");
}

Console.WriteLine("Press any key to stop...");
Console.ReadKey();

await host.StopAsync();


static SampleOptions LoadOptions()
{
    const string configPath = "appsettings.json";
    if (!File.Exists(configPath))
        return new SampleOptions();

    var json = File.ReadAllText(configPath);
    var options = JsonSerializer.Deserialize<SampleOptions>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    });

    return options ?? new SampleOptions();
}

internal sealed class SampleOptions
{
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string DatabaseName { get; set; } = "profiler_samples";
    public string CollectionName { get; set; } = "orders";
    public int GrpcPort { get; set; } = 5179;
    public bool EnableIndexAdvisor { get; set; } = true;
    public int IndexAdvisorSlowQueryThresholdMs { get; set; } = 300;
    public int IndexAdvisorMinDocsExaminedForWarning { get; set; } = 200;
    public int IndexAdvisorMaxAnalysesPerFingerprintPerMinute { get; set; } = 1;
    public int IndexAdvisorExplainTimeoutMs { get; set; } = 1500;
    public int RedactionMaxStringLength { get; set; } = 256;
    public int MongoServerSelectionTimeoutMs { get; set; } = 1500;
    public int MongoConnectTimeoutMs { get; set; } = 1500;
    public string[] RedactionSensitiveKeys { get; set; } =
    [
        "password",
        "passwd",
        "pwd",
        "token",
        "access_token",
        "refresh_token",
        "secret",
        "apiKey",
        "api_key",
        "authorization",
        "cookie"
    ];
}
