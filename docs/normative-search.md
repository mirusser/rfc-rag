# Normative Search — How It Works

Normative keywords are the RFC 2119/8174 requirement-level words (MUST, MUST NOT, SHOULD,
SHOULD NOT, MAY, REQUIRED, RECOMMENDED, SHALL, SHALL NOT, OPTIONAL) that define protocol
behavior in IETF standards documents. They carry specific, binding meaning — "MUST" is an
absolute requirement, "MUST NOT" an absolute prohibition, "MAY" a true permission.

This repo indexes every occurrence of every normative keyword in every RFC section so you
can intersect two independent dimensions: semantic search finds the *topic*, normative
filtering adds the *requirement level*.

## Phase 1 — Indexing (One-Time)

During RFC indexing, the `RfcParser` extracts every normative keyword from every section.
Results are stored in `rfc_rag.normative_occurrences`:

```
section_id (FK → rfc_sections) | keyword  | line_offset | excerpt
--------------------------------|----------|-------------|--------------------------
a1b2c3...                       | MUST NOT | 42          | "MUST NOT use plaintext..."
a1b2c3...                       | SHOULD   | 15          | "...SHOULD be avoided"
d4e5f6...                       | MUST     | 3           | "MUST protect the data..."
```

This table contains 682,664 rows — every normative keyword in every RFC section, pre-computed
at indexing time. No extraction happens at query time.

## Phase 2 — Search (Query Time)

Two separate tools expose the data:

### `search_normative` — Pure Keyword Search

```
search_normative(keyword="MUST NOT", limit=10)
→ All sections containing "MUST NOT", ordered by earliest match
```

Simple keyword lookup. Use this when you want every section with a given keyword,
regardless of topic. Useful for broad compliance audits across all RFCs.

### `search_rfc` with `normative_keyword` — Semantic + Normative Filter

```
search_rfc(query="encryption transport security", normative_keyword="MUST NOT", limit=5)
→ Sections semantically about encryption that ALSO contain "MUST NOT"
```

The pipeline:

1. **Semantic search** — generates embedding for the query, searches pgvector,
   fetches top N×4 candidate sections from each CTE (lexical and vector arms)
2. **Normative filter (SQL-side)** — both CTEs include an `EXISTS` predicate against
   `normative_occurrences WHERE keyword = @NormativeKeyword`, so the candidate pool
   is filtered *before* ranking. No in-memory post-filter, no overscan trick.
3. **RRF fusion** — reciprocal rank fusion combines the filtered lexical and vector
   candidate pools, then the outer query trims to the requested limit.

The normative table is just an indexed lookup — it's not the primary search. Semantic
search finds the *topic*, the normative filter adds the *requirement level*. This is
fundamentally different from simple keyword search: you're intersecting two independent
dimensions of meaning.

## Concrete Example

```
search_rfc(query="encryption transport security communication", limit=5)
→ 5 sections: RFC 5000, 5794, 5091, 9065, 5091

search_rfc(query="encryption transport security communication",
           normative_keyword="MUST NOT", limit=5)
→ 1 section: RFC 7435 "Opportunistic Security" §3
  "Opportunistic Security provides a near-term approach to counter passive
   attacks by removing barriers to the widespread use of encryption..."
```

The unfiltered search returned broadly relevant encryption sections. The filtered search
returned *only* the one containing "MUST NOT" — the section explicitly about when
encryption is required. The other four were about encryption but didn't contain a
formal prohibition, so they were excluded.

## Why This Matters for AI Agents

When a coding agent needs to answer "find RFCs that prohibit unencrypted communication":

1. `search_rfc(query="unencrypted communication", normative_keyword="MUST NOT")`
2. Returns precise, citeable sections
3. Agent quotes the exact RFC, section, and requirement level
4. No hallucination — it's retrieval, not generation

Without normative search, the agent either greps for "MUST NOT" (too broad, 682K results)
or does semantic-only search (misses the normative dimension). Combined, you get both
precision and authority.
