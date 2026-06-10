# Changelog

All notable changes to RFC RAG will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- Initial standalone extraction from the Kubernetes MCP Guard monorepo.
- Ten MCP tools: `search_rfc`, `get_rfc`, `get_rfc_section`, `get_rfc_toc`, `search_normative`, `search_abnf`, `find_updates_obsoletes`, `rfc_stats`, `get_rfc_metadata`, `list_indexed_rfcs`.
- Hybrid search combining pgvector cosine similarity and PostgreSQL full-text search with reciprocal rank fusion.
- Incremental SHA256-based indexing (subsequent starts complete in seconds).
- Docker Compose setup with pgvector PostgreSQL sidecar.
- Standalone Dockerfile for containerized stdio MCP server.
- Grammar-style detection: `grammarStyle` field classifies RFCs as `abnf`, `tls-presentation-lang`, `cddl`, `asn.1`, or `none`.
- `get_rfc_section` supports `depth` (child-section expansion) and `expand` (type-reference resolution).

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
