# Implementation Plan: Parser Determinism, Retrieval-Time Filtering, Embedding Hardening

## Overview

Five hardening improvements for rfc-rag: (1) deterministic input discovery with TXT as the
primary source and XML as a secondary/fallback source, (2) normative keyword filtering pushed
into the hybrid-search SQL, (3) provider-aware retry/backoff/observability for the embedding
pipeline, (4) startup config validation, and (5) a "known ugly RFC" regression corpus. Tasks
are sized S/M, mostly independent, and each leaves the system working.

## Architecture Decisions

- **TXT is the canonical parse; XML is fallback-only.** `RfcXmlParser` currently extracts no
  ABNF blocks, no normative occurrences, and weaker metadata, so when both `rfcN.txt` and
  `rfcN.xml` exist, TXT must always win. `RfcParserType.Xml` is redefined as "prefer `.txt`;
  use `.xml` only for RFC numbers that have no `.txt`". Enum value names (`Text`/`Xml`) are
  kept so existing configuration keeps working; only doc-comments and docs change.
- **New deep module: `RfcSourceResolver`** (internal, `Indexing/`). One seam owning mirror-path
  expansion, file enumeration, filename→RFC-number parsing, and the one-source-per-number
  precedence rule (`.txt` > `.xml`, lexicographic path as tiebreaker for same-extension
  duplicates across subdirectories). `IndexAllAsync` and `IndexSingleAsync` both call it.
  The race disappears by construction, and the policy becomes unit-testable with temp dirs —
  no DB needed. Locality: today the policy is smeared across `IndexAllAsync`,
  `IndexSingleAsync` (which hardcodes `.txt`), and `TryCreateSourceFile`.
- **Normative filtering moves behind the `SearchRepository` seam.** `SearchHybridAsync` gains
  an optional `normativeKeyword` and applies an `EXISTS` predicate against
  `normative_occurrences` inside *both* candidate CTEs (before their LIMITs). The DB fills the
  limit from filtered pools — "fetch until filled" done by the engine. `SearchService` loses
  the `limit * 3` overscan and the post-filter; `FilterSectionsByNormativeKeywordAsync` is
  deleted (sole caller is `SearchService` — deletion test passes: complexity concentrates into
  one SQL statement).
- **Embedding retry becomes an explicit policy module.** Internal `EmbeddingRetryPolicy`
  classifies `ClientResultException` by status (429/408/5xx retryable, other 4xx fatal),
  honors `Retry-After`, applies exponential backoff with full jitter via injected
  `TimeProvider` (testable with `FakeTimeProvider`, no mocks). The OpenAI SDK's built-in
  pipeline retry is disabled so backoff is owned in exactly one place.
- **Config validation via `IValidateOptions<RfcRagOptions>` + `ValidateOnStart()`** — fail
  fast at boot with all violations aggregated, validator unit-tested directly.
- **Regression fixtures are real RFC files committed to `TestData/`** (consistent with
  existing practice there; IETF Trust license permits reproduction). Expected values are
  pinned from rfc-editor.org metadata.

## Dependency Graph

```
Task 1 (source discovery)   Task 2 (config validation)   Task 3 (SQL filter)   Task 4 (retry policy)
        │                                                                              │
        │                                                                       Task 5 (wire + metrics)
        └── Task 6 (regression corpus; weak dep on 1 for XML Id assertions)
                    │
              Task 7 (fix what 6 finds)
```

Tasks 1, 2, 3, 4 are mutually independent (disjoint files) and safe to parallelize.

---

## Task List

### Phase 1 — Determinism and validation

#### Task 1: Deterministic RFC source discovery (TXT primary, XML fallback)

**Description:** Extract input discovery from `RfcIndexer` into a new internal
`RfcSourceResolver` that returns exactly one source file per RFC number. In `Text` mode only
`rfc*.txt` is considered. In `Xml` mode, `.xml` is used only for numbers with no `.txt`.
Same-extension duplicates across subdirectories are deduplicated deterministically
(lexicographically smallest relative path wins). `IndexSingleAsync` uses the resolver too
(today it hardcodes `.txt`, so XML fallback never applies to single-RFC indexing). While
touching the XML path, fix the latent bug where `RfcXmlParser` never sets `RfcSection.Id`
(TXT parser does `Guid.NewGuid()`; XML sections all get `Guid.Empty`).

