# Remediation Plan: Phase 4 Answer Evaluation

Source plan: [2026-06-11-production-improvements-implementation-plan.md](./2026-06-11-production-improvements-implementation-plan.md)

Source verification: read-only verification of staged Phase 4 implementation on 2026-06-11.

## Goal

Bring Phase 4, Answer Evaluation, into alignment with Tasks 15 and 16 of the production improvements implementation plan.

The verified staged implementation is close enough to keep, but it is not complete. The highest-risk gaps are:

- Answer eval emits a standalone answer report instead of a combined retrieval-plus-answer report.
- Answer eval output is not manifest-stamped.
- `make eval-answers` runs only the fake CI harness instead of the real-model answer eval path.
- Quote faithfulness measures non-empty quote text instead of verbatim support in cited evidence.
- Obsolete citation rate is not populated by the CLI runner.
- AnswerQuality tests assert computability rather than expected scoring behavior.

## Assumptions

- Keep Task 15 citation precision and recall semantics as RFC-level metrics against `mustCite` and cited RFCs.
- Treat quote faithfulness as the grounded citation metric: quoted citation text must verbatim-match the cited Evidence Section text.
- Fix `CONTEXT.md` to match the metric split instead of redefining citation precision as grounded quote validity.
- Preserve existing retrieval eval JSON shape and add answer eval through `RetrievalEvalReport.AnswerEval`.
- Keep generation optional. CI continues to use `FakeChatClient`; real-model answer evaluation is a local command.

## Phase 1: Contract Tests First

### Task 1: Add failing answer eval contract tests

**Description:** Add tests that encode the Phase 4 contract before changing implementation. Cover the combined report shape, manifest stamping, answer-mode limit propagation, and meaningful AnswerQuality assertions.

**Acceptance criteria:**

- A test proves answer eval mode emits a `RetrievalEvalReport` with `answerEval` populated.
- A test proves the answer eval report includes `manifestId`, `embeddingModel`, and `parserType`.
- A test proves `--limit` affects the answer eval `AskAsync` limit.
- `ScriptedAnswer_q001_ProducesCitationPrecisionAndRecall` asserts the expected `1.0` precision, recall, and F1 values.

**Verification:**

```bash
dotnet test tests/RfcRag.Tests/RfcRag.Tests.csproj --filter "FullyQualifiedName~AnswerQuality|FullyQualifiedName~EvalCommand"
```

**Dependencies:** None

**Files likely touched:**

- `tests/RfcRag.Tests/IntegrationTests/AnswerQualityTests.cs`
- New or existing CLI eval tests under `tests/RfcRag.Tests/UnitTests/`

**Estimated scope:** M

## Phase 2: Report Shape and Runner Behavior

### Task 2: Emit combined retrieval and answer eval reports

**Description:** Change answer eval mode so it produces the Task 16 report contract: retrieval metrics and answer metrics in one manifest-stamped report. Use `RetrievalEvalReport.AnswerEval` rather than emitting a standalone `AnswerEvalReport`.

**Acceptance criteria:**

- `--eval <golden-questions-file-path> --answers` runs retrieval eval and answer eval for the selected corpus.
- The emitted JSON is a `RetrievalEvalReport` with `answerEval` populated.
- Manifest fields are populated from `IndexingRepository.GetLatestManifestAsync`.
- `--answer` may remain as a backward-compatible alias, but `--answers` is the documented canonical flag.

**Verification:**

```bash
dotnet test tests/RfcRag.Tests/RfcRag.Tests.csproj --filter "FullyQualifiedName~EvalCommand"
```

**Dependencies:** Task 1

**Files likely touched:**

- `src/RfcRag/Cli/CliCommandRouter.cs`
- `src/RfcRag/Cli/EvalCommand.cs`
- `src/RfcRag/Evaluation/EvalModels.cs`

**Estimated scope:** M

### Task 3: Propagate answer eval limit

**Description:** Ensure the eval CLI's `--limit` value is respected in answer eval mode, not only retrieval eval mode.

**Acceptance criteria:**

- `--eval ... --answers --limit N` passes `N` into the ask pipeline for each golden question.
- Usage text and docs describe the same behavior.
- Existing retrieval eval limit behavior is unchanged.

**Verification:**

```bash
dotnet test tests/RfcRag.Tests/RfcRag.Tests.csproj --filter "FullyQualifiedName~EvalCommand"
```

**Dependencies:** Task 2

**Files likely touched:**

- `src/RfcRag/Cli/CliCommandRouter.cs`
- `src/RfcRag/Cli/EvalCommand.cs`
- CLI eval tests

**Estimated scope:** S

## Phase 3: Metric Correctness

### Task 4: Make quote faithfulness evidence-backed

**Description:** Replace the shallow "has RelevantText" metric with deterministic verbatim support against the cited Evidence Section text. Reuse the existing citation-discipline behavior or pass a typed evidence-backed evaluation input rather than duplicating citation matching logic.

**Acceptance criteria:**

- A citation counts as quote-faithful only when its `RelevantText` appears verbatim in the cited Evidence Section.
- Missing evidence ids, empty quotes, and non-verbatim quotes count against quote faithfulness.
- Unit tests cover perfect, partial, and degenerate quote-faithfulness cases.

**Verification:**

```bash
dotnet test tests/RfcRag.Tests/RfcRag.Tests.csproj --filter "FullyQualifiedName~AnswerEvaluationMetrics"
```

**Dependencies:** Task 1

