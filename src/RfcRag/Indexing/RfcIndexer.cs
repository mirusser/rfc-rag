using System.Security.Cryptography;
using System.Text;
using RfcRag.Infrastructure;

namespace RfcRag.Indexing;

internal sealed class RfcIndexer(
    NpgsqlDataSource dataSource,
    IndexingRepository repository,
    RfcParser parser,
    RfcXmlParser xmlParser,
    EmbeddingService embeddingService,
    IOptions<RfcRagOptions> options,
    ILogger<RfcIndexer> logger) : IIndexerService
{
    private readonly RfcRagOptions options = options.Value;

    public async Task IndexAllAsync(CancellationToken cancellationToken)
    {
        string mirrorPath = RfcSourceResolver.ExpandPath(options.RfcMirrorPath);
        if (!Directory.Exists(mirrorPath))
        {
            throw new DirectoryNotFoundException($"RFC mirror path '{mirrorPath}' does not exist.");
        }

        IReadOnlyList<RfcSourceResolver.RfcSourceFile> sourceFiles =
            RfcSourceResolver.Resolve(mirrorPath, options.RfcParserType);

        // Single query to load all existing hashes, avoiding N individual SELECTs during the parallel loop
        Dictionary<int, string> indexedHashes = await repository
            .GetAllIndexedHashesAsync(cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation("Indexing RFC source files from {MirrorPath}", mirrorPath);

        var parallelOptions = new ParallelOptions
        {
            // I/O-bound (embedding HTTP + DB writes): higher concurrency than CPU count is correct
            MaxDegreeOfParallelism = options.MaxIndexingParallelism,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            sourceFiles,
            parallelOptions,
            async (sourceFile, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                logger.LogDebug("Indexing RFC {RfcNumber}...", sourceFile.RfcNumber);
                await IndexFileAsync(sourceFile, mirrorPath, force: false, indexedHashes, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);

        int rfcCount = await repository.GetIndexedCountAsync(cancellationToken).ConfigureAwait(false);
        int sectionCount = await repository.GetIndexedSectionCountAsync(cancellationToken).ConfigureAwait(false);

        string manifestParserType = sourceFiles.Any(sourceFile =>
            sourceFile.Path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            ? options.RfcParserType.ToString()
            : nameof(RfcParserType.Text);

        await repository.InsertManifestAsync(
            mirrorPath,
            manifestParserType,
            parserVersion: RfcRagConventions.ParserVersion,
            options.EmbeddingProvider.ToString(),
            options.EmbeddingModel,
            options.EmbeddingDimensions,
            options.EmbeddingBatchSize,
            rfcCount,
            sectionCount,
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Indexing run complete: {RfcCount} RFCs, {SectionCount} sections indexed",
            rfcCount,
            sectionCount);
    }

    public async Task IndexSingleAsync(int rfcNumber, bool force, CancellationToken cancellationToken)
    {
        string mirrorPath = RfcSourceResolver.ExpandPath(options.RfcMirrorPath);
        if (!Directory.Exists(mirrorPath))
        {
            throw new DirectoryNotFoundException($"RFC mirror path '{mirrorPath}' does not exist.");
        }

        IReadOnlyList<RfcSourceResolver.RfcSourceFile> all =
            RfcSourceResolver.Resolve(mirrorPath, options.RfcParserType);

        RfcSourceResolver.RfcSourceFile? sourceFile = all.FirstOrDefault(f => f.RfcNumber == rfcNumber);
        if (sourceFile is null)
        {
            throw new FileNotFoundException($"RFC {rfcNumber} not found in mirror '{mirrorPath}'.");
        }

        await IndexFileAsync(sourceFile.Value, mirrorPath, force, cachedHashes: null, cancellationToken).ConfigureAwait(false);

        int rfcCount = await repository.GetIndexedCountAsync(cancellationToken).ConfigureAwait(false);
        int sectionCount = await repository.GetIndexedSectionCountAsync(cancellationToken).ConfigureAwait(false);

        await repository.InsertManifestAsync(
            mirrorPath,
            options.RfcParserType.ToString(),
            parserVersion: RfcRagConventions.ParserVersion,
            options.EmbeddingProvider.ToString(),
            options.EmbeddingModel,
            options.EmbeddingDimensions,
            options.EmbeddingBatchSize,
            rfcCount,
            sectionCount,
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Single-RFC indexing complete: {RfcCount} RFCs, {SectionCount} sections indexed",
            rfcCount,
            sectionCount);
    }

    public Task<int> GetIndexedCountAsync(CancellationToken cancellationToken) =>
        repository.GetIndexedCountAsync(cancellationToken);

    private async Task IndexFileAsync(
        RfcSourceResolver.RfcSourceFile sourceFile,
        string mirrorPath,
        bool force,
        IReadOnlyDictionary<int, string>? cachedHashes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Read file bytes once — used for both SHA-256 and parsing (avoids a second file read)
        byte[] fileBytes = await File.ReadAllBytesAsync(sourceFile.Path, cancellationToken).ConfigureAwait(false);
        string sourceSha256 = Convert.ToHexString(SHA256.HashData(fileBytes)).ToUpperInvariant();

        if (!force)
        {
            string? indexedHash = cachedHashes is not null
                ? cachedHashes.GetValueOrDefault(sourceFile.RfcNumber)
                : await repository.GetIndexedRfcHashAsync(sourceFile.RfcNumber, cancellationToken).ConfigureAwait(false);

            if (string.Equals(indexedHash, sourceSha256, StringComparison.Ordinal))
            {
                logger.LogDebug("Skipping unchanged RFC {RfcNumber}", sourceFile.RfcNumber);
                return;
            }
        }

        logger.LogInformation("Indexing RFC {RfcNumber}...", sourceFile.RfcNumber);

        // Decode text from already-read bytes; no second file read needed
        string rawText = Encoding.UTF8.GetString(fileBytes);
        string sourceFileName = Path.GetFileName(sourceFile.Path);
        bool isXml = sourceFileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
        RfcDocument document = isXml
            ? xmlParser.ParseContent(rawText, sourceFileName)
            : parser.ParseContent(rawText, sourceFileName);

        string relativePath = Path.GetRelativePath(mirrorPath, sourceFile.Path);
        IReadOnlyList<string> sectionTexts = document.Sections.Select(section => section.Text).ToArray();
        IReadOnlyList<float[]> embeddings = await embeddingService.GenerateEmbeddingsAsync(
            sectionTexts,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<RfcSection> sections = document.Sections
            .Select((section, sectionIndex) => section with
            {
                SourcePath = sourceFile.Path,
                SourceSha256 = sourceSha256,
                Embedding = embeddings[sectionIndex]
            })
            .ToArray();

        await StoreDocumentAsync(
            document,
            sections,
            relativePath,
            sourceSha256,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task StoreDocumentAsync(
        RfcDocument document,
        IReadOnlyList<RfcSection> sections,
        string sourcePath,
        string sourceSha256,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                try
                {
                    await repository.DeleteByRfcNumberAsync(
                        connection,
                        transaction,
                        document.Metadata.Number,
                        cancellationToken).ConfigureAwait(false);

                    await repository.InsertSectionsAsync(
                        connection,
                        transaction,
                        sections,
                        cancellationToken).ConfigureAwait(false);

                    await IndexingRepository.InsertAbnfBlocksAsync(
                        connection,
                        transaction,
                        document.AbnfBlocks,
                        cancellationToken).ConfigureAwait(false);

                    await IndexingRepository.InsertNormativeOccurrencesAsync(
                        connection,
                        transaction,
                        document.NormativeOccurrences,
                        cancellationToken).ConfigureAwait(false);

                    await repository.UpsertIndexedRfcAsync(
                        connection,
                        transaction,
                        new IndexedRfcData(
                            document.Metadata.Number,
                            sourcePath,
                            sourceSha256,
                            document.Metadata.Title,
                            sections.Count,
                            document.Metadata.Updates,
                            document.Metadata.Obsoletes,
                            document.Metadata.Date,
                            document.Metadata.Category,
                            document.Metadata.Authors,
                            document.Metadata.Issn,
                            document.Metadata.GrammarStyle),
                        cancellationToken).ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
    }
}
