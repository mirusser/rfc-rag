namespace RfcRag.Infrastructure;

using Dapper;

internal sealed class RfcRagStartupService(
    IOptions<RfcRagOptions> options,
    NpgsqlDataSource dataSource,
    IIndexerService indexer,
    ISearchService searchService,
    ILoggerFactory loggerFactory)
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

        await ValidateEmbeddingDimensionsAsync(dataSource, opts.EmbeddingDimensions, logger)
            .ConfigureAwait(false);

        int cliArgIndex = Array.FindIndex(args,
            a => string.Equals(a, "--cli", StringComparison.OrdinalIgnoreCase));

        if (cliArgIndex >= 0)
        {
            return await HandleCliAsync(args, cliArgIndex, cancellationToken).ConfigureAwait(false);
        }

        int benchmarkArgIndex = Array.FindIndex(args,
            a => string.Equals(a, "--benchmark", StringComparison.OrdinalIgnoreCase));

        if (benchmarkArgIndex >= 0)
        {
            return await HandleBenchmarkAsync(args, benchmarkArgIndex, cancellationToken)
                .ConfigureAwait(false);
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

        logger.LogInformation(
            "Resetting embedding column to {Dimensions} dimensions.",
            targetDimensions);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var _ = transaction.ConfigureAwait(false);

            await connection.ExecuteAsync(
                $"""
                alter table rfc_rag.rfc_sections drop column if exists embedding;
                alter table rfc_rag.rfc_sections add column embedding vector({targetDimensions});
                """).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Embedding column reset to {Dimensions} dimensions. Starting full reindex.",
            targetDimensions);

        await indexer.IndexAllAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Reindex complete after embedding column reset.");
        return false;
    }

    private async Task<bool> HandleCliAsync(
        string[] args,
        int cliArgIndex,
        CancellationToken cancellationToken)
    {
        string[] cliArgs = args[(cliArgIndex + 1)..];
        var cliCommand = new CliCommand(searchService, loggerFactory.CreateLogger<CliCommand>());
        await cliCommand.RunAsync(cliArgs, cancellationToken).ConfigureAwait(false);
        return false;
    }

    private async Task<bool> HandleBenchmarkAsync(
        string[] args,
        int benchmarkArgIndex,
        CancellationToken cancellationToken)
    {
        string? queriesFilePath = benchmarkArgIndex + 1 < args.Length
            ? args[benchmarkArgIndex + 1]
            : null;

        if (string.IsNullOrWhiteSpace(queriesFilePath) || queriesFilePath.StartsWith("--", StringComparison.Ordinal))
        {
            logger.LogError("Usage: --benchmark <queries-file-path>");
            return false;
        }

        var benchmarkCommand = new BenchmarkCommand(
            searchService,
            loggerFactory.CreateLogger<BenchmarkCommand>());

        await benchmarkCommand.RunAsync(queriesFilePath, topK: 10, cancellationToken).ConfigureAwait(false);
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

    private static async Task ValidateEmbeddingDimensionsAsync(
        NpgsqlDataSource dataSource,
        int expectedDimensions,
        ILogger logger)
    {
        try
        {
            var connection = await dataSource.OpenConnectionAsync().ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                int? actualDimensions = await connection.QuerySingleOrDefaultAsync<int?>(
                    """
                    select a.atttypmod
                    from pg_attribute a
                    join pg_class c on c.oid = a.attrelid
                    join pg_namespace n on n.oid = c.relnamespace
                    where n.nspname = 'rfc_rag'
                      and c.relname = 'rfc_sections'
                      and a.attname = 'embedding'
                      and a.attnum > 0
                    """).ConfigureAwait(false);

                if (actualDimensions is not null && actualDimensions.Value != expectedDimensions)
                {
                    throw new InvalidOperationException(
                        $"RFC RAG embedding dimension mismatch: the 'rfc_sections.embedding' column expects " +
                        $"{actualDimensions} dimensions, but RfcRagOptions.EmbeddingDimensions is configured to " +
                        $"{expectedDimensions}. Update EmbeddingDimensions in your configuration to match, or " +
                        $"change the embedding model to produce {actualDimensions}-dimensional vectors.");
                }
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not validate RFC RAG embedding dimensions. " +
                "Skipping dimension check. Expected={ExpectedDimensions}",
                expectedDimensions);
        }
    }
}
