using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RfcRag.Indexing;
using RfcRag.Tests.Fakes;

namespace RfcRag.Tests.UnitTests;

public sealed class EmbeddingServiceTests
{
    private static EmbeddingService MakeService(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        int batchSize = 2,
        int embeddingDimensions = 1536,
        int maxConcurrency = 1) =>
        new(generator, new EmbeddingRetryPolicy(TimeProvider.System),
            batchSize, embeddingDimensions, maxConcurrency,
            NullLogger<EmbeddingService>.Instance);

    [Fact]
    public async Task GenerateEmbeddingsAsync_MultipleTexts_ReturnsCorrectCount()
    {
        var service = MakeService(new FakeEmbeddingGenerator());

        var texts = new[] { "text1", "text2", "text3", "text4", "text5" };
        var embeddings = await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);

        Assert.Equal(5, embeddings.Count);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_EmptyList_ReturnsEmpty()
    {
        var service = MakeService(new FakeEmbeddingGenerator());

        var texts = Array.Empty<string>();
        var embeddings = await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);

        Assert.Empty(embeddings);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_PredictableValues_MapsCorrectly()
    {
        var service = MakeService(new FakeEmbeddingGenerator());

        var texts = new[] { "test" };
        var embeddings = await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);

        Assert.Single(embeddings);
        Assert.Equal(1536, embeddings[0].Length);
    }

    // --- Batching tests ---

    [Fact]
    public async Task Batching_11Texts_BatchSize5_Makes3GeneratorCalls()
    {
        var generator = new TrackingEmbeddingGenerator();
        var service = MakeService(generator, batchSize: 5);

        var texts = Enumerable.Range(0, 11).Select(i => $"text{i}").ToArray();
        await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);

        // ceil(11 / 5) = 3
        Assert.Equal(3, generator.CallCount);
    }

    [Fact]
    public async Task Batching_11Texts_BatchSize5_LastBatchHasRemainder()
    {
        var generator = new TrackingEmbeddingGenerator();
        var service = MakeService(generator, batchSize: 5);

        var texts = Enumerable.Range(0, 11).Select(i => $"text{i}").ToArray();
        await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);

        // Batches: [0..4]=5, [5..9]=5, [10]=1
        Assert.Equal(new[] { 5, 5, 1 }, generator.BatchSizes);
    }

    [Fact]
    public async Task ConcurrentBatches_PreserveInputOrder()
    {
        // 12 texts, batch=3, maxConcurrency=4 — four batches can run in parallel.
        // Task.WhenAll preserves task-index order in its result array, so the stitching
        // in EmbeddingService must also iterate in task order (not completion order).
        var service = MakeService(new FakeEmbeddingGenerator(), batchSize: 3, maxConcurrency: 4);
        var refService = MakeService(new FakeEmbeddingGenerator(), batchSize: 1);

        var texts = Enumerable.Range(0, 12).Select(i => $"ordering-sentinel-{i:00}").ToArray();
        IReadOnlyList<float[]> batched = await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);

        for (int i = 0; i < texts.Length; i++)
        {
            IReadOnlyList<float[]> single = await refService.GenerateEmbeddingsAsync([texts[i]], CancellationToken.None);
            Assert.Equal(single[0], batched[i]);
        }
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_Deterministic_SameInputProducesSameOutput()
    {
        var service = MakeService(new FakeEmbeddingGenerator(), batchSize: 5);
        string[] texts = ["the quick brown fox", "TLS certificate handshake", "HTTP semantics"];

        IReadOnlyList<float[]> first = await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);
        IReadOnlyList<float[]> second = await service.GenerateEmbeddingsAsync(texts, CancellationToken.None);

        for (int i = 0; i < texts.Length; i++)
            Assert.Equal(first[i], second[i]);
    }

    // --- Validation tests ---

    [Fact]
    public async Task GenerateEmbeddingsAsync_WrongCount_ThrowsEmbeddingProviderBatchMismatchException()
    {
        // Positive wrong counts cannot be safely mapped back to inputs.
        var timeProvider = new FakeTimeProvider();
        var service = new EmbeddingService(
            new MisbehavingEmbeddingGenerator(wrongCount: 2),
            new EmbeddingRetryPolicy(timeProvider),
            batchSize: 1,
            embeddingDimensions: 1536,
            maxConcurrency: 1,
            NullLogger<EmbeddingService>.Instance);

        Task<IReadOnlyList<float[]>> task = service.GenerateEmbeddingsAsync(["text"], CancellationToken.None);

        for (int i = 0; i < EmbeddingRetryPolicy.MaxAttempts - 1; i++)
        {
            await Task.Yield();
            timeProvider.Advance(TimeSpan.FromSeconds(30));
        }

        await Assert.ThrowsAsync<EmbeddingProviderBatchMismatchException>(
            async () => await task);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_SingleInputEmptyAfterRetries_ReturnsZeroVector()
    {
        var timeProvider = new FakeTimeProvider();
        var service = new EmbeddingService(
            new MisbehavingEmbeddingGenerator(wrongCount: 0),
            new EmbeddingRetryPolicy(timeProvider),
            batchSize: 1,
            embeddingDimensions: 1536,
            maxConcurrency: 1,
            NullLogger<EmbeddingService>.Instance);

        Task<IReadOnlyList<float[]>> task = service.GenerateEmbeddingsAsync(["text"], CancellationToken.None);

        for (int i = 0; i < EmbeddingRetryPolicy.MaxAttempts - 1; i++)
        {
            await Task.Yield();
            timeProvider.Advance(TimeSpan.FromSeconds(30));
        }

        IReadOnlyList<float[]> embeddings = await task;

        float[] embedding = Assert.Single(embeddings);
        Assert.Equal(1536, embedding.Length);
        Assert.All(embedding, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_TransientEmptyBatch_RetriesAndReturnsEmbeddings()
    {
        var timeProvider = new FakeTimeProvider();
        var generator = new TransientEmptyEmbeddingGenerator();
        var service = new EmbeddingService(
            generator,
            new EmbeddingRetryPolicy(timeProvider),
            batchSize: 2,
            embeddingDimensions: 1536,
            maxConcurrency: 1,
            NullLogger<EmbeddingService>.Instance);

        Task<IReadOnlyList<float[]>> task = service.GenerateEmbeddingsAsync(["text"], CancellationToken.None);

        await Task.Yield();
        timeProvider.Advance(TimeSpan.FromSeconds(30));
        IReadOnlyList<float[]> embeddings = await task;

        Assert.Equal(2, generator.CallCount);
        float[] embedding = Assert.Single(embeddings);
        Assert.Equal(1536, embedding.Length);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_BatchCountMismatchForMultipleInputs_FallsBackToSingleInputs()
    {
        var timeProvider = new FakeTimeProvider();
        var generator = new BatchEmptyEmbeddingGenerator();
        var service = new EmbeddingService(
            generator,
            new EmbeddingRetryPolicy(timeProvider),
            batchSize: 10,
            embeddingDimensions: 1536,
            maxConcurrency: 1,
            NullLogger<EmbeddingService>.Instance);

        Task<IReadOnlyList<float[]>> task = service.GenerateEmbeddingsAsync(
            ["alpha", "beta", "gamma"],
            CancellationToken.None);

        for (int i = 0; i < EmbeddingRetryPolicy.MaxAttempts - 1; i++)
        {
            await Task.Yield();
            timeProvider.Advance(TimeSpan.FromSeconds(30));
        }

        IReadOnlyList<float[]> embeddings = await task;

        Assert.Equal(3, embeddings.Count);
        Assert.All(embeddings, embedding => Assert.Equal(1536, embedding.Length));
        Assert.Equal([3, 3, 3, 1, 1, 1], generator.BatchSizes);
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_WrongDimensions_ThrowsInvalidOperationException()
    {
        // Generator returns 4-dimension vectors but service expects 1536.
        var service = MakeService(new MisbehavingEmbeddingGenerator(wrongDimensions: 4), embeddingDimensions: 1536);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateEmbeddingsAsync(["text"], CancellationToken.None));
    }
}