**Acceptance criteria:**
- [ ] A mirror containing both `rfcN.txt` and `rfcN.xml` in `Xml` mode indexes exactly one
      document for N, parsed from `.txt` (normative occurrences present, stable SHA on re-run
      — no re-index churn).
- [ ] An RFC number with only `.xml` is indexed in `Xml` mode and skipped in `Text` mode.
- [ ] XML-parsed sections have unique, non-empty `Id`s.

**Verification:**
- [ ] `dotnet test tests/RfcRag.Tests/ --filter "FullyQualifiedName~RfcSourceResolver"`
- [ ] `dotnet test tests/RfcRag.Tests/ --filter "Category=Integration&FullyQualifiedName~Index"` (Docker)
- [ ] `dotnet build` clean.

**Tests** (unit, temp dirs, no DB — `UnitTests/RfcSourceResolverTests.cs`):
`Resolve_TxtAndXmlSameNumber_PrefersTxt`, `Resolve_XmlOnlyNumber_IncludedInXmlMode`,
`Resolve_TextMode_IgnoresXml`, `Resolve_DuplicateTxtAcrossSubdirs_PicksDeterministically`,
`Resolve_TildePath_ExpandsToUserProfile`. Integration: temp mirror with the txt/xml pair →
one `indexed_rfcs` row pointing at the `.txt`.

**Dependencies:** None.

**Files likely touched:**
- `src/RfcRag/Indexing/RfcSourceResolver.cs` (new)
- `src/RfcRag/Indexing/RfcIndexer.cs`
- `src/RfcRag/Parsing/RfcXmlParser.cs` (set `Id`)
- `src/RfcRag/Settings/RfcRagOptions.cs` (doc-comment only)
- `tests/RfcRag.Tests/UnitTests/RfcSourceResolverTests.cs` (new), integration test addition
- `docs/configuration.md` (RfcParserType row wording)

**Estimated scope:** M

#### Task 2: Config validation at startup

**Description:** Add `RfcRagOptionsValidator : IValidateOptions<RfcRagOptions>` (internal
sealed, `Settings/`) and register via `AddOptions<RfcRagOptions>().Bind(...)` +
`ValidateOnStart()` in `Program.cs`. Rules: `RfcMirrorPath` and `PostgresConnectionString`
non-empty; `EmbeddingModel` non-empty; `EmbeddingBatchSize` in 1..2048;
`EmbeddingDimensions` in 1..16000 (pgvector cap); `MaxIndexingParallelism` ≥ 1;
`MaxEmbeddingConcurrency` ≥ 1; `OpenRouterEmbeddingEndpoint` and `LocalEmbeddingEndpoint`
absolute http(s) URIs. All violations reported in one aggregated failure. Mirror-path
*existence* deliberately stays a runtime (indexing-time) check so a search-only server can
run against an already-indexed DB without the mirror mounted.

**Acceptance criteria:**
- [ ] Starting with e.g. `RfcRag__EmbeddingBatchSize=0` fails at boot, message names the
      property and the valid range; multiple violations are all listed.
- [ ] Default options validate clean; existing integration tests unaffected.

**Verification:**
- [ ] `dotnet test tests/RfcRag.Tests/ --filter "FullyQualifiedName~RfcRagOptionsValidator"`
- [ ] Manual: `RfcRag__EmbeddingBatchSize=0 dotnet run --project src/RfcRag/ -- --cli stats` → clear startup error.

**Tests:** `UnitTests/RfcRagOptionsValidatorTests.cs`, `[Theory]`/`[InlineData]` per rule,
named `Validate_<State>_<ExpectedResult>`, invoking the validator directly (no host).

**Dependencies:** None.

