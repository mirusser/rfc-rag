# Plan: Adopt `Microsoft.Extensions.VectorData` (Postgres) as an Additive Pure-Vector Retrieval Path

> **Status:** Revised 2026-06-16 after validating the original plan against the codebase and the
> current upstream packages. The original plan's central premise (replace the hand-rolled hybrid
> search with the connector's `HybridSearchAsync`) is **not achievable** with the current connector.
> Scope is therefore re-set to an **additive, flag-gated pure-vector path** that adopts the standard
> abstraction with **zero regression** to the existing hybrid pipeline. See §2 for the corrections.

## 1. Overview

Introduce the standard .NET vector abstractions — `VectorStore` / `VectorStoreCollection<TKey,TRecord>`
from `Microsoft.Extensions.VectorData`, backed by the `Microsoft.SemanticKernel.Connectors.PgVector`
provider — as a **new, optional, pure-vector retrieval path** that runs against the **existing**
`rfc_rag.rfc_sections` table. The hand-rolled hybrid search (lexical + vector + RRF in one SQL
statement) and the `DeterministicReranker` remain the **default hot path, untouched**.

This delivers the stated goal ("start using the standard .NET VectorStore") in the only form the
current connector supports, while preserving the project's retrieval quality, its single-statement
RRF fusion (ADR-0003), and its raw-SQL house style (ADR-0004). The new path is gated behind a config
flag (default **off**) and measured against the existing eval gates so adoption is provable and safe.

**Non-goal:** replacing the hybrid pipeline. The Postgres connector cannot do full-text or hybrid
search (verified below), so a wholesale replacement would force application-side rank fusion — a
regression ADR-0003 explicitly rejected. That option was considered and declined.

## 2. Research corrections (this section supersedes the original §2/§5)

The original plan was written against an assumed API surface. Verified against the codebase
(commit on `master`, 2026-06-16) and current Microsoft Learn / NuGet:

