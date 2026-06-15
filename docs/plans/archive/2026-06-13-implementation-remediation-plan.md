# Remediation Plan: Production Improvements Verification Gaps

Source report: [2026-06-13-implementation-verification-report.md](./2026-06-13-implementation-verification-report.md)  
Source plan: [2026-06-11-production-improvements-implementation-plan.md](./2026-06-11-production-improvements-implementation-plan.md)

## Overview

Five verification findings — one Blocker, four Important — prevent the production improvements plan from being marked complete. All gaps are narrow and confined to three files in the implementation and two missing test scenarios. Estimated total scope: one focused session (~5 files, ~150 lines).

## Architecture Decisions Carried Forward

All decisions from the original plan apply. No new decisions.

## Dependency Graph

```
R1 sealed + TimeProvider (QueryTraceWriter)
R2 Cited Answer in CONTEXT.md
R3 DeterministicReranker isolated signal tests
R4 Pipeline injection integration test + no-answer cases
    └─ depends on: rfc9998-injection.txt already exists ✓
        FakeChatClient already exists ✓
        AskService + ContextAssembler already wired ✓
```

R1, R2, R3 are independent. R4 is independent but slightly larger — do it last within the session.

---

## Task List

### Remediation Task R1: Seal `QueryTraceWriter` and inject `TimeProvider`

**Description:** Two code-standards fixes in the same class. (1) Add `sealed` to `QueryTraceWriter` (`internal class` → `internal sealed class`) per project naming convention. (2) Add `TimeProvider timeProvider` to the constructor and replace the `DateTime.UtcNow` call in `GetFilePath()` with `timeProvider.GetUtcNow()`, following the `EmbeddingRetryPolicy` pattern. Register `TimeProvider.System` in `ServiceCollectionExtensions.AddRfcRagServices()` if it is not already (check first — `EmbeddingRetryPolicy` may already register it).

**Acceptance criteria:**
- [ ] `QueryTraceWriter` is `internal sealed class`.
- [ ] Constructor takes `TimeProvider timeProvider`; `GetFilePath()` uses `timeProvider.GetUtcNow().ToString("yyyy-MM-dd", ...)`.
- [ ] `QueryTraceWriterTests.cs` gains a test asserting date-based file naming with a fake `TimeProvider` (e.g., `ManualTestClock` or `new FakeTimeProvider()`).
- [ ] `dotnet build` clean; `dotnet test --filter "FullyQualifiedName~QueryTraceWriter"` passes.

**Verification:**
- [ ] `dotnet build -warnaserror` clean.
- [ ] `dotnet test tests/RfcRag.Tests/ --filter "FullyQualifiedName~QueryTraceWriter"` passes including the new test.

**Dependencies:** None.

**Files likely touched:**
- `src/RfcRag/Infrastructure/QueryTraceWriter.cs` (2 lines changed + constructor param)
- `src/RfcRag/Infrastructure/ServiceCollectionExtensions.cs` (check/add `TimeProvider.System` registration)
- `tests/RfcRag.Tests/UnitTests/QueryTraceWriterTests.cs` (new test method)

**Estimated scope:** XS–S (3 files, ~15 lines)

---

### Remediation Task R2: Add "Cited Answer" to `CONTEXT.md`

**Description:** The term "Cited Answer" is used in README.md and src/RfcRag/README.md but has no canonical glossary entry in CONTEXT.md. Task 13's acceptance criterion and Architecture Decision 12 both require it. Definition: the structured output of `ask_rfc` containing an `answer` string, `citations` (each with an evidence id, RFC number, section, and verbatim quote), a `verification` block, and `warnings`. Avoid "grounded response", "cited response", "LLM answer" (implies the model answered without grounding).

