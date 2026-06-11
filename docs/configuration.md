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

| Environment Variable | Default | Valid Range / Values | Description |
|---|---|---|
| `RfcRag__RfcMirrorPath` | `~/OtherRepos/rfc-mirror/` | Path to local RFC mirror |
| `RfcRag__PostgresConnectionString` | *(required)* | PostgreSQL connection string |
| `RfcRag__EmbeddingModel` | `openai/text-embedding-3-small` | OpenRouter embedding model |
| `RfcRag__EmbeddingBatchSize` | `20` | 1–2048 | Batch size for embedding API calls |
| `RfcRag__EmbeddingDimensions` | `1536` | 1–16000 | Embedding vector dimensions |
| `RfcRag__OpenRouterEmbeddingEndpoint` | `https://openrouter.ai/api/v1` | Absolute `http`/`https` URI | OpenRouter API base URL |
| `RfcRag__RunMigrationsOnStartup` | `true` | `true` / `false` | Auto-apply SQL schema migrations |
| `RfcRag__EmbeddingProvider` | `OpenRouter` | `OpenRouter` or `Local` | Embedding provider: `OpenRouter` (default) or `Local` |
| `RfcRag__LocalEmbeddingEndpoint` | `http://localhost:11434/v1` | Absolute `http`/`https` URI | Base URL of local embedding server — used when `EmbeddingProvider=Local` |
| `RfcRag__RfcParserType` | `Text` | `Text` or `Xml` | Parser mode: `Text` (plain-text `.txt` files, default) or `Xml` (prefers `.txt`, uses `.xml` only for RFC numbers that have no `.txt` counterpart) |
| `RfcRag__MaxIndexingParallelism` | `16` | ≥ 1 | Maximum number of RFC files indexed concurrently |
| `RfcRag__MaxEmbeddingConcurrency` | `8` | ≥ 1 | Maximum number of concurrent embedding API requests across all in-flight files |
| `RfcRag__ChatModel` | *(not set)* | OpenAI-compatible model ID (e.g., `openai/gpt-4o-mini`) | Chat model for answer generation. When unset, `ask_rfc` is disabled and the server remains retrieval-only |
| `RfcRag__ChatProvider` | `OpenRouter` | `OpenRouter` or `Local` | Chat provider for answer generation — used when `ChatModel` is set |
| `RfcRag__MaxAnswerTokens` | `1024` | ≥ 1 | Maximum tokens in generated answers |
| `RfcRag__EvidenceBudgetChars` | `16000` | ≥ 1 | Maximum evidence text characters sent to the chat model |
| `OpenRouter__ApiKey` | *(required for OpenRouter)* | OpenRouter API key — not needed when `EmbeddingProvider=Local` |

### OpenTelemetry Metrics

Setting `OTEL_EXPORTER_OTLP_ENDPOINT` registers an OTLP metrics exporter that sends the
`RfcRag.Embeddings` meter counters (`embedding.batches`, `embedding.retries`) to your
collector. All exporter settings come from standard `OTEL_*` environment variables — no
custom config keys. When unset, no exporter is registered and no connection attempts occur.

```bash
export OTEL_EXPORTER_OTLP_ENDPOINT="http://localhost:4318"
export OTEL_SERVICE_NAME="rfc-rag"
dotnet run --project src/RfcRag/
```

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