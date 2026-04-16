# Hosting Reuse Notes

## Context

We compared:

- `Mongo.Profiler.Client/AspNet/MongoProfilerAspNetExtensions.cs`
- `Mongo.Profiler.Client/MongoProfilerRelayHost.cs`

The goal was to understand whether the ASP.NET path could reuse the relay host directly, or whether the logic is fundamentally different.

## Conclusion

The two paths are related, but they do not serve the same hosting role.

- `MongoProfilerAspNetExtensions` augments an already-existing ASP.NET application host.
- `MongoProfilerRelayHost` creates a brand-new dedicated relay host in the background.

Because of that, the ASP.NET path should not be replaced with `MongoProfilerRelayHost` directly.

## What Is Shared

Both paths need to:

- register gRPC-related services
- expose the profiler streaming endpoint
- configure Kestrel to bind ports that support the profiler stream

## What Is Different

### ASP.NET path

The ASP.NET extension:

- works inside an existing `WebApplicationBuilder`
- respects existing `Kestrel:Endpoints` configuration
- reads `ASPNETCORE_URLS` / `urls`
- binds configured app URLs as `Http1AndHttp2`
- adds a dedicated profiler port only when needed

### Relay host path

The relay host:

- creates a separate `WebApplication` with `CreateSlimBuilder()`
- always hosts a dedicated relay endpoint
- uses a separate DI container
- replaces relay-host registrations so the relay and producer app share the same broadcaster instance
- binds the relay port as `Http2`

## Reuse That We Applied

We extracted only the low-risk reusable parts.

### Shared service registration

`MongoProfilerAspNetExtensions` now reuses:

- `AddMongoProfilerGrpc()`

instead of duplicating:

- `AddGrpc()`
- `AddMongoProfilerChannel()`

### Shared Kestrel binding helper

We added:

- `Mongo.Profiler.Client/MongoProfilerKestrelBindings.cs`

This helper contains the reusable low-level binding operations:

- bind a configured URL with a chosen protocol set
- bind the profiler port with `localhost` or `any IP`

This is reused by:

- `MongoProfilerAspNetExtensions`
- `MongoProfilerRelayHost`

## What We Deliberately Did Not Merge

We did not try to fully unify the two code paths because the hosting behavior is intentionally different.

Specifically, the ASP.NET logic still needs to remain responsible for:

- existing endpoint detection
- `ASPNETCORE_URLS` parsing
- mixed `Http1AndHttp2` behavior for normal app endpoints
- deciding whether a dedicated profiler port still needs to be added

Those decisions do not belong in the dedicated relay host.

## API Direction Notes

Current public API shape being discussed:

- `builder.AddMongoProfiler(...)` for ASP.NET apps
- `services.AddMongoProfilerPublisher(...)` for generic-host apps
- `MongoProfilerRelay.StartAsync(...)` for plain apps without a host
- `services.AddMongoProfilerBroadcaster(broadcaster)` for caller-owned broadcaster registration

There is also an `IServiceCollection.AddMongoProfiler()` helper currently used only internally by `AddMongoProfilerPublisher(...)`.

Because the package is not published yet, making that helper non-public is a reasonable cleanup if we want to reduce API surface.