| # | Original claim | Verified reality | Source |
|---|---|---|---|
| 1 | Connector implements `IKeywordHybridSearchable` / `HybridSearchAsync` (RRF k=60, `plainto_tsquery`, GIN auto-index) | **Postgres connector: `HybridSearch supported? → No`, `IsFullTextIndexed supported? → No`.** Only HNSW on the vector column is auto-created. No FTS, no RRF, no hybrid. | [MS Learn – Postgres connector](https://learn.microsoft.com/semantic-kernel/concepts/vector-store-connectors/out-of-the-box-connectors/postgres-connector) |
| 2 | Package `Microsoft.Extensions.VectorData.Postgres` | **Does not exist.** Provider is `Microsoft.SemanticKernel.Connectors.PgVector`, latest **`1.74.0-preview`** (2026-03-20). Abstractions: `Microsoft.Extensions.VectorData.Abstractions` (connector needs ≥ 10.1.0; 10.7.0 available). | [NuGet](https://www.nuget.org/packages/Microsoft.SemanticKernel.Connectors.PgVector) |
| 3 | Record key is `ulong Id` | Table key is **`uuid` → `System.Guid`** (`id uuid primary key default gen_random_uuid()`). Postgres connector supports `Guid` keys. | `Migrations/0001-initial-rfc-rag-schema.sql:14` |
| 4 | Attributes `[VectorStoreRecord]`, `[VectorStoreRecordKey/Data/Vector]`, `FullTextSearchIndexed = true` | Current (v10) attributes are `[VectorStoreKey]`, `[VectorStoreData]`, `[VectorStoreVector]` (no class-level `[VectorStoreRecord]`); full-text flag is `IsFullTextIndexed` — and is **unsupported on Postgres**. Column mapping uses `StorageName`. | MS Learn (same page) |
| 5 | Public API is `RfcSearcher` | **No such class.** Public surface is `ISearchService` / `SearchService.SearchAsync(...)`. | `src/RfcRag/Search/ISearchService.cs:5` |
| 6 | FTS uses custom `ts_rank('{0,0,0,0}', search, query)` | Actual SQL uses **plain `ts_rank(search_vector, plainto_tsquery('english', @Query))`** (default weights), inside `lexical`/`vector` CTEs fused by `1.0/(60+rank)`. | `src/RfcRag/Search/SearchRepository.cs` (`SearchHybridAsync`) |
| 7 | (implied) pure-vector path is in use | `SearchRepository.SearchVectorAsync` exists but has **no callers** (not exposed on `ISearchService`). The new connector path is the first real consumer of pure-vector search. | codegraph: 0 callers |
| 8 | Need to wire `UseVector` and a data source | `AddRfcRagServices` **already** builds `NpgsqlDataSource` with `dataSourceBuilder.UseVector()`. The connector's no-arg `AddPostgresVectorStore()` can reuse it. | `src/RfcRag/Infrastructure/ServiceCollectionExtensions.cs:24` |
| 9 | "Adapt the reranker to consume `VectorSearchResult`" | The reranker's base is `candidate.RrfScore` (the **fused** score). A pure-vector result has no lexical arm and no RRF score, so the reranker is **not** fed by this path — and does not need to be, because the path is additive. | `src/RfcRag/Search/DeterministicReranker.cs:78` |

**Correctly stated in the original plan (retained):** embedding dimension `vector(1536)`; RRF constant
`60`; similarity convention `1/(1+distance)`; class names `PostgresVectorStore` /
`PostgresCollection<TKey,TRecord>`; `VectorStore` is a base class (not `IVectorStore`); ADR-0003 and
ADR-0004 are in genuine tension with any SQL-replacing migration; the generated `tsvector` column is
the central data-model alignment hazard; reranker / query-planner / normative / ABNF / schema-migration
layers cannot be migrated.

## 3. Architecture decision

**Additive path — the connector is a second, optional reader of the same table.**

```
                         ┌───────────────────────────────────────────┐
                         │  ISearchService.SearchAsync (public API)   │
                         └───────────────────────────────────────────┘
                                          │ provider switch (flag, default = Hybrid)
                 ┌────────────────────────┴───────────────────────────┐
   default ▼ Hybrid (unchanged)                          opt-in ▼ VectorData pure-vector (NEW)
 ┌───────────────────────────────────┐            ┌──────────────────────────────────────────┐
 │ SearchRepository (Dapper raw SQL) │            │ VectorDataSearch                          │
 │  lexical CTE + vector CTE + RRF    │            │  PostgresCollection<Guid, RfcSectionRecord>│
 │  → DeterministicReranker           │            │  .SearchAsync(queryVector, top)            │
 └───────────────────────────────────┘            └──────────────────────────────────────────┘
                 │                                                    │
                 └──────────────► same NpgsqlDataSource (UseVector) ◄─┘
                                  same rfc_rag.rfc_sections table
                                  same HNSW index (vector_cosine_ops)
```

Key decisions and their rationale:

- **Existing-table mode; never call `EnsureCollectionExistsAsync`.** Schema is owned by the checksummed
  migration runner (`schema_migrations`). The connector is read-only against a table it did not create.
  The record's `[VectorStoreVector]` must use `DistanceFunction.CosineDistance` + `IndexKind.Hnsw` so it
  reuses the existing `ix_rfc_sections_embedding_hnsw` (`vector_cosine_ops`) index rather than expecting a
  new one.
- **Map to the real columns via `StorageName`** (snake_case) and set the collection **schema to `rfc_rag`**.
  The generated `search_vector` tsvector column is simply not represented in the record (the connector
  selects only mapped columns), so it is left intact for the hybrid path.
- **Reuse `EmbeddingService` to embed the query**, then pass the `ReadOnlyMemory<float>` to
  `SearchAsync`. We do **not** rely on connector auto-embedding — that would bypass the existing
  retry / throttle / zero-vector-fallback / dimension-validation logic.
- **Flag default off.** The hybrid path stays the production default; the flag exists for A/B and eval.
- **Governance is light:** ADR-0003 and ADR-0004 are **amended/clarified**, not superseded, because
  fusion stays in SQL and Dapper remains the engine for hybrid search and all writes.

## 4. Data model (corrected)

```csharp
using Microsoft.Extensions.VectorData;

internal sealed class RfcSectionRecord
{
    [VectorStoreKey(StorageName = "id")]
    public Guid Id { get; init; }

    [VectorStoreData(StorageName = "rfc_number")]
    public int RfcNumber { get; init; }

    [VectorStoreData(StorageName = "title")]
    public string Title { get; init; } = "";

    [VectorStoreData(StorageName = "section")]
    public string Section { get; init; } = "";

    [VectorStoreData(StorageName = "heading")]
    public string? Heading { get; init; }

    [VectorStoreData(StorageName = "text")]
    public string Text { get; init; } = "";

    [VectorStoreData(StorageName = "source_path")]
    public string SourcePath { get; init; } = "";

    [VectorStoreData(StorageName = "url")]
    public string Url { get; init; } = "";

    // Reuses the existing HNSW (vector_cosine_ops) index — do NOT mark IsFullTextIndexed (unsupported on PG).
    [VectorStoreVector(Dimensions: 1536, DistanceFunction = DistanceFunction.CosineDistance, IndexKind = IndexKind.Hnsw, StorageName = "embedding")]
    public ReadOnlyMemory<float>? Embedding { get; init; }
}
```

Notes: `search_vector` (tsvector) and `source_sha256` are intentionally **absent** — the connector only
touches mapped columns. Collection is constructed against schema `rfc_rag`, table `rfc_sections`.

## 5. Task breakdown

### Phase 1 — Foundation: prove the connector reads the existing table

#### Task 1: Add the connector NuGet package
**Description:** Add `Microsoft.SemanticKernel.Connectors.PgVector` (pin `1.74.0-preview`) to
`src/RfcRag/RfcRag.csproj`. It transitively brings `Microsoft.Extensions.VectorData.Abstractions`;
pin the abstractions explicitly only if a version conflict surfaces. Confirm Npgsql (10.0.3) and
Pgvector (0.3.2) already satisfy the connector's minimums (≥ 8.0.7 / ≥ 0.3.2).
**Acceptance criteria:**
- [ ] Package referenced; `dotnet restore` resolves with no downgrade warnings.
- [ ] `dotnet build` clean under `TreatWarningsAsErrors=true`.
**Verification:** `dotnet build RfcRag.slnx -c Release`
**Dependencies:** None · **Files:** `src/RfcRag/RfcRag.csproj` · **Scope:** XS

#### Task 2: Define `RfcSectionRecord`
**Description:** Add the record from §4 mapping to `rfc_rag.rfc_sections` via `StorageName`, `Guid` key,
`CosineDistance` + `Hnsw` vector.
**Acceptance criteria:**
- [ ] All eight data columns + key + vector are mapped with correct `StorageName`.
- [ ] No `IsFullTextIndexed` / no class-level attribute; compiles.
**Verification:** `dotnet build`
**Dependencies:** 1 · **Files:** `src/RfcRag/Search/RfcSectionRecord.cs` · **Scope:** S

#### Task 3: Register `PostgresVectorStore` reusing the existing data source
**Description:** In the search DI registration (`AddRfcRagSearch`), register the vector store with the
no-arg `AddPostgresVectorStore()` so it reuses the already-registered `NpgsqlDataSource` (which already
calls `UseVector()`). Resolve the collection with schema `rfc_rag` / table `rfc_sections`. Do **not**
register any collection-creation/hosted-init that calls `EnsureCollectionExistsAsync`.
**Acceptance criteria:**
- [ ] `VectorStore` (and a `PostgresCollection<Guid, RfcSectionRecord>` for `rfc_rag.rfc_sections`) resolve from DI.
- [ ] No DDL is issued at startup (no `CREATE TABLE`/`CREATE INDEX`).
**Verification:** unit test asserting DI resolves the store; manual log/trace check that startup issues no DDL.
**Dependencies:** 2 · **Files:** search DI extension (`AddRfcRagSearch`), `src/RfcRag/Infrastructure/ServiceCollectionExtensions.cs` · **Scope:** S

#### Task 4: Smoke test against the real table
**Description:** Add an Integration-trait test that resolves the collection and round-trips one known
record (`GetAsync(id)`), asserting `StorageName` mapping is correct (key, `rfc_number`, `embedding`
non-null length 1536).
**Acceptance criteria:**
- [ ] Test reads an existing section by id and maps all fields without exception.
- [ ] Tagged `[Trait("Category", "Integration")]` so it is excluded from the unit lane.
**Verification:** `dotnet test --filter "Category=Integration&FullyQualifiedName~VectorData"` (against a seeded DB)
**Dependencies:** 3 · **Files:** `tests/RfcRag.Tests/IntegrationTests/VectorDataCollectionTests.cs` · **Scope:** S

### ✅ Checkpoint A (after Tasks 1–4)
- [ ] Builds clean; unit lane green; the connector reads the existing table with correct mapping and **zero** DDL. Review before Phase 2.

### Phase 2 — The pure-vector retrieval path

#### Task 5: Implement `VectorDataSearch`
**Description:** New service: embed the query via the existing `EmbeddingService`, call
`collection.SearchAsync(queryVector, top, options, ct)`, and project `VectorSearchResult<RfcSectionRecord>`
→ the existing `SearchResult` positional record (`Id, RfcNumber, Title, Section, Heading, Text, SourcePath, Url, Score`).
**Acceptance criteria:**
- [ ] Returns `IReadOnlyList<SearchResult>` ordered by similarity, honoring `limit`.
- [ ] Reuses `EmbeddingService.GenerateEmbeddingsAsync` (no connector auto-embedding).
**Verification:** unit test with a fake/seeded collection; Integration test for ranking sanity.
**Dependencies:** 4 · **Files:** `src/RfcRag/Search/VectorDataSearch.cs` (+ test) · **Scope:** M

#### Task 6: Score normalization
**Description:** `SearchAsync` with `CosineDistance` returns distance (lower = closer). Convert to the
project convention `1/(1+distance)` so `SearchResult.Score` stays comparable to the rest of the system.
**Acceptance criteria:**
- [ ] Mapped scores are in `(0,1]`, higher = better; unit test pins the formula on a known distance.
**Verification:** `dotnet test --filter "Category!=Integration&FullyQualifiedName~VectorData"`
**Dependencies:** 5 · **Files:** `src/RfcRag/Search/VectorDataSearch.cs` (+ test) · **Scope:** XS

#### Task 7: Feature flag + provider switch
**Description:** Add a flag to `RfcRagOptions` (e.g. `VectorDataSearchEnabled`, default `false`),
following the existing `RerankerEnabled` / `QueryPlannerEnabled` pattern; extend `RfcRagOptionsValidator`
if needed. Branch in `SearchService.SearchAsync` so that when the flag is on, retrieval uses
`VectorDataSearch` (pure vector); when off, the current hybrid+reranker path runs **unchanged**. Status
enrichment / section-reference merge still apply to the result set.
**Acceptance criteria:**
- [ ] Flag off → byte-for-byte current behavior (default).
- [ ] Flag on → results come from the connector path; query-plan status enrichment still applied.
- [ ] Validator covers the new option.
**Verification:** unit tests for both branches; `dotnet test --filter "Category!=Integration"`
**Dependencies:** 6 · **Files:** `src/RfcRag/Settings/RfcRagOptions.cs`, `RfcRagOptionsValidator.cs`, `src/RfcRag/Search/SearchService.cs` (+ tests) · **Scope:** S

### ✅ Checkpoint B (after Tasks 5–7)
- [ ] Both retrieval modes selectable by config; default path unchanged; unit lane green. Review before Phase 3.

### Phase 3 — Validation & governance

#### Task 8: A/B retrieval-quality measurement
**Description:** Run the retrieval-quality gate and golden eval for **both** modes and record the deltas
against the ADR-0007 baseline (Hit@1 0.667 / Hit@5 0.917 / Hit@10 0.917 / MRR 0.762 / nDCG@10 0.800).
Expectation: pure vector underperforms hybrid — acceptable; the goal is adoption + a measured baseline,
not beating hybrid.
**Acceptance criteria:**
- [ ] `Category=RetrievalQuality` passes with the flag **off** (no regression to default).
- [ ] A short results table (hybrid vs vector-only) is recorded in the eval reports / this plan.
**Verification:** `dotnet test --filter "Category=RetrievalQuality"`; `make eval` for each mode.
**Dependencies:** 7 · **Files:** `docs/eval/reports/*`, this plan · **Scope:** M

#### Task 9: ADR amendments
**Description:** **Amend** ADR-0003 (clarify: hybrid fusion stays in SQL; the connector is an additive
pure-vector reader over the same table — no second store, no app-side fusion) and ADR-0004 (clarify:
Dapper remains for hybrid search and **all** writes; the connector provides only read-only vector access
behind a flag). Add a brief ADR recording the foothold decision and the upstream hybrid gap as the
reason scope is limited. Do **not** mark ADR-0004 superseded.
**Acceptance criteria:**
- [ ] Both ADRs updated; new ADR added; cross-links correct.
**Verification:** doc review.
**Dependencies:** 8 · **Files:** `docs/adr/0003-*`, `docs/adr/0004-*`, `docs/adr/0010-vectordata-pure-vector-path.md` · **Scope:** S

#### Task 10: Docs
**Description:** Document the flag in `docs/configuration.md`, add a `docs/known_quirks.md` note that the
Postgres connector has no hybrid/FTS (so it cannot replace the hybrid pipeline), and mention the optional
mode in `README.md`.
**Acceptance criteria:**
- [ ] Flag, default, and limitation documented in all three places.
**Verification:** doc review; optional `/verify-readme-docs`.
**Dependencies:** 9 · **Files:** `docs/configuration.md`, `docs/known_quirks.md`, `README.md` · **Scope:** S

### ✅ Checkpoint C (complete)
- [ ] Connector pure-vector path shipped behind a default-off flag; default hybrid path unchanged and gate-green; A/B measured; ADRs/docs updated. Ready for review.

## 6. Risks and mitigations (corrected)

| Risk | Impact | Mitigation |
|---|---|---|
| Connector cannot do hybrid/FTS | — | **Already mitigated by scope:** hybrid stays in SQL; the connector is additive pure-vector only. |
| Accidental `EnsureCollectionExistsAsync` issues DDL against a migration-owned table | High | Never call it; assert "no DDL at startup" in Task 3/4; construct collection directly in existing-table mode. |
| Schema/column mismatch (default `public` schema, camelCase columns) | Med | Set collection schema `rfc_rag`; map every column with `StorageName`; round-trip test (Task 4). |
| Vector property distance ≠ index opclass (won't use HNSW) | Med | Use `DistanceFunction.CosineDistance` to match `vector_cosine_ops`; confirm via `EXPLAIN` in Task 8. |
| Preview API churn (`1.74.0-preview`) | Low | Pin exact version; the path is opt-in and isolated to `VectorDataSearch` + the record. |
| Score-scale confusion vs hybrid | Low | Normalize to `1/(1+distance)` (Task 6). |
| Bypassing `EmbeddingService` resilience via auto-embed | Med | Do not use connector auto-embedding; embed via `EmbeddingService` and pass the vector. |

## 7. Success criteria (corrected)

1. `dotnet build RfcRag.slnx -c Release` is clean (`TreatWarningsAsErrors=true`).
2. `dotnet test --filter "Category!=Integration"` passes.
3. `dotnet test --filter "Category=RetrievalQuality"` passes with the flag **off** — proves no regression to the default hybrid path.
4. The connector path is exercised by at least one `Category=Integration` test (round-trip + ranking sanity).
5. A/B deltas (hybrid vs pure-vector) recorded against the ADR-0007 baseline; no claim that pure vector matches hybrid.
6. ADR-0003 and ADR-0004 amended (not superseded); foothold ADR added; flag documented.

## 8. Open questions

- If upstream never ships Postgres hybrid, do we later accept app-side RRF fusion (re-architecture, ADR-0003 change)? **Deferred** — revisit only with a concrete upstream capability or a measured need.
- Should indexing **writes** move to connector `UpsertAsync`? **Out of scope** — the connector offers no `COPY`-speed bulk insert and wants schema ownership; current Dapper/Npgsql write path stays.
