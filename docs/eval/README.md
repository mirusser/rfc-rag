# Evaluation Datasets and Reports

This directory contains the evaluation data and tooling for the rfc-rag retrieval and answering pipeline.

## Files

| File | Purpose |
|------|---------|
| `retrieval_queries.json` | Legacy seed queries (RFC-level only, no section expectations). Used by `--benchmark`. |
| `golden_questions.json` | Golden dataset for the eval harness (section-level expectations, corpus markers). Used by `--eval`. |
| `reports/baseline-testdata.json` | Committed retrieval baseline over the TestData corpus (6 RFCs). Gate for ranking changes. |

## `golden_questions.json` Schema

Each item in `golden_questions.json` has the following fields:

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Stable item identifier (e.g. `"q001"`). Never reuse after deletion. |
| `question` | string | The natural-language question to evaluate. |
| `expectedRfcs` | int[] | RFC numbers that SHOULD appear in top-k results. Empty means no expectation. |
| `expectedSections` | string[] | Section ids that SHOULD appear (e.g. `"9.3.1"`). Section match requires correct RFC. |
| `mustCite` | int[] | For answer eval: RFC numbers that MUST appear in citations. |
| `shouldNotCite` | int[] | For answer eval: RFC numbers that MUST NOT appear in citations. |
| `answerType` | string | One of `"normative_explanation"`, `"factual"`, or `"no_answer"`. |
| `includeErrata` | bool | Optional; when true, answer eval calls `ask_rfc` with errata enrichment enabled. |
| `errataStatus` | string | Optional errata status filter for `includeErrata` questions; defaults to `"verified"`. |
| `corpus` | string | `"testdata"` for questions answerable from the 6 TestData RFCs; `"full"` for full-mirror items. |

### `corpus` marker semantics

- `"testdata"` — the question is answerable from the TestData fixtures (RFC 2119, 3986, 8446, 9000, 9110, 9999). These items run in CI against the in-memory Testcontainers database.
- `"full"` — the question requires the full RFC mirror. These items are skipped in CI and only run via `make eval`.

### `answerType` semantics

- `"normative_explanation"` — the question asks about a MUST/SHOULD/MAY requirement.
- `"factual"` — the question asks for a factual detail from an RFC.
- `"no_answer"` — the question cannot be answered from the indexed corpus (unanswerable or out-of-scope).

## Running Evaluations

### Retrieval eval (testdata corpus)

```bash
dotnet test tests/RfcRag.Tests/ --filter "Category=RetrievalQuality"
```

### Full retrieval eval (requires indexed mirror)

```bash
make eval
```

### Answer eval (requires chat model configuration)

```bash
make eval-answers
```

`make eval-answers` runs the real CLI path:

```bash
dotnet run --project src/RfcRag/ -- --eval docs/eval/golden_questions.json --answers --corpus all
```

The answer-eval JSON is a manifest-stamped `RetrievalEvalReport` with normal retrieval metrics plus an `answerEval` section. The `--answers` flag is canonical; `--answer` is kept as a compatibility alias. `--limit N` controls both retrieval eval depth and the ask pipeline search limit.

Before running local answer eval, load the same configuration used by the CLI server: at minimum `RfcRag__RfcMirrorPath`, `RfcRag__PostgresConnectionString`, and chat model/provider credentials. See [configuration.md](../configuration.md).

The `Category=AnswerQuality` tests are a fake-chat CI harness for answer verification behavior. They are not a real-model score:

```bash
dotnet test tests/RfcRag.Tests/ --filter "Category=AnswerQuality" --no-restore
```

## Baseline Reports

The committed baseline (`reports/baseline-testdata.json`) is the reference for the testdata-corpus retrieval metrics. It is updated only when metrics improve. Full-corpus and answer reports are gitignored.
