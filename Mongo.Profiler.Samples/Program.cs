using System.Diagnostics;
using System.Text.Json; 
using Mongo.Profiler;
using Mongo.Profiler.Client.Console;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;

var options = LoadOptions();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(
        outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var hostBuilder = Host.CreateApplicationBuilder(args);
hostBuilder.Services.AddMongoProfilerPublisher(relayOptions =>
{
    relayOptions.Port = options.GrpcPort;
    relayOptions.ListenOnAnyIp = false;
});
using var host = hostBuilder.Build();
await host.StartAsync();

try
{
    using var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog(Log.Logger, dispose: false));
    ILogger mongoLogger = loggerFactory.CreateLogger("MongoProfiler");
    var sink = host.Services.GetRequiredService<IMongoProfilerEventSink>();

    Log.Information("Mongo profiler relay listening on localhost:{GrpcPort}.", options.GrpcPort);
    Log.Information("Waiting for at least one viewer subscriber...");
    await host.Services.WaitForMongoProfilerSubscriberAsync();
    Log.Information("Subscriber connected. Interactive sample is ready.");

    var settings = MongoClientSettings.FromConnectionString(options.ConnectionString);
    settings.ServerSelectionTimeout = TimeSpan.FromMilliseconds(Math.Clamp(options.MongoServerSelectionTimeoutMs, 250, 60_000));
    settings.ConnectTimeout = TimeSpan.FromMilliseconds(Math.Clamp(options.MongoConnectTimeoutMs, 250, 60_000));
    settings = settings.UseMongoProfiler(sink, mongoLogger);
    var client = new MongoClient(settings);
    var database = client.GetDatabase(options.DatabaseName);
    var orders = database.GetCollection<Order>(options.CollectionName);

    //await SeedOrdersAsync(orders);
    await RunInteractiveLoopAsync(orders, sink, options, mongoLogger);
}
finally
{
    await host.StopAsync();
    Log.CloseAndFlush();
}

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

static async Task RunInteractiveLoopAsync(
    IMongoCollection<Order> orders,
    IMongoProfilerEventSink? sink,
    SampleOptions options,
    ILogger mongoLogger)
{
    Log.Information("Mongo profiler sample is ready.");
    Log.Information(
        "Press [F] Find, [A] Aggregate, [B] Both, [W] Write Ops, [E] Error Case, [M] Multi-session, [V] Validation Suite, [P] Perf Benchmark, [S] Reseed, [Q] Quit.");
    var mongoAvailability = new MongoAvailabilityGate(sink);

    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        Log.Information("Key pressed: {Key}", key.Key);
        Func<Task>? action = key.Key switch
        {
            ConsoleKey.F => () => RunFindSampleAsync(orders),
            ConsoleKey.A => () => RunAggregateSampleAsync(orders),
            ConsoleKey.B => async () =>
            {
                await RunFindSampleAsync(orders);
                await RunAggregateSampleAsync(orders);
            },
            ConsoleKey.W => () => RunWriteSamplesAsync(orders),
            ConsoleKey.E => () => RunFailureSampleAsync(orders),
            ConsoleKey.M => () => RunMultiSessionSampleAsync(orders),
            ConsoleKey.V => () => RunValidationSuiteAsync(orders),
            ConsoleKey.P => () => RunEnrichmentOverheadBenchmarkAsync(orders, options, mongoLogger),
            ConsoleKey.S => () => SeedOrdersAsync(orders),
            ConsoleKey.Q => null,
            ConsoleKey.Escape => null,
            _ => null
        };

        if (key.Key is ConsoleKey.Q or ConsoleKey.Escape)
        {
            Log.Information("Stopping sample.");
            return;
        }

        try
        {
            if (action is null)
            {
                Log.Information("No action bound to key: {Key}", key.Key);
                continue;
            }

            if (!await mongoAvailability.CanRunAsync(orders.Database.Client))
                continue;

            await action();
        }
        catch (TimeoutException timeoutException)
        {
            Log.Warning("Mongo server timeout. Continuing without crashing. {ShortMessage}", ShortMongoMessage(timeoutException));
        }
        catch (MongoConnectionException connectionException)
        {
            Log.Warning("Mongo server unavailable. Continuing without crashing. {ShortMessage}", ShortMongoMessage(connectionException));
        }
        catch (MongoException mongoException)
        {
            Log.Warning("Mongo operation failed. Continuing. {ShortMessage}", ShortMongoMessage(mongoException));
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Unexpected error in interactive loop.");
        }
    }
}

