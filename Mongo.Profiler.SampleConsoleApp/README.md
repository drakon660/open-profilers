# Mongo.Profiler.SampleConsoleApp

Hosted console sample for exercising Mongo profiler coverage through a Spectre.Console menu.

## What It Runs

- relay commands: start/stop/status for the on-demand gRPC relay the Avalonia viewer connects to
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

The relay does not auto-start; open the menu's **Relay** category and run *Start gRPC relay* before connecting the Avalonia viewer to the banner address (usually `localhost:5179`).

Raw driver event JSON dumps are disabled by default. To enable them for diagnostics, set `RawEventLogging` to `true` and configure `RawEventLogsDirectory` in `appsettings.json`. Control characters produced by single-backslash Windows paths in JSON (e.g. `"c:\raw_logs"`) are recovered automatically, so both `"c:\\raw_logs"` and `"c:\raw_logs"` resolve to the same folder.
