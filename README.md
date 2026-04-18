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

The codebase supports three hosting patterns:

1. ASP.NET Core app hosts the gRPC subscription endpoint itself.
2. Worker or console app runs a lightweight relay in a hosted background service.
3. Plain app starts the relay explicitly without adopting `HostBuilder`.

In all cases, MongoDB commands are intercepted from `MongoClientSettings`, transformed into `MongoProfilerQueryEvent` messages, and pushed through `MongoProfilerEventChannelBroadcaster` for viewer subscribers.

## Getting started

Package-specific setup, full code snippets, and DI guidance live in [`Mongo.Profiler.Client/README.md`](Mongo.Profiler.Client/README.md). A minimal ASP.NET Core wiring looks like:

```csharp
builder.AddMongoProfiler(options => options.Port = 5179);

builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var settings = MongoClientSettings.FromConnectionString("mongodb://localhost:27017");
    return new MongoClient(settings.UseMongoProfiler(sp));
});

var app = builder.Build();
app.MapMongoProfiler();
```

See the client README for worker/console (`AddMongoProfilerPublisher`) and plain-app (`MongoProfilerRelay.StartAsync`) variants.

## Enriched events

Published `MongoProfilerQueryEvent` messages carry command metadata, query fingerprints, reply sizes, error details, trace ids, and optional index advice. The full field list and redaction behavior is documented in [`MONGO_PROFILER_FIELD_DICTIONARY.md`](MONGO_PROFILER_FIELD_DICTIONARY.md).

## Running samples

- `Mongo.Profiler.SampleApi`: sample web API using ASP.NET and EF Core integration.
- `Mongo.Profiler.SampleConsoleApp`: simple hosted console sample.
- `Mongo.Profiler.Samples`: interactive sample that can seed data, run reads and writes, trigger failures, and append benchmark results to `BENCHMARK_RESULTS.md`. Scenarios are documented in [`Mongo.Profiler.Samples/VALIDATION_SCENARIOS.md`](Mongo.Profiler.Samples/VALIDATION_SCENARIOS.md).

## Notes

- The solution currently targets modern .NET and several projects multi-target `net8.0` and `net10.0`.
