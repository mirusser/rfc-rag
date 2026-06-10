using System.Text.Json;
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

public sealed class RetrievalQualityFixture : IAsyncLifetime
{
    private static readonly int[] TrackedRfcNumbers = [1035, 2119, 3986, 5681, 8446, 9000, 9110];

    private const string PostgresImage = "pgvector/pgvector:pg17";

    private PostgreSqlContainer? container;
    private NpgsqlDataSource? dataSource;
    private string? tempRfcDir;

    public ISearchService SearchService { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        container = new PostgreSqlBuilder(PostgresImage).Build();
        await container.StartAsync(TestContext.Current.CancellationToken);

        dataSource = NpgsqlDataSource.Create(container.GetConnectionString());
        await RfcRagMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);

        tempRfcDir = CreateTempRfcDirectory();
        await IndexRfcsAsync(dataSource, tempRfcDir);

        var repository = new SearchRepository(dataSource);
        var metadataRepository = new MetadataRepository(dataSource);
        var embeddingService = new EmbeddingService(
            new SemanticFakeEmbeddingGenerator(), new EmbeddingRetryPolicy(TimeProvider.System),
            5, embeddingDimensions: 1536, maxConcurrency: 1, NullLogger<EmbeddingService>.Instance);
        SearchService = new SearchService(repository, metadataRepository, embeddingService);
    }

    public async ValueTask DisposeAsync()
    {
        if (dataSource is not null)
            await dataSource.DisposeAsync();
        if (container is not null)
            await container.DisposeAsync();
        if (tempRfcDir is not null && Directory.Exists(tempRfcDir))
            Directory.Delete(tempRfcDir, recursive: true);
    }

    private static string CreateTempRfcDirectory()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"rfc-rag-quality-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        foreach (int rfcNumber in TrackedRfcNumbers)
        {
            string sourcePath = Path.Combine("TestData", $"rfc{rfcNumber}.txt");
            if (File.Exists(sourcePath))
                File.Copy(sourcePath, Path.Combine(tempDir, $"rfc{rfcNumber}.txt"));
        }

        return tempDir;
    }

    private static async Task IndexRfcsAsync(NpgsqlDataSource dataSource, string rfcDir)
    {
        var embeddingService = new EmbeddingService(
            new SemanticFakeEmbeddingGenerator(), new EmbeddingRetryPolicy(TimeProvider.System),
            5, embeddingDimensions: 1536, maxConcurrency: 1, NullLogger<EmbeddingService>.Instance);

        var options = Options.Create(new RfcRagOptions
        {
            RfcMirrorPath = rfcDir,
            PostgresConnectionString = string.Empty,
            EmbeddingBatchSize = 5,
            MaxIndexingParallelism = 1
        });

        var indexer = new RfcIndexer(
            dataSource,
            new IndexingRepository(dataSource),
            new RfcParser(),
            new RfcXmlParser(),
            embeddingService,
            options,
            NullLogger<RfcIndexer>.Instance);

        await indexer.IndexAllAsync(CancellationToken.None);
    }
}

[Trait("Category", "RetrievalQuality")]
public sealed class RetrievalQualityTests(RetrievalQualityFixture fixture) : IClassFixture<RetrievalQualityFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record class EvalQuery(string Query, int[] ExpectedRfcAny);

    [Theory]
    [MemberData(nameof(GetFixtureQueries))]
    public async Task SearchAsync_WithFixtureQuery_HitsExpectedRfc(string query, int[] expectedRfcAny)
    {
        IReadOnlyList<SearchResult> results = await fixture.SearchService
            .SearchAsync(query, limit: 10, normativeKeyword: null, CancellationToken.None);

        var rfcNumbers = results.Select(r => r.RfcNumber).ToHashSet();
        bool hit = expectedRfcAny.Any(rfcNumbers.Contains);

        Assert.True(hit,
            $"Query '{query}' — expected one of [{string.Join(", ", expectedRfcAny)}] in top-10. " +
            $"Got: [{string.Join(", ", rfcNumbers.OrderBy(n => n))}]");
    }

    public static IEnumerable<object[]> GetFixtureQueries()
    {
        string fixturePath = Path.Combine("eval", "retrieval_queries.json");
        string json = File.ReadAllText(fixturePath);
        var queries = JsonSerializer.Deserialize<EvalQueryJson[]>(json, JsonOptions) ?? [];

        return queries.Select(q => new object[] { q.Query, q.ExpectedRfcAny });
    }

    private sealed class EvalQueryJson
    {
        public string Query { get; set; } = string.Empty;
        public int[] ExpectedRfcAny { get; set; } = [];
    }
}
