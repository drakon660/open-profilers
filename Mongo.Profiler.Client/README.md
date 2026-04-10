# Mongo.Profiler.Client

`Mongo.Profiler.Client` is a thin integration layer for any .NET app.

It reuses `Mongo.Profiler.Grpc` directly (no separate relay implementation in this project).

## Quick model

- `AddMongoProfiler()` registers profiler relay services in DI.
- `UseMongoProfiler(serviceProvider)` or `UseMongoProfiler(sink)` is applied while creating `MongoClientSettings`.
- `MapMongoProfiler()` is required only when your ASP.NET app should expose gRPC subscription endpoint itself.
- `AddMongoProfilerBridge(...)` starts relay in a background hosted service and optionally links an external sink.

## 1. Register services (ASP.NET Core app)

```csharp
using Mongo.Profiler.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMongoProfiler();
builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var settings = MongoClientSettings.FromConnectionString("mongodb://localhost:27017");
    settings = settings.UseMongoProfiler(serviceProvider);
    return new MongoClient(settings);
});

var app = builder.Build();

// Required in ASP.NET mode to expose gRPC subscription endpoint.
app.MapMongoProfiler();
```

## 1b. Register hosted relay (worker/batch app)

For non-web hosts (for example `Host.CreateApplicationBuilder()` workers), run relay as hosted service:

```csharp
using Mongo.Profiler.Client;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMongoProfilerBridge(options =>
{
    options.Port = 5179;
    options.ListenOnAnyIp = false; // true for container/remote access
});
builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var settings = MongoClientSettings.FromConnectionString("mongodb://localhost:27017");
    settings = settings.UseMongoProfiler(serviceProvider);
    return new MongoClient(settings);
});
```

This starts gRPC relay in background and exposes `Subscribe` for viewer clients.
In this mode, do not call `MapMongoProfiler()`.

Optionally wait until viewer connects before executing workload:

```csharp
await host.Services.WaitForMongoProfilerSubscriberAsync();
```

## 2. Connect profiler to MongoDB client

```csharp
using Mongo.Profiler.Client;
using MongoDB.Driver;

builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var settings = MongoClientSettings.FromConnectionString("mongodb://localhost:27017");
    settings = settings.UseMongoProfiler(serviceProvider);
    return new MongoClient(settings);
});

var client = app.Services.GetRequiredService<IMongoClient>();
```

If your registration callback does not expose `IServiceProvider`:

```csharp
IMongoProfilerEventSink GetProfilerSink() => factory.GetInstance<IMongoProfilerEventSink>();

factory.Register(config =>
{
    var settings = MongoClientSettings.FromConnectionString("mongodb://localhost:27017");
    settings = settings.UseMongoProfiler(GetProfilerSink());
    return new MongoClient(settings);
});
```

Every captured command is published to the in-process `ProfilerEventBroadcaster` from `Mongo.Profiler.Grpc`.

If your Mongo events should also flow into another DI sink:

```csharp
builder.Services.AddMongoProfilerBridge(
    options => { options.Port = 5179; },
    resolveExternalSink: _ => factory.GetInstance<IMongoProfilerEventSink>());
```

## 3. Viewer connection

Point viewer to your app's gRPC endpoint:

- ASP.NET app mode: same host/port where `MapMongoProfiler()` is mapped
- worker/batch mode: host/port configured in `AddMongoProfilerBridge(...)`

The viewer subscribes via `Subscribe`.

## 4. Setup notes

- This package currently focuses on the standard in-process relay model (app emits events, viewer subscribes).
- If you need a central aggregator model, host one application that runs the same relay registration and route all producers there through your own transport/pipeline.
- `UseMongoProfiler(...)` is a `MongoClientSettings` extension; call it before `new MongoClient(settings)`.
