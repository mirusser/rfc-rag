# Deterministic-first reranking with named signal weights

After hybrid RRF search, a `DeterministicReranker` reorders the wider candidate set (up to 4× the requested limit) using bounded, named signal weights before the results are returned to callers.

## Signals and weights

| Signal | Constant | Value |
|---|---|---|
| Exact RFC number from Query Plan | `RfcNumberMatchBonus` | +0.12 |
| Exact section reference from Query Plan | `SectionMatchBonus` | +0.10 |
| Successor RFC obsoletes/updates a query-mentioned RFC | `UpdatedByRelevanceBonus` | +0.06 |
| Query term appears in section heading | `HeadingTermMatchBonus` | +0.05 |
| RFC number in protocol seed set from Query Plan | `ProtocolRfcBonus` | +0.04 |
| RFC is obsoleted by another (from batch status lookup) | `ObsoletedRfcPenalty` | −0.10 |

The obsolete penalty is suppressed when the Query Plan detects historical intent (`IncludeObsolete = true`).

## Why deterministic-first

Model-based rerankers (cross-encoders, LLM-as-judge) improve ordering at the cost of latency, API spend, and nondeterminism. The signals above are derived from structured RFC metadata: relation graph (obsoletes/updates/updated-by), Query Plan detections (RFC numbers, section references, protocol hints, historical intent), and heading text. These structural signals are cheap, reproducible, and require no model call.

The reranker is behind a `RerankerEnabled` flag (default `true`) for A/B comparison. The retrieval quality gate (`RetrievalQualityTests`) guards against silent regression: the flag may be set to `false` by default if the gate does not hold on initial measurement.

## Retrieval quality baseline

The reranker was validated against the committed baseline (`docs/eval/reports/baseline-testdata.json`, manifest `384aeece-ae5a-4042-90d0-3dfb00725594`). Quality gate thresholds hold with `RerankerEnabled=true`:

| Metric | Value |
|---|---|
| Hit@1 | 0.667 |
| Hit@5 | 0.917 |
| Hit@10 | 0.917 |
| MRR | 0.762 |
| nDCG@10 | 0.800 |
| Avg latency | 16 ms |

A/B delta (flag on vs. off) not yet measured. Run `make eval` with `RerankerEnabled=false` to produce a comparison.

## Implementation boundaries

- **Fusion stays in SQL.** RRF computation remains in the single PostgreSQL statement (per ADR-0003). The wider candidate set is retrieved with arm ranks exposed; reranking is applied in application code.
- **Pure reranker.** `DeterministicReranker.Rerank` is a static method over in-memory collections. It takes pre-fetched RFC statuses; the async DB round-trip happens in `SearchService`, keeping the scoring logic unit-testable without fakes.
- **Score provenance.** The final `SearchResult.Score` is the base RRF score plus signal contributions, so downstream consumers (Context Assembler, eval harness) see a single comparable float rather than separate fields.
- **Model-based reranking deferred.** Revisit only after deterministic signals are measured against the committed eval baseline and a cross-encoder shows a measurable improvement worth the latency cost.
