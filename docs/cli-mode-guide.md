## 💻 CLI Mode

Pass `--cli <verb> [args]` to run a one-shot query against an indexed database instead of starting the MCP server. All output is JSON on stdout.

| Verb | Args | Description |
|---|---|---|
| `search` | `<query> [--limit N] [--include-obsolete]` | Hybrid semantic + full-text search (default limit: 10); obsoleted RFCs demoted by −0.10 unless `--include-obsolete` is set |
| `section` | `<rfcNumber> <sectionId>` | Retrieve a single section by RFC number and section identifier |
| `normative` | `<keyword> [--rfc N]` | Find sections containing a normative keyword (MUST, SHOULD, …) |
| `evidence` | `<query> [--limit N] [--budget N]` | Assemble an Evidence Pack from search results (default limit: 10, default budget: 10,000 chars) |
| `ask` | `<question> [--limit N] [--keyword KW] [--include-obsolete]` | Ask a question and get a cited LLM-generated answer (default limit: 20; optional `--keyword` to filter search by normative keyword; `--include-obsolete` includes obsoleted RFCs without penalty or warning) |
| `stats` | *(none)* | Print indexed corpus statistics as JSON |

Pass `--eval <golden-questions-file> [--corpus testdata|full|all] [--limit N]` (top-level flag, not under `--cli`) to run the retrieval evaluation harness.

```bash
dotnet run --project src/RfcRag/ -- --eval docs/eval/golden_questions.json --corpus testdata
```

### From source

```bash
dotnet run --project src/RfcRag/ -- --cli search "TLS handshake" --limit 5
dotnet run --project src/RfcRag/ -- --cli section 8446 4.1.2
dotnet run --project src/RfcRag/ -- --cli normative MUST --rfc 2119
dotnet run --project src/RfcRag/ -- --cli evidence "GET request body" --limit 5 --budget 5000
dotnet run --project src/RfcRag/ -- --cli ask "What does RFC 9110 say about content negotiation?"
dotnet run --project src/RfcRag/ -- --cli ask "Encryption requirements" --keyword "MUST" --limit 10
dotnet run --project src/RfcRag/ -- --cli stats
```

### From Docker Compose

```bash
docker compose -f deploy/compose/rfc-rag.yaml exec rfc-rag dotnet RfcRag.dll --cli search "TLS handshake"
docker compose -f deploy/compose/rfc-rag.yaml exec rfc-rag dotnet RfcRag.dll --cli normative MUST --rfc 2119
```

### From standalone Docker container

```bash
docker exec rfc-rag dotnet RfcRag.dll --cli search "TLS handshake"
docker exec rfc-rag dotnet RfcRag.dll --cli stats
```