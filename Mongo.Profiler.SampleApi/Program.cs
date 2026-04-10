using Carter;
using Microsoft.EntityFrameworkCore;
using Mongo.Profiler;
using Mongo.Profiler.Client.AspNet;
using EFCore.Profiler;
using Mongo.Profiler.SampleApi.Data;

var builder = WebApplication.CreateBuilder(args);
var grpcPort = builder.Configuration.GetValue("GrpcPort", 5179);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCarter();
builder.AddMongoProfiler(options =>
{
    options.Port = grpcPort;
    options.ListenOnAnyIp = false;
});
builder.Services.AddSingleton<IMongoProfilerEventSink>(serviceProvider =>
{
    var broadcaster = serviceProvider.GetRequiredService<MongoProfilerEventChannelBroadcaster>();
    return new ConsoleProfilerEventSink(broadcaster);
});

var efProfilerSection = builder.Configuration.GetSection("EfCoreProfiler");
builder.Services.AddEfCoreProfiler(options =>
{
    options.Enabled = efProfilerSection.GetValue<bool?>("Enabled") ?? options.Enabled;
    options.MinDurationMs = efProfilerSection.GetValue<int?>("MinDurationMs") ?? options.MinDurationMs;
    options.CaptureParameters = efProfilerSection.GetValue<bool?>("CaptureParameters") ?? options.CaptureParameters;
    options.MaxSqlLength = efProfilerSection.GetValue<int?>("MaxSqlLength") ?? options.MaxSqlLength;
    options.BlockUnsafeDmlWithoutWhere = efProfilerSection.GetValue<bool?>("BlockUnsafeDmlWithoutWhere") ?? options.BlockUnsafeDmlWithoutWhere;
});

var baseOrdersConnectionString = builder.Configuration.GetConnectionString("OrdersDb")
    ?? throw new InvalidOperationException("Connection string 'OrdersDb' is not configured.");

void ConfigureOrdersDbContext(IServiceProvider serviceProvider, DbContextOptionsBuilder options)
{
    options.UseLazyLoadingProxies();
    options.UseSqlServer(baseOrdersConnectionString);
    options.AddEfCoreProfiler(serviceProvider);
}

builder.Services.AddDbContext<OrdersDbContext>(ConfigureOrdersDbContext);
builder.Services.AddDbContextFactory<OrdersDbContext>(ConfigureOrdersDbContext, ServiceLifetime.Scoped);

var app = builder.Build();
HibernatingRhinos.Profiler.Appender.EntityFramework.EntityFrameworkProfiler.Initialize();
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    dbContext.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapCarter();
app.MapMongoProfiler();

app.Run();

internal sealed class ConsoleProfilerEventSink : IMongoProfilerEventSink
{
    private readonly IMongoProfilerEventSink _next;

    public ConsoleProfilerEventSink(IMongoProfilerEventSink next)
    {
        _next = next;
    }

    public void Publish(MongoProfilerQueryEvent queryEvent)
    {
        _next.Publish(queryEvent);

        var reason = queryEvent.IndexAdviceReason ?? string.Empty;
        var hasThreadIssue =
            reason.Contains("shared_context_parallel_use", StringComparison.Ordinal) ||
            reason.Contains("context_thread_hop_only", StringComparison.Ordinal);

        var summary =
            $"[EF-PROFILER] cmd={queryEvent.CommandName} db={queryEvent.DatabaseName} table={queryEvent.CollectionName} " +
            $"durationMs={queryEvent.DurationMs:F1} resultCount={queryEvent.ResultCount?.ToString() ?? "n/a"} " +
            $"contextId={queryEvent.SessionId}";
        if (!string.IsNullOrWhiteSpace(reason))
            summary += $" alerts={queryEvent.IndexAdviceStatus}:{reason}";

        if (hasThreadIssue)
            Console.WriteLine($"[THREAD-CONTEXT-ALERT] {summary}");
        else
            Console.WriteLine(summary);

        // Console.WriteLine(
        //     $"[ProfilerEvent] {DateTimeOffset.UtcNow:O}{Environment.NewLine}{JsonSerializer.Serialize(queryEvent)}");
    }
}
