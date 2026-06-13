# Remediation Plan: Production Improvements — Post-Verification Fixes

**Source**: `.omo/reports/2026-06-13-verification-report.md`  
**Date**: 2026-06-13  
**Scope**: Fixes for 1 blocker, 1 architecture issue, 3 important code issues, 3 test gaps, and 6 documentation drifts.

---

## Dependency Graph

```
Blockers ──┬── Task 1: Hostile test fixture (rfc9998)
           └── Task 2: ask_rfc conditional tool visibility

Code Quality ─┬── Task 3: Mutable ValidStatuses → ImmutableArray
              ├── Task 4: Sentinel pattern → explicit RfcSection.Empty
              ├── Task 5: Fire-and-forget trace → structured queue
              └── Task 6: Magic strings → constants

Test Gaps ─┬── Task 7: Errata edge case tests
            └── Task 8: Limit boundary tests

Docs ──┬── Task 9: README tool table (staged check)
       ├── Task 10: Architecture/schema docs
       ├── Task 11: CONTEXT.md sync
       ├── Task 12: MCP tool contract drift
       ├── Task 13: Eval docs schema
       └── Task 14: CHANGELOG.md

Recheck ── Task 15: Run tests and final verification

*Tasks 3, 4 are independent (parallel)*
*Tasks 7, 8 are independent (parallel)*
*Tasks 9–14 are independent (parallel)*
```

---

## Task List

### 🔴 Phase 1: Blockers

#### Task 1: Create hostile test fixture for prompt injection

**Description:** Create `tests/RfcRag.Tests/TestData/rfc9998-injection.md` containing RFC-formatted markdown with embedded prompt injection attempts (instruction overrides, system prompt leaks, delimiter escapes). Add a test that feeds it through the answering pipeline and asserts the injection is not honored.

**Acceptance criteria:**
- [ ] `tests/RfcRag.Tests/TestData/rfc9998-injection.md` exists with realistic injection attempts
- [ ] Test asserts the content is treated as data, not instructions
- [ ] Answering pipeline does not crash or echo the injection text as a command

**Verification:**
- [ ] `dotnet test --filter "Hostile|Injection"` passes
- [ ] Manual review of the fixture for comprehensiveness

**Dependencies:** None  
**Files likely touched:**
- `tests/RfcRag.Tests/TestData/rfc9998-injection.md` (new)
- `tests/RfcRag.Tests/UnitTests/*` (test file using fixture)

**Estimated scope:** Small (2 files)

---

#### Task 2: Gate `ask_rfc` tool registration behind answering module availability

**Description:** Currently MCP tools are registered via assembly scan, making `ask_rfc` visible even when `ChatModel`/`ChatProvider` aren't configured. Make the tool registration conditional — either check config at startup and skip registration, or make tool discovery aware of service registration state.

**Acceptance criteria:**
- [ ] `ask_rfc` does not appear in tool listing when answering module is disabled
- [ ] `ask_rfc` appears when answering module is enabled
- [ ] No other tool visibility is affected

**Verification:**
- [ ] Start app without ChatModel configured → `tools` response lacks `ask_rfc`
- [ ] Start app with ChatModel configured → `tools` response contains `ask_rfc`
- [ ] `dotnet test` — 377+ tests pass

**Dependencies:** None  
**Files likely touched:**
- `src/RfcRag/Program.cs`
- `src/RfcRag/Tools/RfcAskTools.cs`
- `src/RfcRag/Answering/AnsweringExtensions.cs`

**Estimated scope:** Medium (3 files)

---

### 🟡 Phase 2: Code Quality

#### Task 3: Replace mutable `ValidStatuses` with `ImmutableArray<string>`

**Description:** `RfcErratum.cs` has `public static readonly string[] ValidStatuses` — `readonly` on an array only protects the reference, not contents. Switch to `ImmutableArray<string>` (or `FrozenSet<string>` for O(1) lookup if used for `Contains`).