**Files likely touched:**
- `src/RfcRag/Settings/RfcRagOptionsValidator.cs` (new)
- `src/RfcRag/Program.cs`
- `tests/RfcRag.Tests/UnitTests/RfcRagOptionsValidatorTests.cs` (new)
- `docs/configuration.md` (document valid ranges)

**Estimated scope:** S

### Checkpoint: Phase 1
- [ ] `dotnet build` clean, unit tests pass: `dotnet test --filter "Category!=Integration"`.
- [ ] Integration: duplicate txt/xml mirror test green (Docker).
- [ ] Manual: invalid config rejected at startup with aggregated message.

### Phase 2 — Retrieval

#### Task 3: Push normative filtering into hybrid-search SQL

**Description:** Add `string? normativeKeyword` to `SearchRepository.SearchHybridAsync`. When
present, both the `lexical` and `vector` CTEs gain
`and exists (select 1 from rfc_rag.normative_occurrences o where o.section_id = rfc_sections.id and o.keyword = upper(@NormativeKeyword))`
(house-style conditional interpolation, as with `rfcNumbers`). While editing these CTEs, add
the missing explicit `order by rank` before each CTE's `limit` — today the LIMIT relies on
plan order, which is not guaranteed. `SearchService.SearchAsync` drops the
`limit * 3` overscan and the in-memory post-filter, normalizes whitespace-only keyword to
null (fixing the current `is not null` vs `IsNullOrWhiteSpace` inconsistency), and passes the
keyword through. Delete `FilterSectionsByNormativeKeywordAsync` and its tests.

**Acceptance criteria:**
- [ ] With ≥ limit matching sections ranked *below* the old `3×limit` candidate window, a
      keyword-filtered search returns exactly `limit` results (old code returned partial).
- [ ] Keyword with zero corpus matches returns empty; un-keyworded search behavior unchanged
      (existing integration tests pass untouched).
- [ ] `FilterSectionsByNormativeKeywordAsync` no longer exists.

**Verification:**
- [ ] `dotnet test tests/RfcRag.Tests/ --filter "Category=Integration&FullyQualifiedName~Search"` (Docker)
- [ ] `dotnet test tests/RfcRag.Tests/ --filter "Category=RetrievalQuality"` (no regression in hit rate)

**Tests** (integration, Testcontainers): `SearchAsync_KeywordMatchesBeyondCandidateWindow_FillsLimit`,
`SearchAsync_KeywordWithNoMatches_ReturnsEmpty`, `SearchAsync_WhitespaceKeyword_TreatedAsNoFilter`.

**Dependencies:** None.

**Files likely touched:**
- `src/RfcRag/Search/SearchRepository.cs`
- `src/RfcRag/Search/SearchService.cs`
- `tests/RfcRag.Tests/UnitTests/SearchRepositoryTests.cs`
- `tests/RfcRag.Tests/IntegrationTests/RfcRagIntegrationTests.cs`

**Estimated scope:** M

**Note (pgvector):** with a WHERE clause, HNSW post-filters and the vector arm can underfill
on pgvector < 0.8; the filtered lexical arm plus the existing 4× candidate overscan
compensate via RRF. Optional follow-up (not in this task): `set_config('hnsw.iterative_scan',
'relaxed_order')` on connections when a keyword filter is active (pgvector ≥ 0.8).

### Phase 3 — Embeddings as an external dependency

#### Task 4: Embedding retry policy module

**Description:** New internal sealed `EmbeddingRetryPolicy` (`Indexing/`) replacing the
retry-everything loop in `EmbeddingService.RetryAsync`. Behavior: classify
`ClientResultException` by `Status` — 429 and 408 and 5xx retryable, other 4xx fatal
(fail fast on 400/401/403 instead of burning 3 attempts); transport faults
(`HttpRequestException`, `IOException`, `TaskCanceledException` when not user-initiated)
retryable. Honor `Retry-After` (delta-seconds and HTTP-date forms) from the raw response,
capped at a max delay; otherwise exponential backoff with full jitter (base 1s, cap 30s).
Delays via `TimeProvider.Delay` with constructor-injected `TimeProvider` so tests use
`FakeTimeProvider`. Expose the status/exception classifier as an internal static function —
that is the test surface for classification cases; full-policy tests use throwing fake
generators (no mocks, per repo rule).

