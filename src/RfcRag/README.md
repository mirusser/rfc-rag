# RFC RAG

Local RAG (Retrieval-Augmented Generation) MCP server for RFCs — indexes a local RFC mirror
into PostgreSQL with pgvector for vector search and PostgreSQL full-text search for lexical
retrieval.

**Owns:** RFC parsing, section-based chunking, hybrid search (vector + lexical + exact),
ABNF grammar extraction, normative keyword indexing, MCP tool exposure

## MCP Tools

### `search_rfc`
Hybrid search combining vector similarity and full-text lexical search with reciprocal rank fusion. Supports normative keyword filtering via the `normative_keyword` parameter (applied inside the SQL, no in-memory post-filter). Obsoleted RFCs receive a −0.10 score penalty by default; pass `include_obsolete: true` to suppress the penalty and status warnings.

```
Parameters: query (string), limit (int, default=10), normative_keyword (string?, optional, e.g. "MUST NOT", "SHOULD"), include_obsolete (bool, default=false)
Returns: JSON array of { rfcNumber, title, section, heading, excerpt, sourcePath, url, score, status? }
  status: { category: "current"|"updated"|"obsoleted", obsoletedBy: int[], updatedBy: int[] } — present when RFC has relation metadata
```

### `ask_rfc`
Ask a natural-language question about RFCs. Runs hybrid search, assembles an evidence pack, and generates a cited answer using a language model. Obsoleted RFCs are demoted and flagged by default; pass `include_obsolete: true` to include them without penalty or warning. Pass `include_errata: true` to attach matching errata from the configured local snapshot; `errata_status` defaults to `verified`.

```
Parameters: question (string), limit (int?, default=20), normative_keyword (string?, optional), include_obsolete (bool, default=false), include_errata (bool, default=false), errata_status (string?, default="verified")
Returns: JSON object with { answer, citations: [{evidenceId, rfcNumber, section, relevantText?}], model?, finishReason?, noAnswer, warnings: [{type, message, evidenceId?}], retrieval: { strategy, filters: { normativeKeyword?, includeErrata, errataStatus? }, plan? }, verification: { claims: [{claim, status, citationEvidenceIds?}], claimSupportRate, verificationWarnings: [{type, message, evidenceId?}] } }
```

### `get_rfc`
Retrieve RFC metadata, table of contents, and a preview of the first 20 sections. Breaking change: the top-level `text` field has been removed. Full section content is available via `get_rfc_section`.

```
Parameters: rfcNumber (int)
Returns: JSON object with { rfcNumber, title, sourcePath, url, sectionCount, toc, sections } (where toc is a section→heading map and sections is a preview array of the first 20 RfcSection objects)
```

### `get_rfc_section`
Retrieve a specific section of an RFC with optional child section expansion and type-reference resolution.

```
Parameters: rfcNumber (int), section (string, e.g. "6.3"), depth (int, default=0, 1=include immediate children), expand (bool, default=false, resolves type references; ignored when depth>0)
Returns: JSON object with { section, children? } when depth>0, { section, expandedTypes? } when expand=true, or plain RfcSection when depth=0 and expand=false
```

### `get_rfc_toc`
Get the table of contents for an RFC as a flat section→heading map.

```
Parameters: rfcNumber (int)
Returns: JSON object mapping section identifiers to heading strings (null for untitled sections)
```

### `search_normative`
Search for normative keywords (RFC 2119/8174) across indexed RFCs.

```
Parameters: keyword (string), rfcNumbers (int[]?, optional), limit (int, default=20)
Valid keywords: MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED, MAY, OPTIONAL
Returns: JSON array of { rfcNumber, title, section, heading, excerpt, score }
```

### `search_abnf`
Search ABNF grammar definitions by rule name or fragment.

```
Parameters: query (string), rfcNumbers (int[]?, optional), limit (int, default=20)
Returns: JSON array of { rfcNumber, title, section, heading, excerpt, score }
```

### `find_updates_obsoletes`
Find RFCs that update or obsolete a given RFC.

