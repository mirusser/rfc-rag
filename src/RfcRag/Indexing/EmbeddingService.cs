using System.Diagnostics.Metrics;

namespace RfcRag.Indexing;

/// <summary>
/// Generates vector embeddings for RFC section text using the configured embedding provider.
/// Handles batching, concurrent dispatch, rate limiting, and error recovery.
/// </summary>
internal sealed partial class EmbeddingService(
    IEmbeddingGenerator<string, Embedding<float>> generator,
    EmbeddingRetryPolicy retryPolicy,
    int batchSize,
    int embeddingDimensions,
    int maxConcurrency,
    ILogger<EmbeddingService> logger) : IDisposable
{
    internal const string EmbeddingsMeterName = "RfcRag.Embeddings";

    private const string MetricBatchName = "embedding.batches";
    private const string MetricRetryName = "embedding.retries";
    private const string TagReason = "reason";
    private const string TagOutcome = "outcome";
    private const string OutcomeOk = "ok";
    private const string OutcomeFailed = "failed";
    private const string ReasonRateLimited = "rate_limited";
    private const string ReasonServerError = "server_error";
    private const string ReasonTransport = "transport";

    private static readonly Meter embeddingsMeter = new(EmbeddingsMeterName);
    private static readonly Counter<long> batchCounter =
        embeddingsMeter.CreateCounter<long>(MetricBatchName, description: "Number of embedding batches processed");
    private static readonly Counter<long> retryCounter =
        embeddingsMeter.CreateCounter<long>(MetricRetryName, description: "Number of embedding retry attempts");

    private readonly SemaphoreSlim throttle = new(maxConcurrency, maxConcurrency);

    public void Dispose() => throttle.Dispose();

    /// <summary>
    /// Generates embeddings for a collection of text inputs.
    /// Batches are dispatched concurrently, throttled by <c>maxConcurrency</c>.
    /// </summary>
    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
        {
            return [];
        }

        int batchCount = (texts.Count + batchSize - 1) / batchSize;
        var batchTasks = new Task<GeneratedEmbeddings<Embedding<float>>>[batchCount];

        for (int b = 0; b < batchCount; b++)
        {
            int offset = b * batchSize;
            int count = Math.Min(batchSize, texts.Count - offset);
            string[] batch = new string[count];
            for (int i = 0; i < count; i++)
            {
                batch[i] = texts[offset + i];
            }

            batchTasks[b] = SendBatchAsync(b, batchCount, batch, cancellationToken);
        }

        GeneratedEmbeddings<Embedding<float>>[] batchResults = await Task.WhenAll(batchTasks).ConfigureAwait(false);

        float[][] results = new float[texts.Count][];
        int idx = 0;
        foreach (GeneratedEmbeddings<Embedding<float>> batchResult in batchResults)
        {
            foreach (Embedding<float> embedding in batchResult)
            {
                results[idx++] = embedding.Vector.ToArray();
            }
        }

        return results;
    }

    private async Task<GeneratedEmbeddings<Embedding<float>>> SendBatchAsync(
        int batchIndex,
        int batchCount,
        string[] batch,
        CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LogBatchStart(logger, batchIndex, batchCount, batch.Length);
            return await retryPolicy.ExecuteAsync(
                ct => GenerateAndValidateAsync(batch, ct),
                (attempt, ex, delay) =>
                {
                    LogBatchRetry(logger, batchIndex, attempt, EmbeddingRetryPolicy.MaxAttempts, delay.TotalSeconds, ex);
                    string reason = GetRetryReason(ex);
                    retryCounter.Add(1, new KeyValuePair<string, object?>(TagReason, reason));
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            batchCounter.Add(1, new KeyValuePair<string, object?>(TagOutcome, OutcomeFailed));
            LogBatchFailed(logger, batchIndex, ex);
            throw;
        }
        finally
        {
            throttle.Release();
        }
    }

    private async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAndValidateAsync(
        string[] batch,
        CancellationToken cancellationToken)
    {
        var result = await generator.GenerateAsync(batch, null, cancellationToken).ConfigureAwait(false);

        if (result.Count != batch.Length)
        {
            throw new InvalidOperationException(
                $"Embedding provider returned {result.Count} embeddings for a batch of {batch.Length} inputs.");
        }

        foreach (var embedding in result.Where(embedding => embedding.Vector.Length != embeddingDimensions))
        {
            throw new InvalidOperationException(
                $"Embedding provider returned dimension {embedding.Vector.Length}, expected {embeddingDimensions}.");
        }

        batchCounter.Add(1, new KeyValuePair<string, object?>(TagOutcome, OutcomeOk));
        return result;
    }

    private static string GetRetryReason(Exception ex) => ex switch
    {
        System.ClientModel.ClientResultException { Status: 429 } => ReasonRateLimited,
        System.ClientModel.ClientResultException => ReasonServerError,
        _ => ReasonTransport
    };

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Generating embeddings batch {BatchIndex}/{BatchCount} ({BatchSize} texts)")]
    private static partial void LogBatchStart(ILogger logger, int batchIndex, int batchCount, int batchSize);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Embedding batch {BatchIndex} failed on attempt {Attempt}/{MaxAttempts}, retrying in {DelaySeconds:F1}s")]
    private static partial void LogBatchRetry(
        ILogger logger, int batchIndex, int attempt, int maxAttempts, double delaySeconds, Exception exception);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Embedding batch {BatchIndex} failed after all retry attempts")]
    private static partial void LogBatchFailed(ILogger logger, int batchIndex, Exception exception);
}