**Files likely touched:**

- `src/RfcRag/Evaluation/AnswerEvaluationMetrics.cs`
- `tests/RfcRag.Tests/UnitTests/AnswerEvaluationMetricsTests.cs`
- Possibly a small typed evaluation input model under `src/RfcRag/Evaluation/`

**Estimated scope:** M

### Task 5: Populate obsolete citation rate in answer eval runner

**Description:** Supply obsolete RFC data when the CLI runner evaluates answers so `AvgObsoleteCitationRate` is meaningful in generated reports.

**Acceptance criteria:**

- The answer eval runner passes obsolete RFC information into `AnswerEvaluationMetrics.Evaluate`.
- A controlled runner-level test proves obsolete citation rate is nonzero when an answer cites an obsolete RFC.
- No hardcoded obsolete list is embedded in metric functions.

**Verification:**

```bash
dotnet test tests/RfcRag.Tests/RfcRag.Tests.csproj --filter "ObsoleteCitation"
```

**Dependencies:** Task 2

**Files likely touched:**

- `src/RfcRag/Cli/EvalCommand.cs`
- `src/RfcRag/Evaluation/AnswerEvaluationMetrics.cs`
- Unit or CLI eval tests

**Estimated scope:** S-M

### Task 6: Align no-answer metric naming and semantics

**Description:** Decide whether the metric is no-answer recall for refusal-required items or full no-answer classification accuracy. Then make the code, report field name, tests, and docs agree.

**Acceptance criteria:**

- If the metric remains refusal-required only, rename or document it clearly as no-answer/refusal accuracy for `answerType == "no_answer"`.
- If the metric becomes full classification accuracy, factual and normative false refusals are included as failures.
- Aggregate tests cover false positives and false negatives.

**Verification:**

```bash
dotnet test tests/RfcRag.Tests/RfcRag.Tests.csproj --filter "FullyQualifiedName~AnswerEvaluationMetrics"
```

**Dependencies:** Task 4

**Files likely touched:**

- `src/RfcRag/Evaluation/AnswerEvaluationMetrics.cs`
- `src/RfcRag/Evaluation/EvalModels.cs`
- `tests/RfcRag.Tests/UnitTests/AnswerEvaluationMetricsTests.cs`
- `CONTEXT.md`

**Estimated scope:** S

## Phase 4: Local Eval Command and Documentation

### Task 7: Fix `make eval-answers`

**Description:** Make `make eval-answers` run the real-model answer eval path described by Task 16. Keep the fake CI harness available through the test filter, but do not present it as the local model-quality eval.

**Acceptance criteria:**

- `make eval-answers` invokes the CLI answer eval path.
- Required local configuration is documented or inherited from existing config docs.
- Fake `Category=AnswerQuality` tests remain available and documented as CI harness validation.

**Verification:**

```bash
make eval-answers
```

**Dependencies:** Tasks 2 and 3

**Files likely touched:**

- `Makefile`
- `docs/eval/README.md`
- `tests/RfcRag.Tests/README.md`

**Estimated scope:** S

### Task 8: Reconcile docs and glossary with implemented contracts

**Description:** Update documentation to reflect the final answer eval command, report shape, and metric semantics.

**Acceptance criteria:**

- `docs/eval/README.md` documents `--answers`, `make eval-answers`, and the distinction between CI harness and real-model eval.
- `CONTEXT.md` defines citation precision, citation recall, quote faithfulness, obsolete citation rate, and no-answer metric semantics without contradiction.
- `tests/RfcRag.Tests/README.md` describes the AnswerQuality suite as harness validation, not a real-model score.

**Verification:**

```bash
rg -n -- "--answer|--answers|eval-answers|Citation Precision|Quote Faithfulness|No-answer" docs tests src Makefile
```

**Dependencies:** Tasks 4, 6, and 7

**Files likely touched:**

- `docs/eval/README.md`
- `CONTEXT.md`
- `tests/RfcRag.Tests/README.md`

**Estimated scope:** S

## Checkpoint: Phase 4 Remediation Complete

Run the following before re-verifying:

```bash
dotnet test tests/RfcRag.Tests/RfcRag.Tests.csproj --filter "FullyQualifiedName~AnswerEvaluationMetrics"
dotnet test tests/RfcRag.Tests/RfcRag.Tests.csproj --filter "Category=AnswerQuality" --no-restore
dotnet test RfcRag.slnx --filter "Category!=Integration&Category!=LiveApi"
make eval-answers
```

Then rerun read-only implementation verification against Phase 4.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Combined report logic duplicates retrieval eval work unnecessarily | Medium | Share the existing retrieval eval path and attach answer eval as an additive report section. |
| Quote-faithfulness needs Evidence Pack text that `GeneratedAnswer` does not currently expose | High | Add a small eval-specific typed result or reuse citation-discipline output rather than widening public tool contracts prematurely. |
| `make eval-answers` cannot pass in every required local secret/config value | Medium | Document required configuration and use existing app configuration conventions. |
| Obsolete RFC data source is not yet convenient for eval | Medium | Use existing metadata/search repository behavior; keep metric functions pure and pass obsolete ids in from the runner. |

## Open Questions

- Should `--answer` remain as an alias for `--answers`, or should it be removed before this becomes a documented contract?
- Should no-answer accuracy mean recall on `answerType == "no_answer"` only, or full classification accuracy including false refusals on factual/normative items?
- Should answer eval reports be written to a gitignored file by default, printed to stdout, or both?
