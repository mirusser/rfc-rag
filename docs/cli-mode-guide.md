## 💻 CLI Mode

Pass `--cli <verb> [args]` to run a one-shot query against an indexed database instead of starting the MCP server. All output is JSON on stdout.

| Verb | Args | Description |
|---|---|---|
| `search` | `<query> [--limit N]` | Hybrid semantic + full-text search (default limit: 10) |
| `section` | `<rfcNumber> <sectionId>` | Retrieve a single section by RFC number and section identifier |
| `normative` | `<keyword> [--rfc N]` | Find sections containing a normative keyword (MUST, SHOULD, …) |
| `stats` | *(none)* | Print indexed corpus statistics as JSON |

### From source

```bash
dotnet run --project src/RfcRag/ -- --cli search "TLS handshake" --limit 5
dotnet run --project src/RfcRag/ -- --cli section 8446 4.1.2
dotnet run --project src/RfcRag/ -- --cli normative MUST --rfc 2119
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