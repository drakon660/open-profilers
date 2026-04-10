# Mongo Profiler Validation Scenarios

Use the sample app interactive keys to quickly validate enrichment behavior.

## Quick Run

1. Ensure MongoDB is running and reachable on `localhost:27017`.
2. Start sample app.
3. Start a viewer:
   - Avalonia:
     - gRPC mode: keep `Listen to system.profile` unchecked and connect to `localhost:5179`.
     - direct mode (dev/test): check `Listen to system.profile`, set Mongo connection + DB, and ensure Mongo profiling is enabled for that DB.
4. Press `V` in sample app to run the full suite.
5. Optional explicit checks:
   - press `P` for enrichment overhead benchmark (appends `BENCHMARK_RESULTS.md`)

If the grid is empty, first confirm Mongo is reachable. No Mongo traffic means no profiler events.

## Individual Scenarios

- `F` - `find` success path, with sort/project/limit.
- `A` - `aggregate` success path.
- `W` - write path (`insert`, `update`, `delete`).
- `E` - failure path using intentionally invalid operator.
- `M` - multi-session read operations.
- `P` - enrichment overhead benchmark (baseline vs enrichment-enabled client).
- Disconnect and reconnect viewer while actions are running to validate stream resilience.

## What To Verify In Viewer

- `COMMAND`, `SESSION`, `SERVER`, `RESULT COUNT`, `DURATION`, `STATUS` (or equivalent) populate.
- Details panel shows:
  - operation metadata (session, operation id, fingerprint)
  - error metadata for failures
  - index advisor status/reason and execution stats for slow queries
- Avalonia:
  - two grids:
    - `gRPC Stream` (query-count/avg-duration grouped view)
    - `system.profile` (fields: app/client/command/docsExamined/ts/op)
  - source switch behavior (`Listen to system.profile`)
  - reconnect behavior controlled by `Auto reconnect` (default off)
  - bottom status bar reports connection/runtime errors

## Notes

- Index advisor runs only when enabled and query duration exceeds configured threshold.
- Redaction and truncation settings are configured in `appsettings.json`.
- Sample app key presses are logged (`Key pressed: ...`) to confirm interactive input is working.
- For direct `system.profile` mode:
  - Profiler level/filter on Mongo controls what can be observed.
  - Polling is periodic; updates may appear in batches.
  - Viewer filters out `system.profile` self-noise entries.
  - Follow `../SYSTEM_PROFILE_MODE_GUARDRAILS.md` for operational safety guidance.
