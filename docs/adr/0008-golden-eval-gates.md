# Golden Eval Decision Gates

`make eval-answers` runs a golden evaluation suite that computes four retrieval metrics: WarningRate, ObsoleteCitationRate, CitationRate, and ClaimSupportRate. The evaluation results are printed to the console in a tabular report. No pass-fail gate is enforced — the report is informational, intended for human review during development.

## Considered Options

- **No eval at all (manual inspection only)** — fastest path, no test maintenance, but inconsistent across contributors and produces no repeatable quality signal.
- **Console eval output only (current)** — fast execution, human-in-the-loop for interpreting regressions, no CI gate to tune or maintain. The report format is machine-parseable (JSON lines) for post-processing if needed later.
- **CI gate with hard thresholds** — enforces automatic pass-fail, catches regressions before merge, but needs a representative evaluation set and stable baseline first to avoid brittle thresholds.

Console eval is the right choice for early development. A CI gate is deferred until the evaluation set is representative and thresholds have been validated against real regressions.
