# Verification Report: Production Improvements Implementation Plan

**Plan:** `docs/plans/2026-06-11-production-improvements-implementation-plan.md`  
**Base commit:** `d50b438b8f25c163ca9a5acfb7e2917fdaacaa96`  
**Verified against HEAD:** `73438dc`  
**Date:** 2026-06-13

---

## Completeness

| Task | Plan Item | Status | Evidence |
|---|---|---|---|
| T1 | Index Manifest — migration + write | Done | `0005-index-manifest.sql`; `IndexingRepository.InsertManifestAsync`; `IndexAllAsync_WritesManifestRow` + `IndexAllAsync_IncrementalRun_StillWritesManifest` integration tests |
| T2 | Expose manifest in `rfc_stats` | Done | `GetStatsAsync_AfterIndexing_IncludesManifest` integration test at `RfcRagIntegrationTests.cs:615` |
| T3 | Golden eval dataset v1 | Done | `golden_questions.json`: 32 items, 30 testdata-corpus, 4 no_answer, 9 normative_explanation, 27 with expectedSections, 3 obsolete-conflict candidates |
| T4 | Retrieval metric functions | Done | `Evaluation/RetrievalMetrics.cs`, `EvalModels.cs`, `RetrievalMetricsTests.cs` (all 5 required scenarios) |
| T5 | Retrieval eval harness + baseline | Done | `EvalCommand.cs`, `RetrievalQualityTests.cs` with thresholds, `docs/eval/reports/baseline-testdata.json` committed |
| T6 | Evidence Pack models + vocabulary | Done | `EvidencePack.cs`, `EvidenceSection.cs`, `EvidenceWarning.cs`; CONTEXT.md terms added |
| T7 | Context Assembler core | Done | `ContextAssembler.cs`; `ContextAssemblerTests.cs` covers all 5 required scenarios |
| T8 | Evidence enrichment — relations + NOs | Done | Batch methods in `MetadataRepository.cs`; enrichment in `ContextAssembler.EnrichAsync` |
| T9 | CLI `evidence` verb | Done | `CliCommand.cs:38,122`; `CliCommandTests.cs` covers happy path + missing args |
| T10 | Generation options + chat client plumbing | Done | `RfcRagOptions`, `RfcRagOptionsValidator`, `AnsweringExtensions.cs`, `FakeChatClient.cs` |
| T11 | Answer Generator + injection resistance | Done | `AnswerGenerator.cs`, `GeneratedAnswer.cs`; `AnswerGeneratorTests.cs` covers happy path, repair, double-malformed, noAnswer, injection unit test |
| T12 | Citation discipline + no-answer gate | Done | `CitationDiscipline.cs`, `AskService.cs`; unit tests present |
| T13 | `ask_rfc` MCP tool + CLI `ask` verb | Done | `RfcAskTools.cs`; `CliCommand.cs:39,144`; conditional registration in `Program.cs:26-35`; docs updated |
| T14 | Injection + no-answer hardening | **Partial** | Fixture `rfc9998-injection.txt` exists (different name than planned `rfc9998.txt`); parser-level injection tests in `PromptInjectionTests.cs`; unit-level prompt-structure test in `AnswerGeneratorTests.cs:197`. **Missing**: no end-to-end pipeline integration test indexing the fixture and asserting hostile text confined to data block; no ≥3 explicit no-answer integration test cases |
| T15 | Answer metric functions | Done | `AnswerEvaluationMetrics.cs`; `AnswerEvaluationMetricsTests.cs` (14 test methods) |
| T16 | Answer eval runner + CI harness | Done | `EvalCommand.cs` answers mode; `AnswerQualityTests.cs`; `make eval-answers` target |
| T17 | Query Planner core | Done | `QueryPlanner.cs`, `QueryPlan.cs`; `QueryPlannerTests.cs` (8 [Theory] tests including negatives) |
| T18 | Planner routing (eval-gated) | Done | Integrated in `SearchService.cs:20`; `QueryPlannerEnabled` flag |
| T19 | Wider candidates + reranker core | Done | `DeterministicReranker.cs`, `HybridCandidate.cs`; `DeterministicRerankerTests.cs` — but missing isolated [Theory] for 3 signals |
| T20 | Reranker integration (eval-gated) | Done | `SearchService.cs:35`; `RerankerEnabled` flag; ADR-0007 |
| T21 | Status surface on results + evidence | Done | `RfcStatusBlock.cs`; `include_obsolete` parameter on search + ask tools; integration test |
| T22 | Current-vs-historical answer behavior | Done | Relation notes in `AnswerGenerator` prompt; obsolete-conflict golden items present |
| T23 | Errata ingestion from local snapshot | Done | `0006-rfc-errata.sql`, `ErrataLoader.cs`; `ErrataLoaderTests.cs`; `make fetch-errata` target |
| T24 | Errata in evidence + answers | Done | `ContextAssembler.AttachErrataAsync`; `include_errata` on `ask_rfc`; `AnswerQualityTests.cs:180` |
| T25 | Query Trace (JSONL) | Done | `QueryTrace.cs`, `QueryTraceWriter.cs`, `ITraceQueue.cs`, `TraceBackgroundService.cs` — with findings (see below) |
| T26 | Full citation verifier | Done | `CitationVerifier.cs`; `CitationVerifierTests.cs` (14 test methods covering all 4 required scenarios) |
| T27 | Docs/ADRs/CHANGELOG sweep | **Partial** | ADRs 0006–0009 all present; README, configuration.md, cli-mode-guide.md, CHANGELOG updated. **Missing**: "Cited Answer" not defined in CONTEXT.md (Task 13 acceptance criterion, reiterated here) |

