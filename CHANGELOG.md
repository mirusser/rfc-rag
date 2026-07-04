# Changelog

All notable changes to RFC RAG will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [1.1.0] - 2026-07-04

### Changed

- Exited the experimental phase — `1.x` releases are now published as full, stable GitHub Releases (no longer marked pre-release).

### Fixed

- Docker build now copies `Directory.Packages.props` into the build stage so `dotnet restore` resolves package versions under Central Package Management. The Docker smoke test was failing with `NU1015` after the move to centrally-managed package versions.

### Added

- Operational scripts: database backup/restore and an automated dependency-update script.

## [1.0.1] - 2026-06-16

### Added

- Microsoft.Extensions.VectorData pure-vector Postgres adapter (ADR-0010): an additive, flag-gated (`RfcRag__VectorDataSearchEnabled`) pure-vector retrieval path for A/B evaluation. The default search path remains hybrid SQL. See `docs/known_quirks.md` §4.

## [1.0.0] - 2026-06-15

### Added

- Initial standalone extraction from the Kubernetes MCP Guard monorepo.
- Search/retrieval MCP tools: `search_rfc`, `get_rfc`, `get_rfc_full`, `get_rfc_section`, `get_rfc_toc`, `search_normative`, `search_abnf`, `find_updates_obsoletes`, `rfc_stats`, `get_rfc_metadata`, `list_indexed_rfcs`.
- Hybrid search combining pgvector cosine similarity and PostgreSQL full-text search with reciprocal rank fusion.
- Incremental SHA256-based indexing (subsequent starts complete in seconds).
- Docker Compose setup with pgvector PostgreSQL sidecar.
- Standalone Dockerfile for containerized stdio MCP server.
- Grammar-style detection: `grammarStyle` field classifies RFCs as `abnf`, `tls-presentation-lang`, `cddl`, `asn.1`, or `none`.
- `get_rfc_section` supports `depth` (child-section expansion) and `expand` (type-reference resolution).
- Citation verifier with deterministic claim segmentation and support checking.
- `ClaimSupportRate` golden evaluation metric for answer citation quality.
- ADRs 0006 (optional answering), 0008 (golden eval gates), 0009 (errata snapshot).
- **Answering pipeline**: `ask_rfc` tool — natural-language Q&A over RFCs with hybrid search, evidence assembly, and LLM-generated cited answers.
- **Errata enrichment**: `include_errata` and `errata_status` parameters on `ask_rfc`. Loads RFC Editor errata from a local JSON snapshot; matching errata produce evidence and answer warnings.
- **Question analysis**: deterministic `QuestionAnalyzer` extracts RFC numbers, section references, protocol seeds, normative-intent filters, and ABNF/grammar intent from user queries — no LLM dependency.
- **Answer evaluation**: `AnswerEvaluationMetrics` with citation precision, citation recall, citation F1, quote faithfulness, obsolete citation rate, and no-answer accuracy.
- **Query trace channel**: fire-and-forget `QueryTrace` writer replaced with `Channel<QueryTrace>` background consumer — trace I/O failures no longer block query response.
- **Hostile injection test fixture**: `HostileModelFixture` validates that the answer pipeline rejects prompt injection, hallucination seeding, and structural manipulation in user questions.

### Changed

- **Parser determinism**: `RfcParserType.Text` processes only `.txt` files. `RfcParserType.Xml` now prefers `.txt` and uses `.xml` only for RFC numbers that have no `.txt` counterpart — no more double-indexing. **Users who ran `Xml` mode before should force a re-index once** to settle source attribution.
- **Normative filtering**: keyword filtering (when passing `normative_keyword` to `search_rfc`) is now applied inside the hybrid-search SQL rather than as an in-memory post-filter with overscan. `search_rfc` returns exactly up to `limit` matching sections regardless of how far down the ranking the filtered candidates fall.
- **Embedding pipeline**: retry logic moved out of `EmbeddingService` into a dedicated `EmbeddingRetryPolicy` module with per-status classification (429/5xx retryable, 4xx fatal), `Retry-After` header honoring, full-jitter exponential backoff, and `TimeProvider`-based testability. OpenAI SDK's built-in pipeline retry is disabled so retry ownership is singular.
- **Observability**: `EmbeddingService` emits `RfcRag.Embeddings` metrics (`embedding.batches` and `embedding.retries` counters) via `System.Diagnostics.Metrics`. An OTLP exporter is registered when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (standard `OTEL_*` env vars; no custom config keys).
- **Config validation**: startup validates all `RfcRag` options via `IValidateOptions` + `ValidateOnStart()` — aggregated failure on boot instead of late runtime errors.

### Fixed

- `RfcXmlParser` sections now get unique, non-empty `Id` values (was `Guid.Empty` for all XML-parsed sections).
- `RfcSourceResolver` eliminates the race where both `.txt` and `.xml` versions of the same RFC could be indexed, causing re-index churn.
- `IndexSingleAsync` now uses `RfcSourceResolver` so XML fallback applies to single-RFC indexing (previously hardcoded `.txt`).
- `RfcParser` header extraction: trimmed field-name matching (handles whitespace-padded metadata headers), extended `ExtractIntArray` to parse wrapped continuation lines and leading-digits formats (e.g., `4234 Author Name` in pre-2000 RFC headers).
- `RfcParser` normative keyword extraction: removed `RegexOptions.IgnoreCase` — only UPPERCASE keywords have normative meaning per RFC 8174.

### Added (regression corpus)

- Characterization tests locking down parser behavior for historically awkward RFCs: rfc793 (old-format section boundaries), rfc822 (appendix numbering), rfc5234 (ABNF core rules, `=/` incremental rules, rule-name dedup), rfc8174 (uppercase-only keyword extraction, `NOT RECOMMENDED` dedup), rfc9293 (wrapped multi-line `Obsoletes:`), rfc9110 (Appendix A extracted as section with attributed ABNF blocks).
