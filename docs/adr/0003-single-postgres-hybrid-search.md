# One PostgreSQL owns vectors, full-text, and fusion

Hybrid retrieval runs entirely inside a single PostgreSQL database: pgvector (HNSW, cosine) for the semantic arm, a generated `tsvector` column with a GIN index for the lexical arm, and Reciprocal Rank Fusion (`1/(60+rank)`, 4× candidate overscan) computed in the same SQL statement. The obvious alternative — a dedicated vector store (Qdrant, Weaviate, …) beside a search engine — was rejected: both arms must agree on one source of truth for Sections, fusing in SQL avoids cross-store consistency and rank-merging in application code, and a local research tool should require exactly one stateful dependency.

## Consequences

- HNSW post-filtering limits apply (on pgvector < 0.8 a `WHERE` clause can underfill the vector arm); accepted and mitigated by the candidate overscan plus the exactly-filtered lexical arm. See the Task 3 note in `docs/plans/archive/2026-06-10-hardening-plan.md`.