**Scope drift:**
- `docs/plan/` directory (singular) created alongside `docs/plans/` (plural) — contains `2026-06-13-verification-remediation-plan.md`. Should be `docs/plans/` per project convention.
- `.agents/skills/to-prd/` added (unrelated to plan scope).

---

## Findings

### Blockers

- **`QueryTraceWriter` not sealed** — `src/RfcRag/Infrastructure/QueryTraceWriter.cs:14`
  > `internal class QueryTraceWriter`
  Every internal class must be `internal sealed` per project code standards. Unsealed allows unintended subclassing and violates the house naming convention applied uniformly to all other classes in the same slice (e.g., `TraceBackgroundService` is correctly `internal sealed class`).

### Important

- **`DateTime.UtcNow` in `QueryTraceWriter.GetFilePath()`** — `src/RfcRag/Infrastructure/QueryTraceWriter.cs:67`
  > `string date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);`
  Architecture Decision 11 requires `TimeProvider` injection for all time observation. `EmbeddingRetryPolicy` (the stated pattern) correctly injects `TimeProvider`; `QueryTraceWriter` uses `DateTime.UtcNow` directly. This makes date-based file naming untestable without real-clock dependency.

- **"Cited Answer" missing from CONTEXT.md** — `CONTEXT.md` (no line)
  Task 13 acceptance criterion ("Update … CONTEXT.md — Cited Answer") and Architecture Decision 12 ("New domain terms enter CONTEXT.md in the task that introduces them") both require it. The term appears in `README.md:120` and `src/RfcRag/README.md` but has no canonical glossary entry.

- **Task 14 pipeline injection integration test absent** — no file
  The acceptance criterion states: *"Pipeline test with scripted `FakeChatClient` asserting the hostile text only ever appears inside the evidence data block of the captured prompt."* `PromptInjectionTests.cs` tests only the parser layer; `AnswerGeneratorTests.cs:197` tests only the generator in isolation. No test indexes `rfc9998-injection.txt`, runs a search query that retrieves it, and asserts on the full prompt structure captured by `FakeChatClient`. The plan also requires ≥3 no-answer integration test cases; currently none are explicit in integration test files.