**Acceptance criteria:**
- [ ] 429 with `Retry-After: 7` waits ~7s (observed via FakeTimeProvider), then succeeds.
- [ ] 400 response fails immediately with the provider message preserved; no retries.
- [ ] Retries exhausted → exception propagates carrying attempt count context.

**Verification:**
- [ ] `dotnet test tests/RfcRag.Tests/ --filter "FullyQualifiedName~EmbeddingRetryPolicy"`

**Tests:** `UnitTests/EmbeddingRetryPolicyTests.cs` — classification `[Theory]` over status
codes; `ExecuteAsync_RateLimitedWithRetryAfter_DelaysRequestedInterval`,
`ExecuteAsync_FatalStatus_DoesNotRetry`, `ExecuteAsync_Cancellation_PropagatesImmediately`.
Add `Microsoft.Extensions.TimeProvider.Testing` to the test project only.

**Dependencies:** None.

**Files likely touched:**
- `src/RfcRag/Indexing/EmbeddingRetryPolicy.cs` (new)
- `tests/RfcRag.Tests/UnitTests/EmbeddingRetryPolicyTests.cs` (new)
- `tests/RfcRag.Tests/RfcRag.Tests.csproj` (test-only package)

**Estimated scope:** M

#### Task 5: Wire policy + observability into EmbeddingService

**Description:** `EmbeddingService` delegates to `EmbeddingRetryPolicy`; remove the old
`RetryAsync`. Add per-batch structured logging via `[LoggerMessage]` (batch index, size,
attempt, delay, final failure) and a `System.Diagnostics.Metrics.Meter` named
`RfcRag.Embeddings` with: `embedding.batches` counter (tag `outcome` = ok|failed),
`embedding.retries` counter (tag `reason` = rate_limited|server_error|transport). Wire an
OTLP metrics exporter (`OpenTelemetry.Extensions.Hosting` +
`OpenTelemetry.Exporter.OpenTelemetryProtocol`) registered **only when
`OTEL_EXPORTER_OTLP_ENDPOINT` is set**; all exporter settings come from standard `OTEL_*`
env vars — no custom config keys, no Task 2 validator growth (grilling decision 2026-06-10).
Metrics only: logs stay on stderr, no tracing. Validate
provider responses: returned embedding count must equal batch input count, and vector length
must equal the configured `EmbeddingDimensions` — throw a clear `InvalidOperationException`
naming the batch and counts instead of failing later with an opaque pgvector dimension error
at insert. In `ServiceCollectionExtensions`, disable the OpenAI SDK's built-in pipeline retry
for both providers (`OpenAIClientOptions.RetryPolicy` with zero retries — confirm exact API
against the SDK version at implementation time) so retry ownership is singular.

**Acceptance criteria:**
- [ ] A generator returning a wrong count or wrong dimensions produces a clear exception
      naming expected vs actual (unit-tested with a misbehaving fake).
- [ ] Retried-then-successful batch emits retry log + counter; existing
      `EmbeddingServiceTests` still pass (updated for constructor signature).
- [ ] With `OTEL_EXPORTER_OTLP_ENDPOINT` unset, no exporter is registered and no
      connection attempts occur; when set, counters reach the collector (manual check).

**Verification:**
- [ ] `dotnet test tests/RfcRag.Tests/ --filter "FullyQualifiedName~EmbeddingService"`
- [ ] `dotnet test --filter "Category!=Integration"` (full unit sweep)

**Dependencies:** Task 4.

