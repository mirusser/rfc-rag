# RFC RAG

Local RAG MCP server that indexes a mirror of IETF RFCs into PostgreSQL (pgvector + full-text search) and serves section-level retrieval tools to AI agents.

## Language

**RFC**:
An IETF document identified by its number (e.g. RFC 9293).
_Avoid_: spec, standard (not all RFCs are standards)

**Mirror**:
The local directory of RFC files that indexing reads from; nothing is fetched from the internet at query time.
_Avoid_: corpus, archive

**Source**:
The single file an RFC is parsed from — exactly one per RFC number per indexing run (TXT canonical, XML fallback; see ADR-0001).
_Avoid_: input file, source file (ambiguous with C# sources)

**Section**:
The unit of retrieval — a heading-delimited region of an RFC, identified by section number (e.g. `3.7.1`) or appendix letter (see ADR-0002).
_Avoid_: chunk, passage, paragraph

**Indexed RFC**:
The record that an RFC's Source has been parsed and stored, keyed by content hash so unchanged Sources are skipped on re-runs.

**ABNF Block**:
A contiguous run of ABNF grammar rules extracted from a Section.
_Avoid_: grammar snippet

**Normative Keyword**:
One of the BCP 14 requirement words (MUST, MUST NOT, SHOULD, SHALL, MAY, …), recognized only in uppercase.

**Normative Occurrence**:
A lexical match of a Normative Keyword inside a Section — a high-precision heuristic, *not* a claim that the RFC formally adopts BCP 14 (pre-2119 RFCs like 793 count too).
_Avoid_: binding requirement (overclaims), requirement keyword hit

**Erratum**:

An RFC Editor correction or report loaded from a local `errata.json` snapshot. Errata are optional evidence enrichments keyed by RFC number and Section, filtered by status (`verified`, `held_for_document_update`, or `reported`). Verified errata produce evidence and answer warnings only when errata are explicitly included.

**Hybrid Search**:
Retrieval that fuses the semantic (vector) and lexical (full-text) rankings of Sections into one result list.
_Avoid_: semantic search (that is only one arm of it)

**Query Plan**:
A deterministic, pure interpretation of a user query that records detected RFC numbers, explicit RFC section references, protocol seed RFCs, strong normative-intent filters, ABNF/grammar intent, historical intent, and the rationale for each detection. Retrieval may use it for direct section routing and effective filters; answer output reports it for traceability.
_Avoid_: LLM query rewrite, ranking model, hidden prompt analysis

**Query Trace**:
A per-query record written as one JSONL line when `RfcRag__TraceDirectory` is configured. Each trace captures the question, timed pipeline stages (search → assemble → generate), candidate RFC numbers, answer and warning counts, and retrieval metadata. The trace writer is fail-open — I/O failures produce a logged warning and the query succeeds. Traces are daily-rotated files under the configured directory.
_Avoid_: audit log, telemetry event (implies streaming), query log (too generic)

**Index Manifest**:
A row written at the end of every successful indexing run that records provenance: mirror path, parser type and version, embedding provider/model/dimensions, batch parameters, counts, and creation timestamp. Every eval report and trace carries the manifest id so results are comparable across runs.
_Avoid_: index metadata (too generic), indexing log

**Evidence Pack**:
The assembled, deduplicated, budget-enforced collection of evidence Sections and metadata for a query. It is the single output of the Context Assembler — callers consume the pack without knowing about deduplication, overlap collapse, or budget enforcement internals.
_Avoid_: context window, prompt payload, chunk bundle

**Evidence Section**:
A Section packaged as a unit of evidence, carrying its full text, parent-heading chain, score, optional Errata, and a stable citation id (`{RfcNumber}#{Section}`, e.g. "9110#9.3.1"). Evidence Sections are the building blocks of an Evidence Pack and the targets of citations.
_Avoid_: evidence chunk, context snippet

**Citation**:
A reference from a generated answer to an Evidence Section, consisting of the evidence id and a verbatim quote from the section text. Citations are the proof that an answer's claims are grounded in indexed RFC content.
_Avoid_: reference (ambiguous with RFC bibliographic references), footnote

**Answer Evaluation Metric**:
A computed score that measures the quality of a generated answer against a Golden Question: citation precision, citation recall, citation F1, and a boolean flag for correct no-answer classification. Metrics are computed by `AnswerEvaluationMetrics.Evaluate` per question and aggregated by `AnswerEvaluationMetrics.Aggregate`.

**Citation Precision**:
The fraction of citations in a generated answer that are grounded (i.e. the cited evidence id exists in the Evidence Pack and the quoted text is a substring of the section text). Equivalent to the standard information-retrieval precision applied to citations: `|grounded citations| / |total citations|`. NaN or 1.0 when there are zero citations.

**Citation Recall**:
The fraction of required RFCs (the Golden Question's `mustCite` field) that are correctly cited. NaN or 1.0 when there are no must-cite requirements.

**Golden Question**:
A curated question-answer expectation tuple in `golden_questions.json` with fields for expected RFCs, expected sections, must-cite/should-not-cite RFCs, answer type, and corpus marker. Golden questions are the ground truth for both retrieval and answer evaluation.

## Relationships

- The **Mirror** may contain several candidate files for one **RFC**; resolution always picks exactly one **Source**.
- An **RFC** is parsed from its **Source** into many **Sections**.
- A **Section** owns zero or more **ABNF Blocks** and zero or more **Normative Occurrences**.
- **Hybrid Search** returns **Sections**, never whole **RFCs**.
- An **Evidence Pack** is composed of **Evidence Sections**; each **Evidence Section** wraps one **Section**.
- An **Evidence Pack** contains **Citations**; each **Citation** references one **Evidence Section** by its evidence id.

## Example dialogue

> **Dev:** "If the **Mirror** has both `rfc9293.txt` and `rfc9293.xml`, do we index both?"
> **Domain expert:** "No — resolution picks one **Source** per **RFC**, and TXT always wins. The XML is only a fallback for numbers that have no TXT at all."
>
> **Dev:** "RFC 793 is from 1981 — it can't cite BCP 14. Do its uppercase MUSTs produce **Normative Occurrences**?"
> **Domain expert:** "Yes. A **Normative Occurrence** is a lexical signal, not proof of formal BCP 14 adoption. That's a deliberate trade-off for recall across the whole corpus."

## RFC Status vocabulary

Each indexed RFC may carry a **Status Block** derived from relation metadata (which RFC obsoletes or updates it):

| Term | Meaning |
|---|---|
| **current** | No RFC obsoletes or updates it. Default category when no relation row exists. |
| **updated** | One or more RFCs have issued partial updates to it, but it has not been fully superseded. |
| **obsoleted** | One or more RFCs have fully superseded it. The reranker applies a −0.10 score penalty by default. |

The `status` field is a nullable JSON object `{ category, obsoletedBy: int[], updatedBy: int[] }` returned on every **Search Result** and **Evidence Section** where relation metadata is available.

The `include_obsolete` parameter (available on `search_rfc`, `ask_rfc`, and CLI `--include-obsolete`) suppresses the −0.10 penalty and omits obsolescence warnings in assembled evidence. It defaults to `false`. Golden questions that intentionally target a historical RFC (e.g. a question explicitly about RFC 7231 after 9110 was published) should set `includeObsolete: true` in the golden question schema to avoid false retrieval regressions.

## Flagged ambiguities

- "secondary/auxiliary XML parsing" was ambiguous between *fallback* and *metadata enrichment* — resolved: strict fallback, one Source per RFC (ADR-0001).
- "binding requirements" (README phrasing) vs. what the index stores — resolved: a **Normative Occurrence** is a lexical uppercase-keyword match; it does not verify BCP 14 adoption.
## Answer Evaluation Metrics

**Citation Precision** is the fraction of uniquely cited RFCs that are in the Golden Question `mustCite` set.

**Citation Recall** is the fraction of `mustCite` RFCs that appear in the generated answer citations.

**Quote Faithfulness** is the fraction of citations whose `relevantText` is non-empty and appears verbatim in the cited Evidence Section. Missing evidence ids, empty quotes, and non-verbatim quotes count against the metric when evidence is supplied to evaluation.

**Obsolete Citation Rate** is the fraction of uniquely cited RFCs that are known to be obsolete from indexed RFC metadata. The metric function is pure; runners pass obsolete RFC numbers derived from corpus metadata.

**No-answer Accuracy** currently means refusal accuracy for Golden Questions whose `answerType` is `no_answer`. Factual and normative questions are not included in that aggregate until the metric is widened to full no-answer classification accuracy.
