namespace RfcRag.Settings;

/// <summary>
/// Configuration options for the RFC RAG pipeline.
/// Bound from the <c>RfcRag</c> configuration section.
/// </summary>
public sealed record class RfcRagOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "RfcRag";

    /// <summary>Environment variable for the OpenRouter API key.</summary>
    public const string OpenRouterApiKeyEnvironmentVariable = "OpenRouter__ApiKey";

    /// <summary>Path to the local RFC mirror directory containing .txt files.</summary>
    public required string RfcMirrorPath { get; init; }

    /// <summary>PostgreSQL connection string for the RFC RAG database.</summary>
    public required string PostgresConnectionString { get; init; }

    /// <summary>OpenRouter embedding model identifier (e.g., "openai/text-embedding-3-small").</summary>
    public string EmbeddingModel { get; init; } = "openai/text-embedding-3-small";

    /// <summary>Batch size for embedding generation. Limited by OpenRouter API constraints.</summary>
    public int EmbeddingBatchSize { get; init; } = 10;

    /// <summary>Whether to run schema migrations on startup.</summary>
    public bool RunMigrationsOnStartup { get; init; } = true;

    /// <summary>OpenRouter API base URL for embedding requests.</summary>
    public string OpenRouterEmbeddingEndpoint { get; init; } = "https://openrouter.ai/api/v1";

    /// <summary>
    /// Expected vector dimension for embeddings. Must match the pgvector column dimension.
    /// Default (1536) matches text-embedding-3-small from OpenRouter/OpenAI.
    /// </summary>
    public int EmbeddingDimensions { get; init; } = 1536;

    /// <summary>
    /// Maximum number of RFC files indexed concurrently.
    /// I/O-bound (embedding API + DB), so higher than CPU count is appropriate.
    /// </summary>
    public int MaxIndexingParallelism { get; init; } = 16;

    /// <summary>
    /// Maximum number of concurrent embedding API requests across all in-flight files.
    /// Caps burst traffic to the embedding provider while allowing intra-file batch parallelism.
    /// </summary>
    public int MaxEmbeddingConcurrency { get; init; } = 2;

    /// <summary>
    /// Selects the embedding provider. Defaults to <see cref="EmbeddingProvider.OpenRouter"/>.
    /// Set to <see cref="EmbeddingProvider.Local"/> to use a local OpenAI-compatible server.
    /// </summary>
    public EmbeddingProvider EmbeddingProvider { get; init; } = EmbeddingProvider.OpenRouter;

    /// <summary>
    /// Base URL for the local embedding server (e.g. <c>http://localhost:11434/v1</c>).
    /// Used when <see cref="EmbeddingProvider"/> is <see cref="EmbeddingProvider.Local"/>.
    /// </summary>
    public string LocalEmbeddingEndpoint { get; init; } = "http://localhost:11434/v1";

    /// <summary>
    /// Selects the RFC parser type. <see cref="RfcParserType.Text"/> processes only
    /// <c>rfc*.txt</c> files (default). <see cref="RfcParserType.Xml"/> additionally
    /// processes <c>rfc*.xml</c> files using the RFC XML 2 format.
    /// </summary>
    public RfcParserType RfcParserType { get; init; } = RfcParserType.Text;

    /// <summary>Optional local RFC Editor errata JSON snapshot path. Unset disables errata ingestion.</summary>
    public string? ErrataJsonPath { get; init; }

    /// <summary>
    /// Chat model identifier for answer generation (e.g., "openai/gpt-4o-mini").
    /// When <see langword="null"/> or empty, answer generation is disabled — the server
    /// remains a retrieval-only MCP server. Set this to enable <c>ask_rfc</c>.
    /// </summary>
    public string? ChatModel { get; init; }

    /// <summary>
    /// Selects the chat provider. Defaults to <see cref="ChatProvider.OpenRouter"/>.
    /// Only used when <see cref="ChatModel"/> is set.
    /// </summary>
    public ChatProvider ChatProvider { get; init; } = ChatProvider.OpenRouter;

    /// <summary>
    /// Maximum number of tokens allowed in the generated answer.
    /// Used as <c>MaxTokens</c> in chat completion requests.
    /// </summary>
    public int MaxAnswerTokens { get; init; } = 1024;

    /// <summary>
    /// Maximum total characters of evidence text to include in the
    /// generation prompt. Truncation warnings are added to the evidence
    /// pack when this budget is exceeded.
    /// </summary>
    public int EvidenceBudgetChars { get; init; } = 16_000;

    /// <summary>When true, deterministic query planning can refine retrieval before answer generation.</summary>
    public bool QueryPlannerEnabled { get; init; } = true;

    /// <summary>
    /// When true, a wider fused candidate set is fetched and reranked deterministically
    /// using signal weights (RFC number match, section match, heading terms, protocol hints,
    /// obsolete penalty). Disable for A/B comparison against baseline hybrid search.
    /// </summary>
    public bool RerankerEnabled { get; init; } = true;

    /// <summary>
    /// Optional directory for per-query JSONL trace files. When set, each call to
    /// <c>ask_rfc</c> produces a trace line with stage timestamps, candidate RFC numbers,
    /// and retrieval metadata. When unset, tracing is disabled.
    /// </summary>
    public string? TraceDirectory { get; init; }
}