**Files likely touched:**
- `src/RfcRag/Indexing/EmbeddingService.cs`
- `src/RfcRag/Infrastructure/ServiceCollectionExtensions.cs`
- `src/RfcRag/Program.cs` (conditional OTel registration)
- `src/RfcRag/RfcRag.csproj` (OTel packages)
- `tests/RfcRag.Tests/UnitTests/EmbeddingServiceTests.cs`
- `tests/RfcRag.Tests/Fakes/` (misbehaving fake generator)
- `docs/configuration.md` (document `OTEL_EXPORTER_OTLP_ENDPOINT` opt-in)

**Estimated scope:** M

### Checkpoint: Phase 3
- [ ] All unit tests pass; integration suite green (Docker).
- [ ] Manual: index a few RFCs against OpenRouter with tiny `MaxEmbeddingConcurrency` and
      observe structured retry logs on stderr when throttled.

### Phase 4 — Regression corpus

#### Task 6: Known-ugly-RFC fixtures and characterization tests

**Description:** Commit a small set of historically awkward RFC files to
`tests/RfcRag.Tests/TestData/` and pin expected parse results. New TXT assertions extend
`RfcParserTests`; new XML assertions go to a new `RfcXmlParserTests` class (existing
XML facts stay where they are — surgical changes only).

Fixture → what it locks down:
- `rfc793.txt` (1981, TCP) — old-format section boundaries, body-on-heading-line style,
  page header/footer stripping, title extraction without modern front matter.
- `rfc822.txt` — appendices A–D and `A.1.1`-style numbering survive section splitting.
- `rfc5234.txt` (ABNF spec) — core-rules ABNF block extraction, `=/` incremental rules,
  rule-name dedup.
- `rfc8174.txt` — `Updates: 2119` metadata; uppercase-only keyword rule (lowercase
  "should"/"may" prose MUST NOT be extracted); `NOT RECOMMENDED` matched as one keyword and
  not double-counted as `RECOMMENDED`.
- `rfc9293.txt` (TCP bis) — *wrapped multi-line* `Obsoletes:` header list
  (793, 879, 2873, 6093, 6429, 6528, 6691) — exercises `ExtractIntArray` across
  continuation lines; suspected current loss of wrapped values.
- `rfc9110.txt` (already present) — add: `Appendix A` ("Collected ABNF") is extracted as a
  section and its ABNF blocks are attributed to it.