**Acceptance criteria:**
- [ ] `ValidStatuses` is immutable (no caller can mutate entries)
- [ ] All existing usage sites compile and work
- [ ] No unnecessary allocations at access time

**Verification:**
- [ ] `dotnet build` clean
- [ ] `dotnet test` passes
- [ ] LSP diagnostics clean on changed files

**Dependencies:** None  
**Files likely touched:**
- `src/RfcRag/Models/RfcErratum.cs`

**Estimated scope:** XS (1 file)

**Parallel with:** Task 4

---

#### Task 4: Replace sentinel pattern with explicit `RfcSection.Empty`

**Description:** `SearchRepository.cs` returns `(new RfcSection(), [])` as a sentinel for "not found". The consumer in `RfcRagTools.cs` checks `.Length == 0` — an implicit contract. Add a static `RfcSection.Empty` sentinel and use it, or change the return type to `RfcSection?` / `RfcSection?` with null. Prefer `RfcSection.Empty` as it's the clearest path.

**Acceptance criteria:**
- [ ] `RfcSection.Empty` static property exists and documents the sentinel contract
- [ ] SearchRepository returns `RfcSection.Empty` instead of `new RfcSection()`
- [ ] Consumer checks `== RfcSection.Empty` or `.IsEmpty` instead of `.Length == 0`
- [ ] No behavioral changes

**Verification:**
- [ ] `dotnet build` clean
- [ ] `dotnet test` passes
- [ ] All sentinel consumption sites updated

**Dependencies:** None  
**Files likely touched:**
- `src/RfcRag/Models/RfcSection.cs`
- `src/RfcRag/Search/SearchRepository.cs`
- `src/RfcRag/Tools/RfcRagTools.cs`

**Estimated scope:** Small (3 files)

**Parallel with:** Task 3

---

#### Task 5: Replace fire-and-forget trace write with structured background queue

**Description:** `AskService.cs` writes traces via `_ = traceWriter.WriteAsync(...)` — exceptions are silently swallowed and the cancellation token is consumed by a task that outlives the request. Await the call if latency is acceptable (trace write should be fast), or use `Channel<QueryTrace>` + a background consumer with error logging.

**Acceptance criteria:**
- [ ] Trace exceptions are not silently swallowed
- [ ] Trace writing doesn't add latency to the response path (if background)
- [ ] Tests exist for the error path

**Verification:**
- [ ] `dotnet build` clean
- [ ] `dotnet test` passes
- [ ] LSP diagnostics clean

**Dependencies:** None  
**Files likely touched:**
- `src/RfcRag/Answering/AskService.cs`
- Potentially `src/RfcRag/Infrastructure/QueryTraceWriter.cs`

**Estimated scope:** Small–Medium (2–3 files)

---

#### Task 6: Extract magic strings into constants

**Description:** Inline stage names ("Creating", "Reranking"), evaluation dimensions, and error messages in `CitationVerifier.cs`, `AnswerEvaluationMetrics.cs`, `AskService.cs` should be `private const` fields.

**Acceptance criteria:**
- [ ] No bare string literals for stage names or error messages
- [ ] Constants scoped appropriately (private in class, or internal if shared)

**Verification:**
- [ ] `dotnet build` clean
- [ ] `dotnet test` passes

**Dependencies:** None  
**Files likely touched:**
- `src/RfcRag/Answering/CitationVerifier.cs`
- `src/RfcRag/Evaluation/AnswerEvaluationMetrics.cs`
- `src/RfcRag/Answering/AskService.cs`

**Estimated scope:** Small (3 files)

**Parallel with:** Tasks 7, 8, 9–14

---

### 🟡 Phase 3: Test Gaps

#### Task 7: Add errata loading edge case tests

**Description:** Errata loading (missing file, invalid JSON, non-array root) should be tested. Each edge case should produce a graceful skip (logged warning, no crash). Add parameterized tests.

