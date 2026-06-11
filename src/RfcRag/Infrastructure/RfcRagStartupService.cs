namespace RfcRag.Infrastructure;

internal sealed class RfcRagStartupService(
    IOptions<RfcRagOptions> options,
    NpgsqlDataSource dataSource,
    IIndexerService indexer,
    ILoggerFactory loggerFactory,
    CliCommandRouter cliCommandRouter)
{
    private readonly ILogger logger = loggerFactory.CreateLogger("RfcRag");

    /// <summary>
    /// Runs startup orchestration: migrations, dimension validation, --reindex, and indexing.
    /// Returns <c>true</c> if the application should continue to <c>app.RunAsync</c>,
    /// or <c>false</c> if an early exit was requested (e.g., --reindex).
    /// </summary>
    public async Task<bool> RunStartupAsync(string[] args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);

        var opts = options.Value;

        if (opts.RunMigrationsOnStartup)
        {
            logger.LogInformation("Applying RFC RAG PostgreSQL migrations.");
            await RfcRagMigrationRunner.ApplyAsync(dataSource, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("RFC RAG PostgreSQL migrations applied.");
        }

        bool resetEmbeddings = args.Any(a =>
            string.Equals(a, "--reset-embeddings", StringComparison.OrdinalIgnoreCase));

        if (resetEmbeddings)
        {
            return await HandleResetEmbeddingsAsync(args, opts.EmbeddingDimensions, cancellationToken)
                .ConfigureAwait(false);
        }

        await RfcRagMigrationRunner.ValidateEmbeddingDimensionsAsync(
            dataSource, opts.EmbeddingDimensions, logger, cancellationToken).ConfigureAwait(false);

        if (await cliCommandRouter.TryHandleAsync(args, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        string? reindexArg = args.FirstOrDefault(a =>
            a.StartsWith("--reindex", StringComparison.OrdinalIgnoreCase));
        bool forceReindex = args.Any(a =>
            string.Equals(a, "--force", StringComparison.OrdinalIgnoreCase));

        if (reindexArg is not null)
        {
            return await HandleReindexAsync(reindexArg, forceReindex, cancellationToken)
                .ConfigureAwait(false);
        }

        bool hasEmbeddingCapability = opts.EmbeddingProvider == EmbeddingProvider.Local
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RfcRagOptions.OpenRouterApiKeyEnvironmentVariable));

        if (!hasEmbeddingCapability)
        {
            logger.LogWarning(
                "{EnvVar} is not set. Skipping startup indexing. " +
                "Search queries against already-indexed data will still work. " +
                "Set this environment variable, or set RfcRag__EmbeddingProvider=Local, to enable embedding generation.",
                RfcRagOptions.OpenRouterApiKeyEnvironmentVariable);
        }
        else
        {
            int beforeCount = await indexer.GetIndexedCountAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Starting RFC RAG indexing. IndexedRfcsBefore={IndexedRfcsBefore}", beforeCount);
            await indexer.IndexAllAsync(cancellationToken).ConfigureAwait(false);
            int afterCount = await indexer.GetIndexedCountAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("RFC RAG indexing complete. IndexedRfcsAfter={IndexedRfcsAfter}", afterCount);
        }

        return true;
    }

    private async Task<bool> HandleResetEmbeddingsAsync(
        string[] args,
        int targetDimensions,
        CancellationToken cancellationToken)
    {
        bool confirmed = args.Any(a =>
            string.Equals(a, "--confirm", StringComparison.OrdinalIgnoreCase));

        if (!confirmed)
        {
            logger.LogWarning(
                "The --reset-embeddings flag will drop and recreate the 'rfc_sections.embedding' column " +
                "at dimension {Dimensions}, destroying all existing embedding data. " +
                "Re-run with --reset-embeddings --confirm to proceed.",
                targetDimensions);
            return false;
        }

        logger.LogInformation("Resetting embedding column to {Dimensions} dimensions.", targetDimensions);
        await RfcRagMigrationRunner.ResetEmbeddingColumnAsync(dataSource, targetDimensions, cancellationToken)
            .ConfigureAwait(false);
        await indexer.IndexAllAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Embedding column reset to {Dimensions} dimensions. Reindex complete.",
            targetDimensions);
        return false;
    }

    private async Task<bool> HandleReindexAsync(
        string reindexArg,
        bool forceReindex,
        CancellationToken cancellationToken)
    {
        string? numberStr = reindexArg.Contains('=', StringComparison.Ordinal)
            ? reindexArg.Split('=', 2)[1]
            : null;

        if (numberStr is null || !int.TryParse(numberStr, out int reindexRfcNumber))
        {
            logger.LogError("Usage: --reindex=<rfc-number> [--force]");
            return false;
        }

        logger.LogInformation(
            "Reindexing single RFC {RfcNumber} (Force={Force}).",
            reindexRfcNumber,
            forceReindex);
        await indexer.IndexSingleAsync(reindexRfcNumber, forceReindex, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation("Reindex complete for RFC {RfcNumber}.", reindexRfcNumber);
        return false;
    }
}
