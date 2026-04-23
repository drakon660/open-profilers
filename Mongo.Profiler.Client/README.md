# Mongo.Profiler.Client

`Mongo.Profiler.Client` is the app-side integration layer for wiring Mongo query profiling into .NET hosts.

It builds on top of `Mongo.Profiler` and `Mongo.Profiler.Grpc`:

- register an in-process event broadcaster in DI
- optionally host a gRPC relay for desktop viewer subscribers
- apply profiling to `MongoClientSettings`

## Quick model

- `AddMongoProfiler()` registers the in-process broadcaster and `IMongoProfilerEventSink`.
- `AddMongoProfilerBroadcaster(...)` registers a caller-owned broadcaster as both the broadcaster and `IMongoProfilerEventSink`.
- `AddMongoProfilerPublisher(...)` starts a background gRPC relay for worker or console hosts.
- `MongoProfilerRelay.StartAsync(...)` starts the same relay for plain apps that do not use `HostBuilder`.
- `UseMongoProfiler(serviceProvider)` is available from `Mongo.Profiler.Client.AspNet` for ASP.NET-style registrations.
- `UseMongoProfiler(sink, logger)` applies profiling to `MongoClientSettings` with an explicit sink.
- `UseMongoProfiler(logger)` applies profiling for logging-only scenarios.
- `MapMongoProfiler()` is only needed in ASP.NET apps that expose the gRPC subscription endpoint themselves.

## ASP.NET Core app

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

// Required in ASP.NET mode to expose gRPC subscription endpoint.
app.MapMongoProfiler();
```

`builder.AddMongoProfiler(...)` configures the broadcaster and the ASP.NET-hosted gRPC endpoint. It also adds gRPC services and, unless Kestrel endpoints are already explicitly configured, binds the profiler port for HTTP/2.

## Worker, service, or console host

For non-web hosts (for example `Host.CreateApplicationBuilder()` workers), run relay as hosted service:

```csharp
using Mongo.Profiler.Client;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddMongoProfilerPublisher(options =>
{
    options.Port = 5179;
    options.ListenOnAnyIp = false; // true for container/remote access
});
builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var settings = MongoClientSettings.FromConnectionString("mongodb://localhost:27017");
    var sink = serviceProvider.GetRequiredService<IMongoProfilerEventSink>();
    settings = settings.UseMongoProfiler(sink);
    return new MongoClient(settings);
});
```

This starts a lightweight gRPC relay in the background and exposes `Subscribe` for viewer clients.
In this mode, do not call `MapMongoProfiler()`.

Optionally wait until viewer connects before executing workload:

```csharp
await host.Services.WaitForMongoProfilerSubscriberAsync();
```

`Mongo.Profiler.SampleConsoleApp` is the reference generic-host console sample. It uses `AddMongoProfilerPublisher(...)`, starts the relay, and presents a Spectre.Console menu of read, write, aggregate, admin, transaction, change stream, and raw-command scenarios.

## Plain app without `HostBuilder`

If the app does not use `HostBuilder` or ASP.NET hosting, start the relay explicitly and use the returned sink when creating `MongoClientSettings`:

```csharp
await using var relay = await MongoProfilerRelay.StartAsync(options =>
{
    options.Port = 5179;
    options.ListenOnAnyIp = false;
});

await relay.WaitForMongoProfilerSubscriberAsync();

var settings = MongoClientSettings.FromConnectionString(connectionString);
settings = settings.UseMongoProfiler(relay.Sink);

var client = new MongoClient(settings);
```

This gives the app a background gRPC endpoint for the viewer without requiring the rest of the application to adopt a generic host.

### Relay lifetime with DI

Be careful when passing `relay.Sink` into long-lived DI registrations. The relay handle is disposable and should live for at least as long as anything using its streaming endpoint.

This is safe in a plain app when the relay and the Mongo client share the same outer scope:

```csharp
await using var relay = await MongoProfilerRelay.StartAsync(options =>
{
    options.Port = 5179;
    options.ListenOnAnyIp = false;
});

var settings = MongoClientSettings.FromConnectionString(connectionString);
settings = settings.UseMongoProfiler(relay.Sink);

var client = new MongoClient(settings);
```

If you are using a custom DI container, prefer registering a long-lived broadcaster and then start the relay over that same broadcaster:

```csharp
var broadcaster = new MongoProfilerEventChannelBroadcaster();

await using var relay = await MongoProfilerRelay.StartAsync(
    broadcaster,
    options =>
    {
        options.Port = 5179;
        options.ListenOnAnyIp = false;
    });

services.AddMongoProfilerBroadcaster(broadcaster);

