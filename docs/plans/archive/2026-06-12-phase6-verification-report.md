# Verification Report: Phase 6 — Deterministic Reranker (Tasks 19 & 20)

**Date:** 2026-06-12
**Source plan:** [2026-06-11-production-improvements-implementation-plan.md](./2026-06-11-production-improvements-implementation-plan.md)

---

## Completeness

| Plan item | Status | Evidence |
|---|---|---|
| T19: `DeterministicReranker.cs` (6 named signals, static Rerank) | Done | `src/RfcRag/Search/DeterministicReranker.cs` — 125 lines, 6 constants, static |
| T19: `HybridCandidate.cs` record | Done | `src/RfcRag/Search/HybridCandidate.cs:3` |
| T19: `SearchHybridWideCandidatesAsync` returning 4× with arm ranks + RRF | Done | `src/RfcRag/Search/SearchRepository.cs:117` — `candidateLimit = normalizedLimit * 4` |
| T19: Existing `SearchHybridAsync` untouched for non-opted callers | Done | `src/RfcRag/Search/SearchService.cs:50` — else branch calls unchanged method |
| T19: `[Theory]` tests per signal (fires / doesn't fire) | Partial | HeadingTermMatch and ObsoletePenalty covered both ways; SectionMatch, ProtocolRfc, UpdatedByRelevance have positive-only `[Fact]` tests |
| T19: `DeterministicRerankerTests.cs` | Done | 14 tests pass |
| T19: Obsolete penalty suppressed when `IncludeObsolete` | Done | `DeterministicRerankerTests.cs:200` — `Rerank_IncludeObsolete_SuppressesObsoletePenalty` |
| T20: `RerankerEnabled` flag in `RfcRagOptions` | Done | `src/RfcRag/Settings/RfcRagOptions.cs:104` |
| T20: Pipeline wired in `SearchService.SearchAsync` | Done | `src/RfcRag/Search/SearchService.cs:32` |
| T20: `RerankerEnabled=false` restores pre-Phase-6 behavior | Done | `src/RfcRag/Search/SearchService.cs:50-55` |
| T20: `docs/adr/0007-deterministic-first-reranking.md` | Partial | ADR written with signal weights; "measured numbers" (eval comparison) absent |
| T20: RetrievalQuality thresholds hold | Done | 14 tests pass; baseline committed at `docs/eval/reports/baseline-testdata.json` |
| Checkpoint 6: Both flags in `docs/configuration.md` | Done | Lines 75–76: `QueryPlannerEnabled` and `RerankerEnabled` both documented |

**Scope drift:** None. `src/RfcRag/Search/MetadataRepository.cs` not staged — `GetRelationsBatchAsync` already existed from Task 8.

---

## Findings

### Important

- **SectionMatchBonus missing negative case** — `tests/RfcRag.Tests/UnitTests/DeterministicRerankerTests.cs:129`
  > `public void Rerank_SectionReferenceInPlan_AppliesSectionBonus()`
  Task 19 AC requires `[Theory]` with "fires / doesn't fire" per signal. This test only covers the fire case (section "9.3.1" matches). No `[InlineData]` for a candidate whose section is NOT in the plan.

- **ProtocolRfcBonus missing negative case** — `tests/RfcRag.Tests/UnitTests/DeterministicRerankerTests.cs:144`
  > `public void Rerank_ProtocolRfcInPlan_AppliesProtocolBonus()`
  Same gap — fire case only, no negative `[Theory]` covering an RFC number absent from the protocol seed set.

- **UpdatedByRelevanceBonus missing negative case** — `tests/RfcRag.Tests/UnitTests/DeterministicRerankerTests.cs:215`
  > `public void Rerank_SuccessorObsoletesQueryRfc_AppliesUpdatedByRelevanceBonus()`
  Fire case only. Missing: candidate that does NOT obsolete/update any query-plan RFC should get no bonus.

- **ADR-0007 lacks measured eval numbers** — `docs/adr/0007-deterministic-first-reranking.md:22`
  > `The retrieval quality gate (RetrievalQualityTests) guards against silent regression`
  Task 20 AC: "ADR recorded: deterministic-first reranking, with measured numbers." No before/after delta or eval comparison is present in the ADR. The committed baseline (`docs/eval/reports/baseline-testdata.json`) shows `hitAt10=0.917`, `MRR=0.762`, `nDCG@10=0.800` but is not referenced. Recommend either citing it in the ADR or updating the wording to "thresholds held."

- **Magic number for candidate expansion factor** — `src/RfcRag/Search/SearchRepository.cs:117`
  > `int candidateLimit = normalizedLimit * 4;`
  The `4` multiplier appears twice (lines 117 and 229) without a named constant. All signal weights are named constants in `DeterministicReranker.cs`; this multiplier should follow the same pattern.

### Nice-to-have

- **Magic minimum term length** — `src/RfcRag/Search/DeterministicReranker.cs:120`
  > `if (word.Length >= 3)`
  Should be extracted as `private const int MinQueryTermLength = 3` for consistency with the named-constant pattern applied to weights.

- **`Rerank` is `public` on `internal` class** — `src/RfcRag/Search/DeterministicReranker.cs:15`
  > `public static IReadOnlyList<SearchResult> Rerank(`
  The class is `internal static`, making `public` redundant. `internal static` would be consistent with other utility classes in the repo.

- **`normativeKeyword` filtering untested in wide-candidate method** — `tests/RfcRag.Tests/UnitTests/SearchRepositoryTests.cs:105-143`
  Both new `SearchRepositoryTests` pass `normativeKeyword: null`. The SQL conditional for normative keyword filtering has no coverage with a non-null keyword in the wide-candidate path.

- **Configuration table column inconsistency (pre-existing)** — `docs/configuration.md:59-61`
  Rows 59–61 and 77 are missing the "Valid Range / Values" column. The two Phase 6 rows (75–76) are correctly formatted. Pre-existing issue, not introduced by this change.

---

## Codegraph impact

- `SearchHybridAsync` — callers: `SearchService.cs:50` (unchanged else-branch). All updated. No unupdated callers.
- `HybridCandidate` — referenced only within `Search/`. No coupling leakage.
- `RfcRelationsBatch.Updates` — confirmed present at `src/RfcRag/Models/RfcRelationsBatch.cs:14`. Reranker correctly uses `.Obsoletes` and `.Updates` for UpdatedBy signal (`DeterministicReranker.cs:104-108`).

---

## Tests

| Tier | Result |
|---|---|
| Unit — `DeterministicRerankerTests` | 14 passed |
| Integration — `SearchRepositoryTests` (Testcontainers PostgreSQL) | 7 passed (includes 2 new wide-candidate tests) |
| RetrievalQuality gate — `Category=RetrievalQuality` | 14 passed (thresholds held) |
| `dotnet build -warnaserror` | Clean |

---

## Behavioral check

RetrievalQuality gate passes with reranker enabled (default). Baseline committed at `docs/eval/reports/baseline-testdata.json`: `hitAt10=0.917`, `MRR=0.762`, `nDCG@10=0.800`, stamped to manifest `384aeece-ae5a-4042-90d0-3dfb00725594`.

A/B comparison (flag on vs. off) not run — requires local `make eval`. Not attached to this verification.

`--cli` behavioral check not run — requires a fully indexed PostgreSQL mirror.

---

## Acceptance criteria

**Task 19:**
- [x] Candidate query returns ≥ 4× requested limit with arm ranks + RRF score — `SearchRepository.cs:117`
- [x] Existing `SearchHybridAsync` behavior untouched for callers not opted in — `SearchService.cs:50`
- [ ] Each signal has isolated `[Theory]` tests (fires / doesn't fire) — SectionMatch, ProtocolRfc, UpdatedByRelevance missing negative cases
- [x] Weights in named constants in one place — `DeterministicReranker.cs:5-10`
- [x] Obsolete penalty suppressed when `IncludeObsolete` — `DeterministicRerankerTests.cs:200`

**Task 20:**
- [x] Pipeline order: plan → retrieve wide → rerank → top-k — `SearchService.cs:32-56`
- [x] Flag off restores pre-Phase-6 behavior byte-for-byte — `SearchService.cs:50-55`
- [x] RetrievalQuality thresholds hold — 14 tests pass
- [ ] ADR recorded with measured numbers — ADR present but no eval delta documented
- [x] Both flags documented in `docs/configuration.md` — lines 75–76

---

## Recommendation

**Remediation needed** before marking Phase 6 complete. Two acceptance criteria are unmet:

1. Three signals (SectionMatch, ProtocolRfc, UpdatedByRelevance) need negative-case `[Theory]` tests — Task 19 AC.
2. ADR-0007 needs measured eval numbers or the wording revised to reference committed baseline thresholds — Task 20 AC.

The `4` multiplier extraction (magic number) is an Important code-standards fix. Everything else — architecture, SQL fusion boundary, backward compatibility, build, eval gate — is solid.