static async Task SeedOrdersAsync(IMongoCollection<Order> orders)
{
    await orders.DeleteManyAsync(FilterDefinition<Order>.Empty);

    var seedOrders = new[]
    {
        new Order
        {
            Customer = "Alice",
            City = "London",
            Status = "paid",
            Amount = 125.50m,
            OrderedAt = new DateTimeOffset(2026, 3, 20, 8, 30, 0, TimeSpan.Zero)
        },
        new Order
        {
            Customer = "Bob",
            City = "London",
            Status = "pending",
            Amount = 80.00m,
            OrderedAt = new DateTimeOffset(2026, 3, 21, 10, 15, 0, TimeSpan.Zero)
        },
        new Order
        {
            Customer = "Carla",
            City = "Berlin",
            Status = "paid",
            Amount = 230.00m,
            OrderedAt = new DateTimeOffset(2026, 3, 22, 9, 0, 0, TimeSpan.Zero)
        },
        new Order
        {
            Customer = "Dan",
            City = "Prague",
            Status = "paid",
            Amount = 42.00m,
            OrderedAt = new DateTimeOffset(2026, 3, 22, 11, 45, 0, TimeSpan.Zero)
        }
    };

    await orders.InsertManyAsync(seedOrders);
    Log.Information("Seeded {OrderCount} order documents.", seedOrders.Length);
}

static async Task RunFindSampleAsync(IMongoCollection<Order> orders)
{
    var startDate = new DateTimeOffset(2026, 3, 21, 0, 0, 0, TimeSpan.Zero);
    var filter = Builders<Order>.Filter.Gte(x => x.Amount, 90m) &
                 Builders<Order>.Filter.Eq(x => x.Status, "paid") &
                 Builders<Order>.Filter.Gte(x => x.OrderedAt, startDate);

    var projected = await orders.Find(filter)
        .SortByDescending(x => x.Amount)
        .Project(x => new
        {
            x.Customer,
            x.City,
            x.Amount,
            x.OrderedAt
        })
        .Limit(3)
        .ToListAsync();

    Log.Information("Find sample returned {RowCount} rows.", projected.Count);
}

static async Task RunAggregateSampleAsync(IMongoCollection<Order> orders)
{
    var cityTotals = await orders.Aggregate()
        .Match(x => x.Status == "paid")
        .Group(
            x => x.City,
            g => new CityTotal
            {
                City = g.Key,
                TotalAmount = g.Sum(x => x.Amount),
                PaidOrders = g.Count()
            })
        .SortByDescending(x => x.TotalAmount)
        .ToListAsync();

    Log.Information("Aggregate sample totals:");
    foreach (var total in cityTotals)
        Log.Information("- {City}: {TotalAmount} ({PaidOrders} orders)", total.City, total.TotalAmount, total.PaidOrders);
}

static async Task RunWriteSamplesAsync(IMongoCollection<Order> orders)
{
    var insert = new Order
    {
        Customer = "Write-Sample",
        City = "Madrid",
        Status = "pending",
        Amount = 91.75m,
        OrderedAt = DateTimeOffset.UtcNow
    };
    await orders.InsertOneAsync(insert);

    var updateFilter = Builders<Order>.Filter.Eq(x => x.Customer, "Write-Sample");
    var update = Builders<Order>.Update.Set(x => x.Status, "paid").Set(x => x.Amount, 99.99m);
    var updateResult = await orders.UpdateOneAsync(updateFilter, update);

    var deleteResult = await orders.DeleteOneAsync(updateFilter);
    Log.Information("Write sample completed. Modified: {ModifiedCount}, Deleted: {DeletedCount}",
        updateResult.ModifiedCount, deleteResult.DeletedCount);
}

