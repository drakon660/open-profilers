## Mongo Profiler Enrichment Implementation Plan

### Goal
Expand the current Mongo command subscription pipeline to capture richer telemetry (server, operation metadata, write/read outcomes, errors, query fingerprinting), send it through gRPC, and expose it in the Avalonia viewer.

---

## Current Status (2026-03-25)

- ✅ Event schema marker set to `preview` and core enrichment is implemented end-to-end.
- ✅ Capture includes server/operation metadata, payload sizes, cursor/result info, read/write options, and query fingerprints.
- ✅ Error enrichment includes exception and reply-context extraction where available.
- ✅ Redaction and truncation are implemented and configurable.
- ✅ gRPC contract/mapping updated with optional field presence handling.
- ✅ Avalonia viewer now supports dual-source telemetry:
  - gRPC stream mode
  - direct `system.profile` mode (dev/test)
- ✅ Avalonia UI now includes:
  - separate gRPC and `system.profile` grids with separate models
  - source toggle (`Listen to system.profile`)
  - optional auto-reconnect toggle (default off)
  - improved connection status diagnostics (numeric timestamp + sequence)
- ✅ Direct `system.profile` ingestion includes:
  - self-noise filtering (`system.profile` reads/comments excluded)
  - seen-item deduplication and incremental grid updates
  - 5-second poll interval with multi-batch draining for bursty workloads
- ✅ Sample app includes validation scenarios and safer disconnect behavior.
- ⚠️ Remaining manual validation: collect at least one benchmark result block in a target environment.

---

## 1) Scope and Deliverables

- Extend capture in `MongoClientSettingsExtensions` from basic query/duration to enriched event data.
- Version and extend gRPC contract (`ProfilerEvent`) with optional new fields.
- Update viewer grid/details to display high-value fields and allow grouping/filtering.
- Add redaction + truncation safeguards for sensitive or oversized payloads.
- Add tests/validation scenarios for `find`, `aggregate`, write ops, and failures.

---

## 2) Data Contract (Enriched preview event)

The full list of enriched fields is maintained in `MONGO_PROFILER_FIELD_DICTIONARY.md`.

### 2.2 Compatibility strategy
- Keep existing fields unchanged.
- Add only optional / repeated additions with new field numbers.
- Do not remove or repurpose current field numbers.
- Use non-versioned marker `preview` until first official release while keeping backward consumption logic.

---

## 3) Capture Layer Changes (.NET Core Profiler)

### 3.1 `CommandStartedEvent` extraction
- Parse command document for:
  - read/write options (`readConcern`, `writeConcern`, etc.)
  - performance controls (`maxTimeMS`, `allowDiskUse`)
  - `lsid` (already implemented)
  - command/database/collection (already implemented)
- Compute:
  - `command_size_bytes` via BSON serialization length
  - `query_fingerprint` via normalized query shape (literals replaced by placeholders)

### 3.2 `CommandSucceededEvent` extraction
- Parse reply document for:
  - `cursor.id` / batch counts
  - write result fields (`n`, `nModified`, `upserted`)
  - existing `result_count` logic
- Compute:
  - `reply_size_bytes`
- Capture endpoint/operation correlation where available.

### 3.3 `CommandFailedEvent` extraction
- Capture:
  - error message (already)
  - error code/codeName/labels from failure or reply context
- Keep cancellation/expected disconnect behavior non-fatal.

### 3.4 Redaction and limits
- Redact known sensitive keys (configurable list: `password`, `token`, etc.).
- Truncate large string payloads to safe max length.
- Ensure enrichment failures never break app flow (current try/catch pattern preserved).

---

## 4) gRPC Transport Changes

### 4.1 Proto update
- Add optional fields for enriched preview event.
- Regenerate server/client stubs.

### 4.2 Mapping layer
- Map new core event properties in `ProfilerStreamService`.
- Keep null-safe mapping and avoid default-value noise when absent.

### 4.3 Stream behavior
- Preserve existing cancellation semantics (already fixed).
- Consider adding lightweight server-side filtering in future (phase 2).

---

## 5) Viewer (Avalonia) Changes

### 5.1 Dual-source UI model
- Keep separate tabs/models for:
  - gRPC stream events
  - direct `system.profile` events (dev/test)
- Add source switch and reconnect behavior controls:
  - `Listen to system.profile`
  - `Auto reconnect`

### 5.2 gRPC stream view
- Show grouped query view by fingerprint/query shape.
- Include key metadata in rows/details:
  - command, session, server, result count, duration, status
  - operation id, error metadata, fingerprint

### 5.3 `system.profile` view
- Show profiler-derived fields:
  - app, client, command, docs examined, timestamp, op
- Keep periodic polling + burst draining and self-noise filtering.

---

## 6) Testing and Validation

### 6.1 Functional scenarios

Functional scenarios are documented in `Mongo.Profiler.Samples/VALIDATION_SCENARIOS.md`.

### 6.2 Contract validation
- Confirm no exceptions on missing fields.

### 6.3 Performance checks
- Measure overhead of enrichment and fingerprinting.
- Verify bounded channels still prevent backpressure issues.

---

## 7) Rollout Plan

### Phase 1 (Core value)
- Add server/operation/error/fingerprint fields.
- Show key new columns in viewer.
- Keep current append-only log model.

### Phase 2 (Analysis UX)
- Filters + grouping by fingerprint.
- Slow query highlighting and top-N expensive views.

### Phase 3 (Harden)
- Redaction config + payload caps.
- Export/import session traces.

---

## 8) Risks and Mitigations

- **Risk:** Driver event shape differences by command type  
  **Mitigation:** Command-specific parsing with safe fallbacks.
- **Risk:** Increased event size/traffic  
  **Mitigation:** Optional fields + truncation + future filter controls.
- **Risk:** Sensitive data leakage  
  **Mitigation:** Redaction defaults + explicit allowlist/denylist config.

---

## 9) Definition of Done

- Enriched fields captured in core and visible in viewer.
- No regressions in existing stream/connect/disconnect flows.
- Build passes for core, grpc, sample, and viewer.
- Basic manual scenarios validated for read/write/error cases.
- Documentation updated with field dictionary and redaction behavior.

### Remaining checklist from DoD/plan

- [x] Add enrichment overhead benchmark tooling and persisted report output (`P` mode + `BENCHMARK_RESULTS.md`).
- [x] Add Avalonia reconnect/source switching controls.
- [x] Add direct `system.profile` reader path (dev/test).
- [x] Add direct `system.profile` mode documentation + operational guardrails (`SYSTEM_PROFILE_MODE_GUARDRAILS.md`).

---

## 10) Next Feature Candidates

The backlog of follow-on features (query shape rollups, repeat aggregation, efficiency heuristics, sampled explain, OTel alignment, slow-query policy) lives in `mongo_features.md`.
