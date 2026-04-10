# Mongo Profiler Feature Backlog

## High-Value Features for Developers

### 1) Query Shape Rollups
- Add stable `query_shape_id` based on normalized command shape (literals replaced).
- Aggregate by shape with:
  - `count`
  - `first_seen_utc`
  - `last_seen_utc`
  - `p50_duration_ms`
  - `p95_duration_ms`
  - `error_rate`
- Use this to show "top repeated expensive queries" in the viewer.

### 2) Repeat Aggregation / Noise Control
- Keep first event fully detailed.
- Aggregate repeated identical warnings for a short rolling window.
- Emit repeat metadata:
  - `repeat_count`
  - `repeat_window_start_utc`
  - `repeat_window_end_utc`
- Bound in-memory state to avoid unbounded growth.

### 3) Query Efficiency Heuristics
- Add warnings based on optimization signals:
  - high `docsExamined / nReturned`
  - high `keysExamined / nReturned`
  - collection scan risk (`COLLSCAN`-like patterns where available)
- Surface concise reason codes for grouping and alerting.

### 4) Sampled Explain Enrichment (Async)
- For slow + frequent query shapes only, capture `explain("executionStats")` summary.
- Store compact plan details:
  - winning stage summary
  - `docsExamined`
  - `keysExamined`
  - `nReturned`
- Run asynchronously with strict rate limits.

### 5) OpenTelemetry Alignment
- Enrich spans/events with common DB attributes:
  - `db.system = mongodb`
  - `db.operation.name`
  - `db.collection.name`
  - `db.namespace`
  - `server.address`
- Keep mapping consistent with profiler event schema.

### 6) Slow Query Policy Controls
- Add environment-aware controls:
  - `slowms` threshold
  - sampling rate
  - optional per-command overrides
- Provide safe defaults for local/dev vs production.

## Suggested Implementation Order
1. Query shape rollups
2. Repeat aggregation
3. Efficiency heuristics
4. Sampled explain enrichment
5. OTel alignment
6. Slow-query policy controls
