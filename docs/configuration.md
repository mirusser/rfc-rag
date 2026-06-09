Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), [PostgreSQL 15+](https://www.postgresql.org/) with [pgvector](https://github.com/pgvector/pgvector), and an [OpenRouter API key](https://openrouter.ai/).

### 🛠️ From Source

```bash
git clone https://github.com/mirusser/rfc-rag.git
cd rfc-rag

# Set up RFC mirror (one-time)
rsync -avz --delete rsync.rfc-editor.org::rfcs-text-only ~/OtherRepos/rfc-mirror/

# Configure environment
cp deploy/compose/rfc-rag.env.example .env.rfc-rag
# edit .env.rfc-rag with your OpenRouter API key and mirror path
set -a && source .env.rfc-rag && set +a

# Enable pgvector in PostgreSQL (one-time)
psql "Host=localhost;Database=rfc_rag;Username=postgres;Password=postgres" \
  -c "CREATE EXTENSION IF NOT EXISTS vector;"

# Build and run (auto-indexes on first start)
dotnet run --project src/RfcRag/
```

On first run, the server indexes all ~9,800 RFCs (~10–15 minutes). Subsequent starts use incremental SHA256-based skip detection and complete in seconds.

### 🐳 Docker Compose

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

### 🐳 Standalone Docker

```bash
docker build -t rfc-rag .

docker run --rm -i --network host \
  -v ~/rfc-mirror:/rfc-mirror:ro \
  -e RfcRag__PostgresConnectionString="Host=localhost;Database=rfc_rag;Username=postgres;Password=postgres" \
  -e RfcRag__RfcMirrorPath=/rfc-mirror \
  -e OpenRouter__ApiKey="sk-or-..." \
  rfc-rag
```

## 🔧 Configuration

| Environment Variable | Default | Description |
|---|---|---|
| `RfcRag__RfcMirrorPath` | `~/OtherRepos/rfc-mirror/` | Path to local RFC mirror |
| `RfcRag__PostgresConnectionString` | *(required)* | PostgreSQL connection string |
| `RfcRag__EmbeddingModel` | `openai/text-embedding-3-small` | OpenRouter embedding model |
| `RfcRag__EmbeddingBatchSize` | `20` | Batch size for embedding API calls |
| `RfcRag__EmbeddingDimensions` | `1536` | Embedding vector dimensions |
| `RfcRag__OpenRouterEmbeddingEndpoint` | `https://openrouter.ai/api/v1` | OpenRouter API base URL |
| `RfcRag__RunMigrationsOnStartup` | `true` | Auto-apply SQL schema migrations |
| `RfcRag__EmbeddingProvider` | `OpenRouter` | Embedding provider: `OpenRouter` (default) or `Local` |
| `RfcRag__LocalEmbeddingEndpoint` | `http://localhost:11434/v1` | Base URL of local embedding server — used when `EmbeddingProvider=Local` |
| `RfcRag__RfcParserType` | `Text` | Parser mode: `Text` (plain-text `.txt` files, default) or `Xml` (also indexes RFC XML 2 `.xml` files) |
| `OpenRouter__ApiKey` | *(required for OpenRouter)* | OpenRouter API key — not needed when `EmbeddingProvider=Local` |

### Switching to a Local Embedding Provider

By default embeddings are generated via OpenRouter. To use a local server (Ollama, llama.cpp) instead — no API key required — add the following to your `.env.rfc-rag`:

```ini
# Example: Ollama with nomic-embed-text (768-dim)
RfcRag__EmbeddingProvider=Local
RfcRag__LocalEmbeddingEndpoint=http://localhost:11434/v1
RfcRag__EmbeddingModel=nomic-embed-text
RfcRag__EmbeddingDimensions=768
# OpenRouter__ApiKey is not required when EmbeddingProvider=Local
```

`EmbeddingDimensions` must match the model's output dimension.

If switching from a different model or provider, the `rfc_sections.embedding` column dimension must match. Reset it first:

```bash
# Drops and recreates the embedding column, then triggers a full reindex
dotnet run --project src/RfcRag/ -- --reset-embeddings --confirm
```

Without `--confirm` the command prints a warning and exits without making any changes.