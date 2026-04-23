# Mongo Profiler Field Dictionary (preview)

This dictionary documents the enriched telemetry fields emitted by the profiler.

## Implementation coverage

- Core capture: implemented in `Mongo.Profiler`.
- gRPC transport: implemented in `Mongo.Profiler.Grpc`.
- Viewer: Avalonia viewer with:
  - gRPC stream grid (grouped query view)
  - direct `system.profile` grid (dev/test)
  - source and reconnect controls with status diagnostics
- Sample validation harness: available in `Mongo.Profiler.Samples`.

## Core identifiers

- `schema_version`: event schema marker (`preview`).
- `event_id`: unique event id per captured command completion.
- `unix_time_ms`: UTC event timestamp in epoch milliseconds.
- `command_name`: Mongo command (`find`, `aggregate`, `insert`, etc.).
- `database_name`: database targeted by command.
- `collection_name`: collection targeted by command (when applicable).
- `request_id`: Mongo driver request id.
- `operation_id`: Mongo driver operation id if available.
- `session_id`: serialized `lsid` for session correlation.
- `server_endpoint`: Mongo server endpoint (`host:port`) handling the command.

## Query + duration

- `query`: sanitized shell-like query text for the viewer.
- `duration_ms`: command duration.
- `success`: true when command succeeded.
- `error_message`: error text on failure.
- `result_count`: inferred result size (`firstBatch`, `nextBatch`, or `n`).
- `query_fingerprint`: hash of normalized query shape.

## Read/write execution metadata

- `read_preference`: serialized read preference if provided.
- `read_concern`: serialized read concern if provided.
- `write_concern`: serialized write concern if provided.
- `max_time_ms`: command `maxTimeMS` when provided.
- `allow_disk_use`: command `allowDiskUse` when provided.
- `command_size_bytes`: BSON command payload size.
- `reply_size_bytes`: BSON reply payload size.
- `cursor_id`: cursor id from reply where applicable.

## Error metadata

- `error_code`: numeric error code (exception or reply context).
- `error_code_name`: error code name.
- `error_labels`: Mongo error labels from exception and/or reply context.

## Index advisor metadata

- `index_advice_status`: one of:
  - `ok`
  - `possible_missing_index`
  - `analysis_timeout`
  - `analysis_failed`
- `index_advice_reason`: concise reason for status.
- `winning_plan_summary`: execution plan hint (`IXSCAN`, `COLLSCAN`, `UNKNOWN`).
- `explain_docs_examined`: `executionStats.totalDocsExamined` when available.
- `explain_keys_examined`: `executionStats.totalKeysExamined` when available.
- `explain_n_returned`: `executionStats.nReturned` when available.

## Redaction behavior

The emitted `query` string is sanitized before publishing.

- Keys in `RedactionSensitiveKeys` are replaced with `***REDACTED***`.
- String values longer than `RedactionMaxStringLength` are truncated with `...[truncated]`.
- Redaction settings are configured through `MongoProfilerOptions.Redaction` (sample values in `Mongo.Profiler.Samples/appsettings.json`).

## Raw driver event dumps

When `MongoProfilerOptions.RawEvents.Enabled` is set, `MongoRawEventLogger` writes best-effort raw JSON diagnostics to `MongoProfilerOptions.RawEvents.DestinationDirectory`. If no destination is supplied, it uses the user's local application data directory under `Mongo.Profiler/raw_logs`.

These files are not part of the published gRPC `ProfilerEvent` schema. They are intended for local troubleshooting and include:

- event type and dump timestamp
- every readable public property exposed by the MongoDB driver event object
- BSON command/reply payloads as relaxed extended JSON
- expanded exception data for failed command, heartbeat, and connection events
- normalized convenience fields such as `ServerEndpoint`, `DurationMs`, and stringified connection id values

Raw dump failures are swallowed so diagnostics never affect application execution.

## Viewer-only direct `system.profile` fields (Avalonia)

These fields are derived from Mongo profiler documents and shown in the `system.profile` grid model.

- `app_name`: `system.profile.appName` when available.
- `client`: `system.profile.client`.
- `command`: serialized `system.profile.command` (shell JSON).
- `docs_examined`: `system.profile.docsExamined`.
- `ts`: epoch-milliseconds derived from `system.profile.ts`.
- `op`: `system.profile.op`.

Notes:
- These fields are viewer-side and are not part of the gRPC `ProfilerEvent` contract.
- The direct mode is intended for lower environments and applies filtering to avoid showing reader self-noise from `system.profile`.
- Operational guidance is documented in `SYSTEM_PROFILE_MODE_GUARDRAILS.md`.
