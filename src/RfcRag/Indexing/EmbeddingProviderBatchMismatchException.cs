namespace RfcRag.Indexing;

internal sealed class EmbeddingProviderBatchMismatchException(int actualCount, int expectedCount)
    : InvalidOperationException(
        $"Embedding provider returned {actualCount} embeddings for a batch of {expectedCount} inputs.")
{
    public int ActualCount { get; } = actualCount;

    public int ExpectedCount { get; } = expectedCount;
}
