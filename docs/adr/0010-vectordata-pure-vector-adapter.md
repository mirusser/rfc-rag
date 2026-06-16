# VectorData is an additive pure-vector adapter

The project may use `Microsoft.Extensions.VectorData` with the Postgres connector as a flag-gated pure-vector retrieval path over the existing `rfc_rag.rfc_sections` table.

This does not supersede ADR-0003 or ADR-0004. Default retrieval stays in hand-written SQL so PostgreSQL full-text search, pgvector similarity, and RRF fusion execute in one database query. The connector path is for A/B measurement and framework compatibility only.

## Consequences

- `RfcRag__VectorDataSearchEnabled` defaults to `false`.
- The connector maps to the existing `rfc_rag.rfc_sections` table and reuses the existing HNSW index.
- Schema remains owned by the checksummed migration runner. The connector must not call `EnsureCollectionExistsAsync`.
- Retrieval-quality gates measure both hybrid and pure-vector behavior, but only the default hybrid path is held to the ADR-0007 baseline.