services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var settings = MongoClientSettings.FromConnectionString(connectionString);
    settings = settings.UseMongoProfiler(serviceProvider.GetRequiredService<IMongoProfilerEventSink>());
    return new MongoClient(settings);
});
```

This way the container owns the broadcaster it uses, while the relay only hosts the gRPC streaming endpoint on top of it.

## Apply profiling to `MongoClientSettings`

### Resolve sink from DI

```csharp
using Mongo.Profiler.Client.AspNet;
using MongoDB.Driver;

builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var settings = MongoClientSettings.FromConnectionString("mongodb://localhost:27017");
    settings = settings.UseMongoProfiler(serviceProvider);
    return new MongoClient(settings);
});

var client = app.Services.GetRequiredService<IMongoClient>();
```

### Pass the sink explicitly

```csharp
var broadcaster = new MongoProfilerEventChannelBroadcaster();

var settings = MongoClientSettings.FromConnectionString("mongodb://localhost:27017");
settings = settings.UseMongoProfiler(broadcaster);

var client = new MongoClient(settings);
```

### Other DI

#### Terminal or Console, no ILogger

```csharp
var settings = MongoClientSettings.FromConnectionString(connectionString);

settings = settings.UseMongoProfiler();

return new MongoClient(settings);
```

#### ILogger

`s = serviceCollection`

```csharp
s.AddLogging(builder => builder.AddConsole());
```

```csharp
var settings = MongoClientSettings.FromConnectionString(connectionString);

var loggerFactory = factory.GetInstance<ILoggerFactory>();
var logger = loggerFactory.CreateLogger("MongoProfiler");

settings = settings.UseMongoProfiler(logger);

return new MongoClient(settings);
```

This keeps the integration independent of ASP.NET-specific helpers while still publishing profiler events to the shared broadcaster or custom sink registered in your container.

### Logging-only mode

If you only want query logging and do not need the gRPC viewer pipeline:

```csharp
var settings = MongoClientSettings.FromConnectionString("mongodb://localhost:27017");
settings = settings.UseMongoProfiler(logger);

var client = new MongoClient(settings);
```

## Viewer connection

Point the viewer to the host and port that expose the gRPC `Subscribe` stream:

- ASP.NET mode: the same app that calls `app.MapMongoProfiler()`
- worker or console mode: the hosted relay configured by `AddMongoProfilerPublisher(...)`
- plain app mode: the relay returned by `MongoProfilerRelay.StartAsync(...)`

## Notes on sinks and DI

- `AddMongoProfiler()` registers `MongoProfilerEventChannelBroadcaster` as the default `IMongoProfilerEventSink`.
- `AddMongoProfilerBroadcaster(...)` registers a specific broadcaster instance that you already own.
- `AddMongoProfilerPublisher(...)` includes `AddMongoProfiler()` and then hosts the relay.
- `MongoProfilerRelay.StartAsync(...)` is the non-DI, non-hosted equivalent when the app needs viewer streaming but does not already have a host.
- The optional `sink` argument on `AddMongoProfilerPublisher(...)` is intended for supplying an existing broadcaster instance that should be shared with the relay host.

## Event behavior

The underlying instrumentation in `Mongo.Profiler` can:

- log rendered Mongo commands
- publish `MongoProfilerQueryEvent` messages to the configured sink
- include request and reply metadata such as result counts, cursor IDs, payload sizes, and error details
- emit query fingerprints and trace identifiers
- run optional index-advice analysis for slow `find` and `aggregate` operations when configured through the core `MongoProfilerOptions`

When `MongoProfilerOptions.RawEvents.Enabled` is set, the core instrumentation writes best-effort raw driver event JSON files to `MongoProfilerOptions.RawEvents.DestinationDirectory`. If no destination is supplied, it uses the user's local application data directory under `Mongo.Profiler/raw_logs`. Those dumps include every readable public property from each captured driver event, with BSON payloads preserved as relaxed extended JSON, and are intended for diagnostics rather than the gRPC viewer contract.

## Packaging (maintainers)

- Never re-pack an existing version number. NuGet treats package versions as immutable, so once a given version has been restored on any machine, it is cached under `~/.nuget/packages/mongo.profiler.client/<version>/` and future restores short-circuit to that cached copy, even if you overwrite the `.nupkg` with new content. Consumers will then build against stale bits with no obvious diagnostic.
- Bump the version on every pack: run `build-nuget.ps1 -Bump patch` (or `-Version x.y.z`) so consumers pick up changes without anyone having to clear caches.
- If a stale cache is already suspected, delete the offending version folder under `~/.nuget/packages/mongo.profiler.client/` and `dotnet restore --force`.