- Small `rfcN.xml` + `rfcN.txt` pair — XML sections get unique non-empty Ids; documents the
  intended TXT-over-XML precedence at parser level (discovery precedence itself is covered by
  Task 1's tests).

**Acceptance criteria:**
- [ ] Every dimension above has ≥ 1 test with expected values sourced from
      rfc-editor.org metadata (not from current parser output).
- [ ] Fixtures total well under ~1 MB added.

**Verification:**
- [ ] `dotnet test tests/RfcRag.Tests/ --filter "FullyQualifiedName~RfcParser"` — failures at
      this point are *findings*, handed to Task 7.

**Dependencies:** Task 1 (for the XML `Id` fix assertions).

**Files likely touched:**
- `tests/RfcRag.Tests/TestData/` (≈6 fixture files)
- `tests/RfcRag.Tests/UnitTests/RfcParserTests.cs`
- `tests/RfcRag.Tests/UnitTests/RfcXmlParserTests.cs` (new)

**Estimated scope:** M

#### Task 7: Fix parser bugs surfaced by Task 6

**Description:** Triage every red test from Task 6. Apply surgical fixes in
`RfcParser`/`RfcXmlParser` (expected candidates: wrapped Obsoletes/Updates continuation
lines, appendix heading edge cases). Any fix that would exceed S scope on its own is *not*
hacked in: the test gets `[Fact(Skip = "known-issue: <ref>")]` and an entry in the
Follow-ups section below — no silent skips, no scope creep.

**Acceptance criteria:**
- [ ] All Task 6 tests pass or carry an explicit known-issue skip with a follow-up entry.
- [ ] No pre-existing test regresses.

**Verification:**
- [ ] `dotnet test --filter "Category!=Integration"`; then full `make test` (Docker).

**Dependencies:** Task 6.

**Files likely touched:** `src/RfcRag/Parsing/RfcParser.cs`, `src/RfcRag/Parsing/RfcXmlParser.cs`, test files from Task 6.

**Estimated scope:** S–M (bounded by the skip rule)

### Checkpoint: Complete
- [ ] `make test` fully green (unit + integration + RetrievalQuality).
- [ ] Docs updated: `docs/configuration.md` (parser-mode semantics, validation ranges,
      `OTEL_EXPORTER_OTLP_ENDPOINT`), `docs/normative-search.md` (filtering now
      retrieval-time in SQL), `CHANGELOG.md`. README: soften "binding requirements" to match
      the lexical-signal definition of Normative Occurrence (see `CONTEXT.md`).
- [ ] Behavior-change note in CHANGELOG: `Xml` mode no longer double-indexes; users who ran
      `Xml` mode before should force a re-index once to settle source attribution.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| HNSW post-filter underfills vector arm on pgvector < 0.8 | Med | Filter applied in both CTEs; lexical arm filters exactly; 4× candidate overscan retained; `hnsw.iterative_scan` noted as follow-up |
| Double-retry interplay with OpenAI SDK's built-in pipeline retry | Med | Explicitly disable SDK retry in `ServiceCollectionExtensions`; verify exact `OpenAIClientOptions.RetryPolicy` API at impl time |
| `ClientResultException` hard to construct in tests | Low | Classifier exposed as internal static function over status/headers — tested directly; `InternalsVisibleTo("RfcRag.Tests")` already present |
| `Xml`-mode semantics change surprises existing users | Low | Names unchanged, docs + CHANGELOG updated; old double-indexed DBs self-heal on next forced re-index |
| Regression fixtures reveal a deep section-splitter flaw | Med | Task 7 skip-with-follow-up rule keeps scope bounded; fixes land as separate tasks |
| `EmbeddingService` constructor changes ripple into test fixtures | Low | Update fixtures in the same task (lesson: DI divergence between prod and tests masks errors) |

## Parallelization

- **Safe in parallel:** Tasks 1, 2, 3, 4 (disjoint files, no shared state).
- **Sequential:** 4 → 5; 6 → 7.
- **Coordine point:** none across phases — no shared API contract changes between tasks
  except `SearchHybridAsync`'s signature (Task 3 only).

## Open Questions — all resolved (grilling session, 2026-06-10)

1. **XML enrichment:** resolved — strict fallback, no enrichment; one Source per RFC number,
   `.txt` always wins. Recorded in `docs/adr/0001-txt-canonical-xml-fallback.md` and
   `CONTEXT.md` (term: **Source**).
2. **Retry tunables:** resolved — internal constants in `EmbeddingRetryPolicy` (3 attempts,
   1s base, 30s cap). Promote to config later only on demonstrated operational need.
3. **Metrics surface:** resolved — Meter **plus** OTLP exporter, registered only when
   `OTEL_EXPORTER_OTLP_ENDPOINT` is set; standard `OTEL_*` env vars, no custom keys,
   metrics only. Task 5 updated accordingly.
4. **Normative semantics (raised during grilling):** a Normative Occurrence is a *lexical*
   uppercase-keyword match, not a claim of formal BCP 14 adoption — pins Task 6's rfc793
   expectations. Recorded in `CONTEXT.md` (terms: **Normative Keyword**,
   **Normative Occurrence**).

## Follow-ups (populated during Task 7)

- **rfc822 appendix sections**: `SectionHeadingRegex` does not match bare-letter appendix style
  (`A. EXAMPLES`), so RFC 822 appendices A–D and sub-sections like `A.1.1` are not split into
  separate sections. The regex currently requires either a digit-prefix heading (`1.`, `1.1.`)
  or the explicit word `Appendix` (`Appendix A.`). A fix would extend the regex to also match
  bare-letter appendix headings — but any such change must avoid false positives (e.g. `B.
  Sockets` in a non-appendix section). The characterization test is skipped with
  `known-issue: SectionHeadingRegex does not match bare-letter appendix style`.
  (`tests/RfcRag.Tests/UnitTests/RfcParserTests.cs`)
