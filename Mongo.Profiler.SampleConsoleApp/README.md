# Mongo.Profiler.SampleConsoleApp

Hosted console sample for exercising Mongo profiler coverage through a Spectre.Console menu.

## What It Runs

- setup commands: seed data, create/drop collection, create/list/drop indexes
- read commands: find, projection, findOneAndUpdate, count, estimated count, distinct
- write commands: insert, replace, update, delete, bulk write
- aggregate commands: group summary, lookup, explain aggregate
- advanced commands: transaction, change stream, GridFS-style metadata query
- admin commands: ping, buildInfo, serverStatus, connectionStatus, list databases/collections, dbStats, collStats, currentOp
- raw command JSON for running any Mongo command against the sample database

## Layout

- `Program.cs`: startup only
- `Infrastructure/`: host, relay, profiler-enabled Mongo client setup
- `ConsoleUi/`: Spectre.Console menu loop and result rendering
- `Commands/`: command catalog and operation handlers split by category
- `Data/`: sample documents and collection helpers
- `Models/`: options, context, command, and result records

## Run

Configure `appsettings.json` if needed, then run:

```powershell
dotnet run --project .\Mongo.Profiler.SampleConsoleApp\Mongo.Profiler.SampleConsoleApp.csproj
```

Start the viewer and connect it to the relay shown in the console banner, usually `localhost:5179`.

Raw driver event JSON dumps are disabled by default. To enable them for diagnostics, set `FeatureManagement:RawEventLogging` to `true` and configure `RawEventLogsDirectory` in `appsettings.json`.
