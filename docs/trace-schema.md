# Query Trace Schema

Per-query trace records written as JSONL when `RfcRag__TraceDirectory` is configured.
One JSON object per line, one line per query. Files are daily-rotated:

```
{dir}/rfc-rag-trace-{yyyy-MM-dd}.jsonl
```

## Fail-Open Contract

The trace writer never throws. I/O failures (unwritable directory, disk full,
permission denied) produce a logged warning and the query succeeds. When
`TraceDirectory` is not configured, tracing is completely disabled — no file
is opened, no I/O occurs. This makes tracing safe to enable in any deployment.

## Field Definitions

### QueryTrace (top-level)

| Field | Type | Nullable | Description |
|---|---|---|---|
| `traceId` | `string` | no | Unique trace identifier (UUID `D` format) for correlating log lines and diagnostics |
| `question` | `string` | no | The user's question verbatim |
| `timestampUtc` | `string` (ISO 8601) | no | When the trace was created, UTC |
| `stages` | `TraceStage[]` | no (default `[]`) | Timed stages in the pipeline — see below |
| `candidateRfcNumbers` | `number[]` | no (default `[]`) | Distinct RFC numbers of search candidates retrieved for this query |
| `retrieval` | `RetrievalInfo \| null` | yes | Retrieval strategy metadata — see RetrievalInfo in [GeneratedAnswer schema](#) (not populated in initial version) |
| `answerGenerated` | `boolean` | no | Whether an answer was produced (always `true` when the pipeline completes; `false` only if generation is skipped) |
| `warningCount` | `number` | no | Count of warnings produced across generation and verification |

### TraceStage

| Field | Type | Nullable | Description |
|---|---|---|---|
| `name` | `string` | no | Stage name — one of `search`, `assemble`, `generate` |
| `startedAtUtc` | `string` (ISO 8601) | no | Wall-clock start time (UTC) |
| `completedAtUtc` | `string` (ISO 8601) | yes | Wall-clock completion time (UTC); absent for stages that did not complete |
| `duration` | `number` (seconds) | yes | Computed as `completedAtUtc - startedAtUtc`; absent when not completed |

### Stage Semantics

The three pipeline stages are sequential and non-overlapping:

1. **search** — hybrid vector + full-text search. Spans from `searchService.SearchAsync()` start to completion. Duration includes the database query and result materialization.
2. **assemble** — evidence assembly. Spans from `contextAssembler.AssembleAsync()` start to completion. Duration includes section hydration, deduplication, overlap collapse, and budget enforcement.
3. **generate** — LLM answer generation. Spans from `answerGenerator.GenerateAsync()` start to completion. Duration includes the LLM API call and response parsing. Followed by the synchronous citation verification step (not timed separately in the current schema).

Stage timestamps are **System wall-clock**, not `TimeProvider` or `Stopwatch`. This is acceptable for relative stage comparison but not for high-precision measurement against external clocks.

## Example

```json
{
  "traceId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "question": "How does HTTP content negotiation work?",
  "timestampUtc": "2026-06-13T20:00:00.000Z",
  "stages": [
    { "name": "search", "startedAtUtc": "2026-06-13T20:00:00.010Z", "completedAtUtc": "2026-06-13T20:00:00.250Z", "duration": 0.240 },
    { "name": "assemble", "startedAtUtc": "2026-06-13T20:00:00.250Z", "completedAtUtc": "2026-06-13T20:00:00.280Z", "duration": 0.030 },
    { "name": "generate", "startedAtUtc": "2026-06-13T20:00:00.280Z", "completedAtUtc": "2026-06-13T20:00:02.100Z", "duration": 1.820 }
  ],
  "candidateRfcNumbers": [9110, 9111, 9112],
  "answerGenerated": true,
  "warningCount": 1
}
```

## Configuration

| Key | Type | Default | Description |
|---|---|---|---|
| `RfcRag__TraceDirectory` | `string` | *(not set)* | Directory for JSONL trace output. When unset, tracing is disabled entirely. |
