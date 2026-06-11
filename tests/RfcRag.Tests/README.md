# RfcRag.Tests

Unit and integration tests for `RfcRag`.

**Covers:** RFC parser, embedding service, indexer, search service, MCP tools, retrieval metrics, eval harness

## Structure

```
UnitTests/
  RfcParserTests.cs             # 14 tests: metadata, sections, ABNF, normative keywords, TOC stripping
  RfcRagToolsTests.cs           # 19 tests: search, metadata, indexing, normative/ABNF search, error handling
  RetrievalMetricsTests.cs      # 18 tests: Hit@K, MRR, nDCG (RFC + section level), aggregate metrics
  CliCommandTests.cs            # CLI verb routing and output contract tests
  RfcRagOptionsValidatorTests.cs  # Config validation (enabled/disabled/invalid states)
IntegrationTests/
  RfcRagIntegrationTests.cs     # 9 tests: migrations, indexing, search, sections, metadata, manifest
  LiveApiIndexingTests.cs       # 4 tests: real-API indexing and vector search (skipped in CI, requires LiveApiKey)
  RetrievalQualityTests.cs      # golden-question baseline threshold assertions (requires Docker)
  (requires Docker)
TestData/
  rfc2119.txt, rfc3986.txt, rfc8446.txt, rfc9000.txt, rfc9110.txt, rfc9999.txt, badfile.txt
```

## Running

```bash
# Unit tests (no dependencies — fast)
dotnet test tests/RfcRag.Tests/ --filter "Category!=Integration"

# Integration tests (requires Docker)
dotnet test tests/RfcRag.Tests/ --filter "Category=Integration"

# Retrieval quality suite — indexes TestData corpus and asserts baseline thresholds (requires Docker)
dotnet test tests/RfcRag.Tests/ --filter "Category=RetrievalQuality"

# All tests
dotnet test tests/RfcRag.Tests/
```

| Category | Count |
|----------|-------|
| Unit (Category!=Integration) | ~190 |
| Integration (Category=Integration) | ~50 |
| **Total** | **~240** |