**Acceptance criteria:**
- [ ] CONTEXT.md defines **Cited Answer** with avoid-guidance consistent with other glossary entries.
- [ ] The definition includes its fields (`answer`, `citations`, `verification`, `warnings`) and its relationship to Evidence Pack (a Cited Answer's citations reference Evidence Sections by evidence id).
- [ ] `dotnet build` clean (no compilation impact — docs only).

**Verification:**
- [ ] `grep -n "Cited Answer" CONTEXT.md` returns a definition line.

**Dependencies:** None.

**Files likely touched:**
- `CONTEXT.md` (1 new entry, ~8 lines)

**Estimated scope:** XS (1 file)

---

### Remediation Task R3: Isolated `[Theory]` tests for 3 missing reranker signals

**Description:** `DeterministicRerankerTests.cs` already covers `RfcNumberMatchBonus`, `HeadingTermMatchBonus`, and `ObsoletedRfcPenalty` with isolated `[Theory]` data. Three signals lack isolated tests: `SectionMatchBonus` (+0.10, fires when a candidate's section id matches `QueryPlan.SectionReference`), `ProtocolRfcBonus` (+0.04, fires when a candidate's RFC number is in `QueryPlan.ProtocolSeedRfcs`), and `UpdatedByRelevanceBonus` (+0.06, fires when the query RFC number is updated-by the candidate RFC number). Each needs one [Theory] method with at minimum two `[InlineData]` rows: signal fires (expected score delta) and signal does not fire (no delta). Check the existing test structure (e.g., `Rerank_RfcNumberInPlan_AppliesOrNotAppliesBonus`) and mirror the pattern exactly.

**Acceptance criteria:**
- [ ] Three new `[Theory]` methods added, each with ≥2 `[InlineData]` rows covering fire/no-fire.
- [ ] Method naming follows `Rerank_{SignalName}_{Fires|DoesNotFire}_Applies{Bonus|NoDelta}` pattern (or mirrors exact pattern in the file).
- [ ] `dotnet test tests/RfcRag.Tests/ --filter "FullyQualifiedName~DeterministicReranker"` passes.

**Verification:**
- [ ] `dotnet test tests/RfcRag.Tests/ --filter "FullyQualifiedName~DeterministicRerankerTests"` passes with ≥3 new test methods.

**Dependencies:** None.

**Files likely touched:**
- `tests/RfcRag.Tests/UnitTests/DeterministicRerankerTests.cs` (~60 lines added)

**Estimated scope:** S (1 file)

---

### Remediation Task R4: Pipeline injection integration test + no-answer integration cases

**Description:** Two acceptance criteria from Task 14 are unmet: (a) "Pipeline test with scripted `FakeChatClient` asserting the hostile text only ever appears inside the evidence data block of the captured prompt" and (b) "≥ 3 no-answer integration cases pass." Add a new integration test class (or extend `AnswerQualityTests.cs`) using the existing Testcontainers fixture. For (a): index `rfc9998-injection.txt` in the test fixture, run a search query that retrieves it, run `AskService.AskAsync` with `FakeChatClient`, capture the chat messages via `FakeChatClient.CapturedCalls`, and assert that the hostile text appears only in the user message's data block (not the system message, and not as a directive). For (b): identify ≥3 `no_answer`-typed Golden Questions from `golden_questions.json` (q017, q018, q019 are available) and assert that the pipeline with a scripted `FakeChatClient` returning `noAnswer: true` passes the no-answer gate without citations.

Note: `rfc9998-injection.txt` is the correct fixture name (the plan said `rfc9998.txt` but the file was created as `rfc9998-injection.txt`; use the actual filename).

**Acceptance criteria:**
- [ ] Integration test indexes the injection fixture and asserts the FakeChatClient's captured system message does NOT contain the hostile instruction text ("IGNORE ALL PREVIOUS INSTRUCTIONS", "[SYSTEM:", etc.).
- [ ] Integration test asserts the hostile text IS present in the user/data portion of the captured call (confirming it arrived as data, not instruction).
- [ ] ≥ 3 no-answer test cases pass (scripted `FakeChatClient` + no-answer golden questions from the testdata subset).
- [ ] All existing integration tests still pass.

**Verification:**
- [ ] `dotnet test tests/RfcRag.Tests/ --filter "Category=Integration"` passes (requires Docker/Testcontainers).
- [ ] New test methods are visible in the output.

**Dependencies:** R1 (to ensure `QueryTraceWriter` is stable before adding pipeline tests that exercise it).

**Files likely touched:**
- `tests/RfcRag.Tests/IntegrationTests/PromptInjectionIntegrationTests.cs` (new, ~80 lines)

**Estimated scope:** S–M (1 new file, ~80 lines)

---

### Checkpoint — Complete

- [ ] `dotnet build -warnaserror` clean.
- [ ] `dotnet test tests/RfcRag.Tests/ --filter "Category!=Integration&Category!=RetrievalQuality&Category!=AnswerQuality"` all pass (includes new R1, R3 tests).
- [ ] `dotnet test tests/RfcRag.Tests/ --filter "Category=Integration"` passes (requires Docker — includes R4 tests).
- [ ] All four findings from the verification report resolved; report updated.

---

## Optional (Nice-to-have, not blocking)

These were flagged as Nice-to-have in the verification report. Implement only if time permits.

**N1 — Chat retry pattern:** Wrap `IChatClient.GetResponseAsync` in `AnswerGenerator.GenerateAsync` with an instance of `EmbeddingRetryPolicy` (or rename/generalize it) to give chat calls the same bounded-backoff resilience as embedding calls. Changes: `AnswerGenerator.cs` (1 new dependency), `AnsweringExtensions.cs` (pass retry policy). S scope, 2 files.

**N2 — ErrataLoader idempotent upsert integration test:** Add an integration test (extending `RfcRagIntegrationTests`) that loads a small errata fixture twice and asserts the row count is unchanged on the second load. XS scope, 1 file.

**N3 — Move `docs/plan/` content to `docs/plans/`:** The file `docs/plan/2026-06-13-verification-remediation-plan.md` was placed in a `docs/plan/` directory (singular) rather than `docs/plans/` (plural). Move or delete the stale file; the canonical location is `docs/plans/`. XS scope.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Testcontainers not available locally for R4 | Med | R4 test can be written; CI/Docker confirms it; local `make test-integration` to validate |
| `TimeProvider` not yet registered in DI | Low | Check `ServiceCollectionExtensions.AddRfcRagServices`; add `services.AddSingleton(TimeProvider.System)` if absent |
| Injection fixture indexing slow (large file) | Low | Fixture is 3 KB; indexes in milliseconds |