static async Task RunFailureSampleAsync(IMongoCollection<Order> orders)
{
    try
    {
        await orders.Database.RunCommandAsync<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument
        {
            { "find", orders.CollectionNamespace.CollectionName },
            { "filter", new MongoDB.Bson.BsonDocument("thisFieldDoesNotExist", new MongoDB.Bson.BsonDocument("$unknown", 1)) }
        });
    }
    catch (Exception exception)
    {
        Log.Information("Failure sample triggered expected error: {ErrorMessage}", exception.Message);
    }
}

static async Task RunMultiSessionSampleAsync(IMongoCollection<Order> orders)
{
    var client = orders.Database.Client;
    using var session1 = await client.StartSessionAsync();
    using var session2 = await client.StartSessionAsync();

    var paidFilter = Builders<Order>.Filter.Eq(x => x.Status, "paid");
    var pendingFilter = Builders<Order>.Filter.Eq(x => x.Status, "pending");

    var paidCount = await orders.CountDocumentsAsync(session1, paidFilter);
    var pendingCount = await orders.CountDocumentsAsync(session2, pendingFilter);
    Log.Information("Multi-session sample complete. Paid={PaidCount}, Pending={PendingCount}", paidCount, pendingCount);
}

static async Task RunValidationSuiteAsync(IMongoCollection<Order> orders)
{
    Log.Information("Running validation suite...");
    await SeedOrdersAsync(orders);
    await RunFindSampleAsync(orders);
    await RunAggregateSampleAsync(orders);
    await RunWriteSamplesAsync(orders);
    await RunFailureSampleAsync(orders);
    await RunMultiSessionSampleAsync(orders);
    Log.Information("Validation suite complete. Review viewer for read/write/error/session traces.");
}

static async Task RunEnrichmentOverheadBenchmarkAsync(
    IMongoCollection<Order> profiledOrders,
    SampleOptions options,
    ILogger mongoLogger)
{
    Log.Information("Running enrichment overhead benchmark (baseline vs profiled)...");
    await SeedOrdersAsync(profiledOrders);

    var baselineSettings = MongoClientSettings.FromConnectionString(options.ConnectionString);
    baselineSettings.ServerSelectionTimeout = TimeSpan.FromMilliseconds(Math.Clamp(options.MongoServerSelectionTimeoutMs, 250, 60_000));
    baselineSettings.ConnectTimeout = TimeSpan.FromMilliseconds(Math.Clamp(options.MongoConnectTimeoutMs, 250, 60_000));
    var baselineClient = new MongoClient(baselineSettings);
    var baselineOrders = baselineClient.GetDatabase(options.DatabaseName).GetCollection<Order>(options.CollectionName);

    var benchmarkOptions = BuildProfilerOptions(options);
    var countingSink = new CountingSink();
    var enrichedSettings = MongoClientSettings.FromConnectionString(options.ConnectionString);
    enrichedSettings.ServerSelectionTimeout = TimeSpan.FromMilliseconds(Math.Clamp(options.MongoServerSelectionTimeoutMs, 250, 60_000));
    enrichedSettings.ConnectTimeout = TimeSpan.FromMilliseconds(Math.Clamp(options.MongoConnectTimeoutMs, 250, 60_000));
    enrichedSettings = enrichedSettings.SubscribeToMongoQueries(mongoLogger, countingSink, benchmarkOptions);
    var enrichedClient = new MongoClient(enrichedSettings);
    var enrichedOrders = enrichedClient.GetDatabase(options.DatabaseName).GetCollection<Order>(options.CollectionName);

    const int warmupIterations = 25;
    const int measuredIterations = 200;

    var baselineDurations = await RunBenchmarkIterationsAsync(baselineOrders, warmupIterations, measuredIterations);
    var enrichedDurations = await RunBenchmarkIterationsAsync(enrichedOrders, warmupIterations, measuredIterations);

    var baselineStats = BenchmarkStats.Create("baseline_no_enrichment", baselineDurations);
    var enrichedStats = BenchmarkStats.Create("enrichment_enabled", enrichedDurations);

    var overheadMs = enrichedStats.AverageMs - baselineStats.AverageMs;
    var overheadPercent = baselineStats.AverageMs <= 0 ? 0 : overheadMs / baselineStats.AverageMs * 100d;

    var report = BuildBenchmarkReport(
        baselineStats,
        enrichedStats,
        overheadMs,
        overheadPercent,
        measuredIterations,
        warmupIterations,
        countingSink.PublishedCount);

    var reportPath = Path.Combine(Environment.CurrentDirectory, "BENCHMARK_RESULTS.md");
    await File.AppendAllTextAsync(reportPath, report);

    Log.Information("Benchmark complete. Avg baseline={BaselineAvg:F2}ms, enriched={EnrichedAvg:F2}ms, overhead={OverheadPercent:F2}%.",
        baselineStats.AverageMs, enrichedStats.AverageMs, overheadPercent);
    Log.Information("Benchmark report appended to {ReportPath}.", reportPath);
}