**Acceptance criteria:**
- [ ] Missing errata file → graceful skip
- [ ] Invalid JSON in errata file → graceful skip
- [ ] Non-array root JSON → graceful skip
- [ ] All edge cases produce appropriate log output

**Verification:**
- [ ] New tests pass: `dotnet test --filter "Errata"`
- [ ] No behavioral change to success path

**Dependencies:** None  
**Files likely touched:**
- `tests/RfcRag.Tests/UnitTests/*` (errata tests)
- Possibly `src/RfcRag/Indexing/ErrataLoader.cs` if error handling needs improvement

**Estimated scope:** Small (2 files)

**Parallel with:** Task 8, 6, 9–14

---

#### Task 8: Add limit boundary tests for SearchRepository

**Description:** SearchRepository clamps limit to `[1, MaxLimit]` but tests only use nominal values. Add parameterized tests for 0, negative, 1, MaxLimit, MaxLimit+1.

**Acceptance criteria:**
- [ ] Limit=0 → clamped to 1
- [ ] Limit=-5 → clamped to 1
- [ ] Limit=1 → used as-is
- [ ] Limit=MaxLimit → used as-is
- [ ] Limit=MaxLimit+1 → clamped to MaxLimit

**Verification:**
- [ ] New tests pass: `dotnet test --filter "Limit|Boundary"`
- [ ] No behavioral change

**Dependencies:** None  
**Files likely touched:**
- `tests/RfcRag.Tests/UnitTests/SearchServiceTests.cs` (or similar)

**Estimated scope:** XS (1 file)

**Parallel with:** Task 7, 6, 9–14

---

### 🟡 Phase 4: Documentation

#### Task 9: Verify staged README changes include `ask_rfc` tool row

**Description:** Staged changes update tool count from 11→12. Verify the `ask_rfc` tool row was actually added. If not, add it.

**Acceptance criteria:**
- [ ] README tool table has 12 rows
- [ ] `ask_rfc` appears with description, input params, and output
- [ ] Tool count text matches actual count

**Verification:**
- [ ] `grep -c "| \`" README.md` == 12
- [ ] `grep "ask_rfc" README.md` returns the row

**Dependencies:** None  
**Files likely touched:**
- `README.md`

**Estimated scope:** XS (1 file)

**Parallel with:** Tasks 10–14

---

#### Task 10: Update architecture/schema docs for new modules

**Description:** The answering pipeline, index_manifest, and rfc_errata modules are undocumented in architecture overviews. Add them to the relevant docs/ architecture doc or ADR index.

**Acceptance criteria:**
- [ ] Answering pipeline described in architecture docs
- [ ] Index manifest migration documented
- [ ] Errata migration documented

**Verification:**
- [ ] A reader unfamiliar with the codebase can learn about these modules from docs

**Dependencies:** None  
**Files likely touched:**
- `docs/` (architecture doc)
- potentially ADR index

**Estimated scope:** Small (2 files)

**Parallel with:** Tasks 9, 11–14

---

#### Task 11: Sync CONTEXT.md with actual trace schema

**Description:** CONTEXT.md claims manifest ID in traces, but `QueryTrace` has no manifest field. Either remove the claim or add the manifest field to the trace.

**Acceptance criteria:**
- [ ] CONTEXT.md accurately describes trace contents
- [ ] No claims about fields that don't exist

**Verification:**
- [ ] Cross-reference CONTEXT.md trace section against `QueryTrace` record

**Dependencies:** None  
**Files likely touched:**
- `CONTEXT.md`
- Potentially `QueryTrace.cs` if adding field

**Estimated scope:** XS (1 file)

**Parallel with:** Tasks 9, 10, 12–14

---

#### Task 12: Sync MCP tool contract docs with actual output

**Description:** `src/RfcRag/README.md` documents tool contracts. `search_rfc` result schema is missing the `id` field, `rfc_stats` is missing the `errata` field. Sync.

**Acceptance criteria:**
- [ ] `search_rfc` result docs include `id`
- [ ] `rfc_stats` result docs include `errata`

