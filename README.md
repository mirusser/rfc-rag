# RFC RAG

Local RAG (Retrieval-Augmented Generation) MCP server for RFCs. Indexes a local RFC mirror
into PostgreSQL with pgvector for vector search and PostgreSQL full-text search for lexical
retrieval, then exposes MCP tools for AI agents to search and cite RFCs with section-level
precision.

[![CI](https://github.com/mirusser/rfc-rag/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/mirusser/rfc-rag/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512bd4?style=flat-square&logo=dotnet)
![MCP](https://img.shields.io/badge/MCP-stdio%20Server-black?style=flat-square)

## Quick Start

```bash
# Clone and set up
git clone https://github.com/mirusser/rfc-rag.git
cd rfc-rag

# Set up RFC mirror (one-time)
rsync -avz --delete rsync.rfc-editor.org::rfcs-text-only ~/OtherRepos/rfc-mirror/

# Configure environment
cp deploy/compose/rfc-rag.env.example .env.rfc-rag
# edit .env.rfc-rag with your OpenRouter API key and mirror path
set -a && source .env.rfc-rag && set +a

# Enable pgvector in PostgreSQL (one-time)
psql "Host=localhost;Database=rfc_rag;Username=postgres;Password=postgres" -c "CREATE EXTENSION IF NOT EXISTS vector;"

# Build and run (auto-indexes on first start)
dotnet run --project src/RfcRag/
```

On first run, the server indexes all ~9,800 RFCs (~10-15 minutes). Subsequent starts use
incremental SHA256-based skip detection and complete in seconds.

### Docker Compose

```bash
cp deploy/compose/rfc-rag.env.example .env.rfc-rag
# edit .env.rfc-rag
docker compose --env-file .env.rfc-rag -f deploy/compose/rfc-rag.yaml up
```

To stop and clean up (including the PostgreSQL data volume):

```bash
docker compose -f deploy/compose/rfc-rag.yaml down -v
docker volume rm rfc-rag_pgdata 2>/dev/null; true
```

### Standalone Docker

```bash
docker build -t rfc-rag .

docker run --rm -i --network host \
  -v ~/rfc-mirror:/rfc-mirror:ro \
  -e RfcRag__PostgresConnectionString="Host=localhost;Database=rfc_rag;Username=postgres;Password=postgres" \
  -e RfcRag__RfcMirrorPath=/rfc-mirror \
  -e OpenRouter__ApiKey="sk-or-..." \
  rfc-rag
```

## Configuration

| Environment Variable | Default | Description |
|---|---|---|
| `RfcRag__RfcMirrorPath` | `~/OtherRepos/rfc-mirror/` | Path to local RFC mirror |
| `RfcRag__PostgresConnectionString` | (required) | PostgreSQL connection string |
| `RfcRag__EmbeddingModel` | `openai/text-embedding-3-small` | OpenRouter embedding model |
| `RfcRag__EmbeddingBatchSize` | `20` | Batch size for embedding API calls |
| `RfcRag__OpenRouterEmbeddingEndpoint` | `https://openrouter.ai/api/v1` | OpenRouter API base URL |
| `RfcRag__RunMigrationsOnStartup` | `true` | Auto-apply SQL schema migrations |
| `RfcRag__EmbeddingDimensions` | `1536` | Embedding vector dimensions |
| `OpenRouter__ApiKey` | (required) | OpenRouter API key |

## MCP Tools

- **`search_rfc`** — Hybrid search (vector + full-text) with reciprocal rank fusion
- **`get_rfc`** — RFC metadata, TOC, and section preview
- **`get_rfc_section`** — Specific section retrieval with child expansion
- **`get_rfc_toc`** — Table of contents as section→heading map
- **`search_normative`** — Search normative keywords (MUST, SHOULD, etc.)
- **`search_abnf`** — Search ABNF grammar definitions
- **`find_updates_obsoletes`** — Back-reference lookup for RFCs
- **`rfc_stats`** — Indexed corpus statistics
- **`get_rfc_metadata`** — Single RFC metadata lookup
- **`list_indexed_rfcs`** — Paginated list of indexed RFCs

Full tool documentation in [src/RfcRag/README.md](src/RfcRag/README.md).

## Connecting AI Agents

### Claude Code

```bash
claude mcp add-json --scope user rfc-rag \
  '{"type":"stdio","command":"dotnet","args":["run","--project","src/RfcRag/"]}'
```

### Codex

```toml
# ~/.codex/config.toml
[mcp_servers.rfc-rag]
command = "dotnet"
args = ["run", "--project", "src/RfcRag/"]
```

### Containerized MCP

```bash
claude mcp add-json --scope user rfc-rag \
  '{"type":"stdio","command":"docker","args":["exec","-i","rfc-rag-rfc-rag-1","dotnet","RfcRag.dll"]}'
```

## Architecture

```
~/OtherRepos/rfc-mirror/*.txt
         │
         ▼
   RfcParser (section splitter, metadata, ABNF, normative keywords)
         │
         ├──► PostgreSQL + pgvector (vector search, cosine distance)
         ├──► PostgreSQL tsvector (full-text lexical search)
         └──► Hybrid retrieval with reciprocal rank fusion
         │
         ▼
   MCP stdio server (ModelContextProtocol 1.3.0)
         │
         ▼
   Coding agents (Claude Code, Codex)
```

## Database Schema

```
rfc_rag.rfc_sections          — primary search unit (vectors + FTS)
rfc_rag.indexed_rfcs           — SHA256 tracking for incremental indexing
rfc_rag.rfc_abnf_blocks        — extracted ABNF grammar blocks
rfc_rag.normative_occurrences  — pre-extracted normative keywords
rfc_rag.schema_migrations      — applied migration tracking
```

## Running Tests

```bash
# Unit tests (no dependencies)
dotnet test --filter "Category!=Integration"

# Integration tests (requires Docker)
dotnet test --filter "Category=Integration"
```

## Boundaries

- PostgreSQL with `pgvector` extension for vector storage and full-text search
- OpenRouter API for embedding generation (`text-embedding-3-small`)
- `ModelContextProtocol` SDK for MCP server transport
- `Microsoft.Extensions.AI` for embedding abstraction
- Local RFC mirror at the configured path

## License

Apache-2.0 — see [LICENSE](LICENSE).