static MongoProfilerOptions BuildProfilerOptions(SampleOptions options)
{
    return new MongoProfilerOptions
    {
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
    };
}

static async Task<double[]> RunBenchmarkIterationsAsync(
    IMongoCollection<Order> orders,
    int warmupIterations,
    int measuredIterations)
{
    for (var i = 0; i < warmupIterations; i++)
        await ExecuteBenchmarkFindAsync(orders);

    var durationsMs = new double[measuredIterations];
    for (var i = 0; i < measuredIterations; i++)
    {
        var sw = Stopwatch.StartNew();
        await ExecuteBenchmarkFindAsync(orders);
        sw.Stop();
        durationsMs[i] = sw.Elapsed.TotalMilliseconds;
    }

    return durationsMs;
}

static async Task ExecuteBenchmarkFindAsync(IMongoCollection<Order> orders)
{
    var startDate = new DateTimeOffset(2026, 3, 21, 0, 0, 0, TimeSpan.Zero);
    var filter = Builders<Order>.Filter.Gte(x => x.Amount, 50m) &
                 Builders<Order>.Filter.Eq(x => x.Status, "paid") &
                 Builders<Order>.Filter.Gte(x => x.OrderedAt, startDate);

    _ = await orders.Find(filter)
        .SortByDescending(x => x.Amount)
        .Project(x => new
        {
            x.Customer,
            x.City,
            x.Amount
        })
        .Limit(10)
        .ToListAsync();
}

static string BuildBenchmarkReport(
    BenchmarkStats baselineStats,
    BenchmarkStats enrichedStats,
    double overheadMs,
    double overheadPercent,
    int measuredIterations,
    int warmupIterations,
    long publishedCount)
{
    return string.Join(
        Environment.NewLine,
        "",
        $"## {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
        "",
        $"- Warmup iterations: {warmupIterations}",
        $"- Measured iterations per mode: {measuredIterations}",
        $"- Enriched mode emitted events: {publishedCount}",
        "",
        "| Mode | Avg (ms) | P50 (ms) | P95 (ms) | Min (ms) | Max (ms) |",
        "| --- | ---: | ---: | ---: | ---: | ---: |",
        $"| {baselineStats.Name} | {baselineStats.AverageMs:F3} | {baselineStats.P50Ms:F3} | {baselineStats.P95Ms:F3} | {baselineStats.MinMs:F3} | {baselineStats.MaxMs:F3} |",
        $"| {enrichedStats.Name} | {enrichedStats.AverageMs:F3} | {enrichedStats.P50Ms:F3} | {enrichedStats.P95Ms:F3} | {enrichedStats.MinMs:F3} | {enrichedStats.MaxMs:F3} |",
        "",
        $"- Average overhead: {overheadMs:F3} ms ({overheadPercent:F2}%)",
        "",
        "");
}

