using Dapper;
using RfcRag.Indexing;
using RfcRag.Infrastructure;
using RfcRag.Parsing;
using RfcRag.Search;
using RfcRag.Settings;
using RfcRag.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace RfcRag.Tests.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class EmbeddingDimensionMigrationTests : IAsyncLifetime
{
    private const string PostgresImage = "pgvector/pgvector:pg17";
    private PostgreSqlContainer? container;
    private NpgsqlDataSource? dataSource;

    public async ValueTask InitializeAsync()
    {
        container = new PostgreSqlBuilder(PostgresImage).Build();
        await container.StartAsync(TestContext.Current.CancellationToken);
        dataSource = NpgsqlDataSource.Create(container.GetConnectionString());
        await RfcRagMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (dataSource is not null)
            await dataSource.DisposeAsync();
        if (container is not null)
            await container.DisposeAsync();
    }

    [Fact]
    public async Task ResetEmbeddings_WithoutConfirm_DoesNotAlterColumn()
    {
        int? originalDim = await GetEmbeddingDimensionAsync();
        var startup = CreateStartupService(targetDimensions: 384);

        await startup.RunStartupAsync(["--reset-embeddings"], CancellationToken.None);

        int? dimAfter = await GetEmbeddingDimensionAsync();
        Assert.Equal(originalDim, dimAfter);
    }

    [Fact]
    public async Task ResetEmbeddings_WithConfirm_ChangesColumnDimension()
    {
        var startup = CreateStartupService(targetDimensions: 384);

        await startup.RunStartupAsync(["--reset-embeddings", "--confirm"], CancellationToken.None);

        int? newDim = await GetEmbeddingDimensionAsync();
        Assert.Equal(384, newDim);
    }

    [Fact]
    public async Task ResetEmbeddings_WithConfirm_ReturnsFalse()
    {
        var startup = CreateStartupService(targetDimensions: 384);

        bool shouldContinue = await startup.RunStartupAsync(
            ["--reset-embeddings", "--confirm"], CancellationToken.None);

        Assert.False(shouldContinue);
    }

    [Fact]
    public async Task ResetEmbeddings_WithoutConfirm_ReturnsFalse()
    {
        var startup = CreateStartupService(targetDimensions: 384);

        bool shouldContinue = await startup.RunStartupAsync(
            ["--reset-embeddings"], CancellationToken.None);

        Assert.False(shouldContinue);
    }

    private async Task<int?> GetEmbeddingDimensionAsync()
    {
        var connection = await dataSource!.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using (connection)
        {
            return await connection.QuerySingleOrDefaultAsync<int?>(
                """
                select a.atttypmod
                from pg_attribute a
                join pg_class c on c.oid = a.attrelid
                join pg_namespace n on n.oid = c.relnamespace
                where n.nspname = 'rfc_rag'
                  and c.relname = 'rfc_sections'
                  and a.attname = 'embedding'
                  and a.attnum > 0
                """);
        }
    }

    private RfcRagStartupService CreateStartupService(int targetDimensions)
    {
        var options = Options.Create(new RfcRagOptions
        {
            RfcMirrorPath = Path.Combine(Directory.GetCurrentDirectory(), "TestData"),
            PostgresConnectionString = container!.GetConnectionString(),
            EmbeddingDimensions = targetDimensions,
            RunMigrationsOnStartup = false
        });

        var embeddingService = new EmbeddingService(
            new FakeEmbeddingGenerator(targetDimensions), 5, maxConcurrency: 1, NullLogger<EmbeddingService>.Instance);

        var indexer = new RfcIndexer(
            dataSource!,
            new IndexingRepository(dataSource!),
            new RfcParser(),
            new RfcXmlParser(),
            embeddingService,
            options,
            NullLogger<RfcIndexer>.Instance);

        var searchService = new SearchService(
            new SearchRepository(dataSource!),
            new MetadataRepository(dataSource!),
            embeddingService,
            NullLogger<SearchService>.Instance);

        return new RfcRagStartupService(
            options,
            dataSource!,
            indexer,
            searchService,
            NullLoggerFactory.Instance);
    }
}
