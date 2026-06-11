namespace RfcRag.Indexing;

internal sealed record class IndexManifest
{
    public Guid Id { get; init; }
    public string MirrorPath { get; init; } = string.Empty;
    public string ParserType { get; init; } = string.Empty;
    public string ParserVersion { get; init; } = string.Empty;
    public string EmbeddingProvider { get; init; } = string.Empty;
    public string EmbeddingModel { get; init; } = string.Empty;
    public int EmbeddingDimensions { get; init; }
    public int EmbeddingBatchSize { get; init; }
    public int RfcCount { get; init; }
    public int SectionCount { get; init; }
    public DateTime CreatedAt { get; init; }
}
