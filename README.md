# open-profilers

`open-profilers` is a .NET solution for capturing MongoDB command activity, streaming it to viewer clients over gRPC, and experimenting with query enrichment features such as redaction, fingerprints, and basic index advice.

## Projects

- `Mongo.Profiler`: core MongoDB driver instrumentation and event publishing.
- `Mongo.Profiler.Client`: app-side registration helpers for worker and generic hosts.
- `Mongo.Profiler.AspNet`: ASP.NET Core helpers for exposing the viewer subscription endpoint.
- `Mongo.Profiler.Grpc`: gRPC contract and subscriber service.
- `Mongo.Profiler.Viewer`: Avalonia desktop viewer for Mongo profiler events.
- `EFCore.Profiler` and `EFCore.Profiler.Viewer`: EF Core-oriented profiling pieces and viewer.
- `Mongo.Profiler.SampleApi`, `Mongo.Profiler.SampleConsoleApp`, `Mongo.Profiler.Samples`: runnable examples.
- `Mongo.Profiler.Tests`: test project for core formatting behavior.

## Current integration model

The codebase supports two main hosting patterns:

1. ASP.NET Core app hosts the gRPC subscription endpoint itself.
2. Worker or console app runs a lightweight relay in a hosted background service.
3. Plain app starts the relay explicitly without adopting `HostBuilder`.

In both cases, MongoDB commands are intercepted from `MongoClientSettings`, transformed into `MongoProfilerQueryEvent` messages, and pushed through `MongoProfilerEventChannelBroadcaster` for viewer subscribers.

## ASP.NET Core example

```csharp
using Mongo.Profiler.Client.AspNet;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

builder.AddMongoProfiler(options =>
{
    options.Port = 5179;
    options.ListenOnAnyIp = false;
});

builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var settings = MongoClientSettings.FromConnectionString("mongodb://localhost:27017");
    settings = settings.UseMongoProfiler(serviceProvider);
    return new MongoClient(settings);
});

var app = builder.Build();
app.MapMongoProfiler();
app.Run();
```

Use `builder.AddMongoProfiler(...)` when the web app itself should expose the gRPC stream used by the viewer.

## Worker or console host example

```csharp
using Microsoft.Extensions.Hosting;
using Mongo.Profiler.Client;
using MongoDB.Driver;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMongoProfilerPublisher(options =>
{
    options.Port = 5179;
    options.ListenOnAnyIp = false;
});

builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var settings = MongoClientSettings.FromConnectionString("mongodb://localhost:27017");
    var sink = serviceProvider.GetRequiredService<IMongoProfilerEventSink>();
    settings = settings.UseMongoProfiler(sink);
    return new MongoClient(settings);
});

using var host = builder.Build();
await host.StartAsync();

await host.Services.WaitForMongoProfilerSubscriberAsync();
```

Use `AddMongoProfilerPublisher(...)` when there is no ASP.NET request pipeline and you still want a viewer to subscribe over gRPC.

## Plain app example

```csharp
using Mongo.Profiler.Client;
using MongoDB.Driver;

await using var relay = await MongoProfilerRelay.StartAsync(options =>
{
    options.Port = 5179;
    options.ListenOnAnyIp = false;
});

await relay.WaitForMongoProfilerSubscriberAsync();

var settings = MongoClientSettings.FromConnectionString("mongodb://localhost:27017");
settings = settings.UseMongoProfiler(relay.Sink);

var client = new MongoClient(settings);
```

Use `MongoProfilerRelay.StartAsync(...)` when the app does not use ASP.NET Core or a generic host but still needs viewer streaming.

## Mongo client instrumentation

There are currently three practical ways to wire profiling into `MongoClientSettings`:

- `settings.UseMongoProfiler(serviceProvider)` in ASP.NET Core when the sink is resolved from DI.
- `settings.UseMongoProfiler(sink, logger)` when you already have a sink instance.
- `settings.UseMongoProfiler(logger)` when you only want logging and are not publishing events to the viewer pipeline.

The underlying core API is `SubscribeToMongoQueries(...)` in `Mongo.Profiler`.

## Event enrichment

The current `Mongo.Profiler` implementation captures more than timing information. Published events can include:

- command name, database, collection, request ID, operation ID, and server endpoint
- rendered query text and a normalized query fingerprint
- success or failure details including error code, code name, and labels
- reply metadata such as result count, cursor ID, and BSON payload sizes
- trace identifiers from the ambient `Activity`
- optional index-advice analysis for slow `find` and `aggregate` operations

Sensitive keys are redacted and long strings are truncated before query text is rendered.

## Running samples

- `Mongo.Profiler.SampleApi`: sample web API using ASP.NET and EF Core integration.
- `Mongo.Profiler.SampleConsoleApp`: simple hosted console sample.
- `Mongo.Profiler.Samples`: interactive sample that can seed data, run reads and writes, trigger failures, and append benchmark results to `BENCHMARK_RESULTS.md`.

## Notes

- The solution currently targets modern .NET and several projects multi-target `net8.0` and `net10.0`.
- Package-specific usage details continue to live in `Mongo.Profiler.Client/README.md`.
