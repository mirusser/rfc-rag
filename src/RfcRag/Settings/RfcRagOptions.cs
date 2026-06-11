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
    public int EmbeddingBatchSize { get; init; } = 20;

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
    public int MaxEmbeddingConcurrency { get; init; } = 8;

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
}
