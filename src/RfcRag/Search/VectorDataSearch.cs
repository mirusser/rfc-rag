using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.PgVector;
using RfcRag.Indexing;

namespace RfcRag.Search;

internal sealed class VectorDataSearch(
    PostgresCollection<Guid, RfcSectionRecord> collection,
    EmbeddingService embeddingService) : IVectorDataSearch
{
    private static readonly VectorSearchOptions<RfcSectionRecord> SearchOptions =
        new() { IncludeVectors = false };

    internal static double NormalizeScore(double distance) => 1.0 / (1.0 + distance);

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        IReadOnlyList<float[]> embeddings = await embeddingService
            .GenerateEmbeddingsAsync([query], cancellationToken)
            .ConfigureAwait(false);

        var queryVector = new ReadOnlyMemory<float>(embeddings[0]);

        var results = new List<SearchResult>(limit);
        await foreach (VectorSearchResult<RfcSectionRecord> hit in collection
            .SearchAsync(queryVector, limit, SearchOptions, cancellationToken)
            .ConfigureAwait(false))
        {
            double score = NormalizeScore(hit.Score ?? 1.0);
            RfcSectionRecord r = hit.Record;

            results.Add(new SearchResult(
                r.Id,
                r.RfcNumber,
                r.Title,
                r.Section,
                r.Heading,
                r.Text,
                r.SourcePath,
                r.Url,
                score));
        }

        return results;
    }
}