**Verification:**
- [ ] Cross-reference against actual tool output

**Dependencies:** None  
**Files likely touched:**
- `src/RfcRag/README.md`

**Estimated scope:** XS (1 file)

**Parallel with:** Tasks 9, 10, 11, 13, 14

---

#### Task 13: Add `includeObsolete` to eval golden_questions schema docs

**Description:** `docs/eval/README.md` documents the golden_questions.json schema but omits the `includeObsolete` field that exists in actual data. Add it.

**Acceptance criteria:**
- [ ] `includeObsolete` documented with type, purpose, and default value

**Verification:**
- [ ] Cross-reference against actual `golden_questions.json`

**Dependencies:** None  
**Files likely touched:**
- `docs/eval/README.md`

**Estimated scope:** XS (1 file)

**Parallel with:** Tasks 9–12, 14

---

#### Task 14: Update CHANGELOG.md

**Description:** CHANGELOG.md says "ten MCP tools". Now there are 12 tools plus errata and answering capabilities. Revise.

**Acceptance criteria:**
- [ ] CHANGELOG reflects current tool count (12)
- [ ] New capabilities (errata, answering, eval) mentioned

**Verification:**
- [ ] CHANGELOG entry exists for the production improvements

**Dependencies:** None  
**Files likely touched:**
- `CHANGELOG.md`

**Estimated scope:** XS (1 file)

**Parallel with:** Tasks 9–13

---

### ✅ Phase 5: Final

#### Task 15: Run full test suite and final verification

**Description:** After all fixes, run the full test suite, verify build is clean, and confirm all acceptance criteria are met.

**Acceptance criteria:**
- [ ] `dotnet build` passes with no errors
- [ ] `dotnet test --filter "Category!=Integration"` passes (377+ tests)
- [ ] LSP diagnostics clean on all changed files

**Verification:**
- [ ] Build + test output captured

**Dependencies:** All prior tasks  
**Files likely touched:** None  
**Estimated scope:** Verification only

---

## Checkpoints

### Checkpoint 1: After Phase 1 (Blockers)
- [ ] Hostile fixture exists and test passes
- [ ] `ask_rfc` hidden when answering disabled
- [ ] `dotnet test` still passes

### Checkpoint 2: After Phases 2–3 (Code Quality + Tests)
- [ ] All code quality fixes applied and building
- [ ] New tests added and passing
- [ ] `dotnet test` passes

### Checkpoint 3: After Phase 4 (Docs)
- [ ] All documentation drifts resolved
- [ ] CONTEXT.md, CHANGELOG.md, READMEs accurate

### Final Checkpoint
- [ ] Full test suite passes
- [ ] Build clean
- [ ] All acceptance criteria met

---

## Parallel Execution Plan

| Batch | Tasks | Runner | Rationale |
|-------|-------|--------|-----------|
| Batch 1 | Task 3, Task 4 | Parallel | Both are 1–3 file code quality fixes, independent |
| Batch 2 | Task 7, Task 8 | Parallel | Both are test additions, independent |
| Batch 3 | Tasks 9–14 | Parallel | All documentation fixes, independent |
| Sequential | Task 1 | Single | Novel test fixture needs careful design |
| Sequential | Task 2 | Single | Conditional registration affects startup path |
| Sequential | Task 5 | Single | Fire-and-forget fix may need Channel setup |
| Sequential | Task 6 | Single | Simple but touches 3 files |
| Final | Task 15 | Single | Verification after all fixes |

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Task 2 (tool gating) changes tool listing behavior | Medium | Document the change; verify both configurations |
| Task 4 (sentinel) has multiple consumption sites | Low | `grep` for `.Length == 0` pattern; update all |
| Task 5 (fire-and-forget) introduces threading | Medium | Use `Channel<QueryTrace>` with single consumer; test concurrency |
| Parallel doc fixes may overlap same file | Low | Tasks 9–14 target distinct files (verify before dispatch) |
