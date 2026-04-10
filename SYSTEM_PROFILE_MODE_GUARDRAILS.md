# Direct `system.profile` Mode Guardrails

This document defines safe usage and operational limits for the Avalonia direct `system.profile` mode.

## Intended use

- Use this mode in development, test, and troubleshooting environments.
- Prefer gRPC stream mode for normal app profiling workflows.
- Avoid enabling direct profiler reads against shared production clusters unless explicitly approved.

## Prerequisites

- MongoDB instance reachable from viewer machine.
- MongoDB profiling enabled for the selected database.
- User account requires read access to `system.profile`.
- Viewer configured with:
  - `Listen to system.profile` enabled
  - valid Mongo connection string
  - target database name

## Mongo profiling settings

Use low-impact settings and narrow filters whenever possible.

Example (run in Mongo shell for target database):

```javascript
db.setProfilingLevel(1, {
  slowms: 100,
  sampleRate: 1
})
```

Optional filter example (capture only namespace of interest):

```javascript
db.setProfilingLevel(1, {
  filter: { ns: /^profiler_samples\.orders$/ }
})
```

## Operational guardrails

- Keep profiling scope narrow (database/collection/filter based).
- Prefer short validation windows, then disable profiling.
- If event volume spikes, increase `slowms` or tighten filter criteria.
- Expect batched updates due to poll-based ingestion (5-second cadence).
- Self-noise filtering is applied, but verify output sanity when changing profiler filters.

## Version and behavior notes

- Direct mode reads `system.profile` documents and maps them to viewer-only fields.
- These fields are not part of the gRPC `ProfilerEvent` contract.
- Timestamp ordering can appear non-linear during burst drains; this is expected for batched polling.

## Security and privacy

- Profiling can include command payload fragments.
- Use redaction settings in the profiled app and avoid sensitive test data.
- Limit access to environments where `system.profile` is enabled.

## Exit procedure

Disable profiling after troubleshooting is complete:

```javascript
db.setProfilingLevel(0)
```

Record:

- start/end time of profiling window
- database and filter used
- reason for session
- where captured traces were stored
