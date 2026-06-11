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

**Hybrid Search**:
Retrieval that fuses the semantic (vector) and lexical (full-text) rankings of Sections into one result list.
_Avoid_: semantic search (that is only one arm of it)

**Index Manifest**:
A row written at the end of every successful indexing run that records provenance: mirror path, parser type and version, embedding provider/model/dimensions, batch parameters, counts, and creation timestamp. Every eval report and trace carries the manifest id so results are comparable across runs.
_Avoid_: index metadata (too generic), indexing log

**Evidence Pack**:
The assembled, deduplicated, budget-enforced collection of evidence Sections and metadata for a query. It is the single output of the Context Assembler — callers consume the pack without knowing about deduplication, overlap collapse, or budget enforcement internals.
_Avoid_: context window, prompt payload, chunk bundle

**Evidence Section**:
A Section packaged as a unit of evidence, carrying its full text, parent-heading chain, score, and a stable citation id (`{RfcNumber}#{Section}`, e.g. "9110#9.3.1"). Evidence Sections are the building blocks of an Evidence Pack and the targets of citations.
_Avoid_: evidence chunk, context snippet

**Citation**:
A reference from a generated answer to an Evidence Section, consisting of the evidence id and a verbatim quote from the section text. Citations are the proof that an answer's claims are grounded in indexed RFC content.
_Avoid_: reference (ambiguous with RFC bibliographic references), footnote

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

## Flagged ambiguities

- "secondary/auxiliary XML parsing" was ambiguous between *fallback* and *metadata enrichment* — resolved: strict fallback, one Source per RFC (ADR-0001).
- "binding requirements" (README phrasing) vs. what the index stores — resolved: a **Normative Occurrence** is a lexical uppercase-keyword match; it does not verify BCP 14 adoption.