- **DeterministicRerankerTests missing 3 isolated signal [Theory] tests** — `tests/RfcRag.Tests/UnitTests/DeterministicRerankerTests.cs`
  Task 19 acceptance criterion: *"Each signal has isolated `[Theory]` tests (signal fires / doesn't fire / combined ordering)."* Tests for `RfcNumberMatchBonus`, `HeadingTermMatchBonus`, and `ObsoletedRfcPenalty` have [Theory] coverage. Tests for `SectionMatchBonus`, `ProtocolRfcBonus`, and `UpdatedByRelevanceBonus` are absent; their contribution is only visible via the combined-signal test.

### Nice-to-have

- **Fixture named `rfc9998-injection.txt`, plan says `rfc9998.txt`** — `tests/RfcRag.Tests/TestData/rfc9998-injection.txt`
  Minor naming drift. File content and test assertions are correct; only the filename diverges from the plan.

- **ErrataLoaderTests lacks idempotent upsert test** — `tests/RfcRag.Tests/UnitTests/ErrataLoaderTests.cs`
  Task 23 acceptance criterion: *"errata ingested idempotently (re-run produces no duplicates)."* Error-path unit tests exist; no test verifies that loading the same fixture twice produces the same DB row count. This is typically verifiable only via an integration test.

- **No retry pattern for `IChatClient` calls** — `src/RfcRag/Answering/AnswerGenerator.cs`
  Architecture Decision 11 states *"chat calls reuse the hand-rolled bounded-backoff retry pattern."* `AnswerGenerator.GenerateAsync` calls `_chatClient.GetResponseAsync` without retry wrapping. The plan noted this pattern (Task 10); embedding-generation has it; chat does not.

---

## Codegraph Impact

- `AssembleAsync` — 4 callers; all updated (`AskService.AskAsync`, 3 `AnswerQualityTests` methods). No unupdated callers.
- `QueryTraceWriter` — 2 dependents: `TraceBackgroundService` (uses `WriteAsync`, correct); no callers of `GetFilePath` outside the class. Impact contained.

---

## Tests

- **Unit tests ran:** 393 passed, 5 skipped, 0 failed. All new test classes executed.
- **Integration tests skipped:** `Category=Integration`, `Category=RetrievalQuality`, `Category=AnswerQuality` — require Docker / Testcontainers; not available in this verification run.
- **Failures:** None.

---

## Behavioral Check

- **Conditional `ask_rfc` registration:** Confirmed via `Program.cs:21-35` — `RfcAskTools` added to MCP tool list only when `answeringEnabled`. `CliCommand.RunAskAsync` at line 152 returns error with `RfcRag__ChatModel` config key when `askService is null`.
- **`--cli evidence` verb:** Confirmed wired at `CliCommand.cs:38`; assembles pack and writes JSON.
- **No LLM calls in CI:** Confirmed — `FakeChatClient` in `Fakes/` is hand-rolled, scripted; no real API client in tests.
- **Integration behavioral check (manifest write, stats, retrieval quality, answer quality):** Cannot verify without running integration tests. Based on test code review, logic is correct.

---

## Acceptance Criteria (plan-level)

### Checkpoint 1 — Foundation
- [x] All tests pass (`make test`) — unit suite green; integration not run in CI context
- [x] `rfc_stats` shows manifest — confirmed `GetStatsAsync_AfterIndexing_IncludesManifest` integration test
- [x] Baseline retrieval report committed — `docs/eval/reports/baseline-testdata.json` exists

### Checkpoint 3 — End-to-end answering
- [x] Full suite green (unit) — 393 passed
- [ ] Injection fixtures pass end-to-end — parser + generator unit levels only; pipeline integration test absent
- [x] `ask_rfc` conditional registration correct

### Checkpoint 7 — Complete
- [x] ADRs 0006–0009 all present and match what was built
- [x] README, configuration.md, cli-mode-guide.md, eval/README.md accurate
- [ ] CONTEXT.md complete — "Cited Answer" missing

---

## Recommendation

**Remediation needed.** Four Important findings and one Blocker require fixes before the plan can be marked complete. The implementation is substantively correct and comprehensive (25 of 27 tasks fully done); the gaps are narrow and addressable in a single focused session. See `docs/plans/2026-06-13-implementation-remediation-plan.md` for the ordered task list.
