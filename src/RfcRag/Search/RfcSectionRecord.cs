using Microsoft.Extensions.VectorData;

namespace RfcRag.Search;

internal sealed class RfcSectionRecord
{
    [VectorStoreKey(StorageName = "id")]
    public Guid Id { get; init; }

    [VectorStoreData(StorageName = "rfc_number")]
    public int RfcNumber { get; init; }

    [VectorStoreData(StorageName = "title")]
    public string Title { get; init; } = "";

    [VectorStoreData(StorageName = "section")]
    public string Section { get; init; } = "";

    [VectorStoreData(StorageName = "heading")]
    public string? Heading { get; init; }

    [VectorStoreData(StorageName = "text")]
    public string Text { get; init; } = "";

    [VectorStoreData(StorageName = "source_path")]
    public string SourcePath { get; init; } = "";

    [VectorStoreData(StorageName = "url")]
    public string Url { get; init; } = "";

    // Reuses the existing HNSW index (vector_cosine_ops) — DistanceFunction.CosineDistance matches vector_cosine_ops.
    [VectorStoreVector(Dimensions: 1536, DistanceFunction = DistanceFunction.CosineDistance, IndexKind = IndexKind.Hnsw, StorageName = "embedding")]
    public ReadOnlyMemory<float>? Embedding { get; init; }
}