```
Parameters: rfcNumber (int)
Returns: { rfcNumber, title, updates: [...], obsoletes: [...], updated_by: [{number, title}], obsoleted_by: [{number, title}] }
```

### `rfc_stats`
Get statistics about the indexed RFC corpus, including the latest Index Manifest with provenance data.

```
Parameters: none
Returns: JSON string { indexedRfcs, sections, abnfBlocks, normativeOccurrences, lastIndexedAtUtc, manifest }
```

### `get_rfc_metadata`
Retrieve metadata for a specific RFC (title, updates, obsoletes, grammar style).

```
Parameters: rfcNumber (int)
Returns: JSON object with { number, title, date, category, updates, obsoletes, authors, issn, grammarStyle, updated_by, obsoleted_by }
```

### `list_indexed_rfcs`
List indexed RFCs with their numbers and titles.

```
Parameters: limit (int, default=100, max=1000), offset (int, default=0)
Returns: JSON object with { total, rfcs: [{ rfcNumber, title, ... }] }
```

## Example Queries

```
"What does RFC 9110 say about content negotiation?"
→ search_rfc("content negotiation", limit=5)
→ get_rfc_section(9110, "8.6")

"Find all TLS 1.3 handshake ABNF"
→ search_abnf("handshake", rfcNumbers=[8446])

"Which RFCs MUST NOT allow unencrypted communication?"
→ search_normative("MUST NOT", limit=10)

"What obsoletes RFC 7230?"
→ find_updates_obsoletes(7230)
```

## Contents

- `RfcParser.cs` — parses raw RFC `.txt` files, strips page headers/footers, extracts metadata, splits into sections, detects ABNF blocks, extracts normative keywords
- `RfcIndexer.cs` — walks the RFC mirror, parses each RFC, generates embeddings via OpenRouter, stores in PostgreSQL
- `Search/SearchRepository.cs` — Dapper-based data access for RFC sections, ABNF blocks, normative occurrences
- `Search/MetadataRepository.cs` — Dapper-based data access for RFC metadata queries
- `Indexing/IndexingRepository.cs` — Dapper-based data access for indexing operations
- `SearchService.cs` — hybrid search combining vector similarity, full-text lexical search, and exact section lookup
- `RfcRagTools.cs` — MCP tool definitions exposed to coding agents
- `Program.cs` — stdio MCP server with auto-indexing on startup
- `Infrastructure/ServiceCollectionExtensions.cs` — dependency injection for RFC RAG services
- `Infrastructure/RfcRagMigrationRunner.cs` — database schema migration runner
- `RfcDocument.cs` — parsed RFC document with metadata and sections
- `Settings/RfcRagOptions.cs` — configuration options
- `Infrastructure/RfcRagConventions.cs` — database schema and table name conventions
- `Indexing/EmbeddingService.cs` — batch text embedding generation
- `Indexing/EmbeddingRetryPolicy.cs` — retry/backoff/classification for embedding API calls
- `Indexing/RfcSourceResolver.cs` — deterministic RFC source file discovery (TXT primary, XML fallback)
- `Settings/RfcRagOptionsValidator.cs` — startup config validation with aggregated error reporting
- `Search/ISearchService.cs` — search and retrieval interface
- `Indexing/IIndexerService.cs` — indexing service interface
- `SearchResult.cs` — ranked search result model
- `Answering/` — evidence assembly and enrichment (`ContextAssembler.cs`, `EvidencePack.cs`, `EvidenceSection.cs`, `EvidenceWarning.cs`)
- `Models/` — database entity models (`RfcSection.cs`, `RfcAbnfBlock.cs`, `NormativeOccurrence.cs`, `RfcMetadata.cs`, `RfcRelationsBatch.cs`)

## Running Tests

```bash
# Unit tests (no dependencies)
dotnet test tests/RfcRag.Tests/ --filter "Category!=Integration"

# Integration tests (requires Docker)
dotnet test tests/RfcRag.Tests/ --filter "Category=Integration"
```