static string ShortMongoMessage(Exception exception)
{
    var message = exception.Message ?? string.Empty;
    var firstLine = message.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    if (string.IsNullOrWhiteSpace(firstLine))
        return "Mongo connection or operation failed.";

    return firstLine.Length > 220 ? $"{firstLine[..220]}..." : firstLine;
}

internal sealed class CountingSink : IMongoProfilerEventSink
{
    private long _publishedCount;
    public long PublishedCount => Interlocked.Read(ref _publishedCount);

    public void Publish(MongoProfilerQueryEvent eventData)
    {
        Interlocked.Increment(ref _publishedCount);
    }
}

internal readonly record struct BenchmarkStats(
    string Name,
    double AverageMs,
    double P50Ms,
    double P95Ms,
    double MinMs,
    double MaxMs)
{
    public static BenchmarkStats Create(string name, double[] durationsMs)
    {
        if (durationsMs.Length == 0)
            return new BenchmarkStats(name, 0, 0, 0, 0, 0);

        var ordered = durationsMs.OrderBy(x => x).ToArray();
        return new BenchmarkStats(
            name,
            durationsMs.Average(),
            Percentile(ordered, 0.50),
            Percentile(ordered, 0.95),
            ordered[0],
            ordered[^1]);
    }

    private static double Percentile(double[] ordered, double percentile)
    {
        if (ordered.Length == 0)
            return 0;

        var index = (ordered.Length - 1) * percentile;
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper)
            return ordered[lower];

        var fraction = index - lower;
        return ordered[lower] + (ordered[upper] - ordered[lower]) * fraction;
    }
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

internal sealed class Order
{
    [BsonId]
    public MongoDB.Bson.ObjectId Id { get; set; }

    public string Customer { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTimeOffset OrderedAt { get; set; }
}

internal sealed class CityTotal
{
    public string City { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int PaidOrders { get; set; }
}

internal sealed class MongoAvailabilityGate
{
    private readonly IMongoProfilerEventSink? _sink;
    private DateTimeOffset _nextRetryUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastWarningUtc = DateTimeOffset.MinValue;

    public MongoAvailabilityGate(IMongoProfilerEventSink? sink = null)
    {
        _sink = sink;
    }

    public async Task<bool> CanRunAsync(IMongoClient client)
    {
        var now = DateTimeOffset.UtcNow;
        if (now < _nextRetryUtc)
        {
            if (now - _lastWarningUtc > TimeSpan.FromSeconds(3))
            {
                var message = $"Mongo still unavailable. Next retry after {_nextRetryUtc.ToLocalTime():HH:mm:ss}.";
                Log.Warning(message);
                PublishAvailabilityWarning(message);
                _lastWarningUtc = now;
            }

            return false;
        }

        try
        {
            using var pingTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
            var adminDatabase = client.GetDatabase("admin");
            await adminDatabase.RunCommandAsync<MongoDB.Bson.BsonDocument>(
                new MongoDB.Bson.BsonDocument("ping", 1),
                cancellationToken: pingTimeout.Token);
            return true;
        }
        catch
        {
            _nextRetryUtc = now.AddSeconds(5);
            _lastWarningUtc = now;
            const string message = "Mongo is not reachable at the moment. Actions will retry in 5 seconds.";
            Log.Warning(message);
            PublishAvailabilityWarning(message);
            return false;
        }
    }

    private void PublishAvailabilityWarning(string message)
    {
        if (_sink is null)
            return;

        try
        {
            _sink.Publish(new MongoProfilerQueryEvent
            {
                CommandName = "availability_check",
                DatabaseName = "admin",
                CollectionName = string.Empty,
                Query = "db.runCommand({ ping: 1 })",
                DurationMs = 0,
                Success = false,
                ErrorMessage = message,
                QueryFingerprint = "SYSTEM:AVAILABILITY_CHECK"
            });
        }
        catch
        {
            // Availability diagnostics should never interrupt sample loop.
        }
    }
}
