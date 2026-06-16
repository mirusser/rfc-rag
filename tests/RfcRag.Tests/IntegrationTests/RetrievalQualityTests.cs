using System.Text.Json;
using RfcRag.Evaluation;
using RfcRag.Indexing;
using RfcRag.Infrastructure;
using RfcRag.Parsing;
using RfcRag.Search;
using RfcRag.Settings;
using RfcRag.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Connectors.PgVector;
using Npgsql;
using Testcontainers.PostgreSql;

namespace RfcRag.Tests.IntegrationTests;

public sealed class RetrievalQualityFixture : IAsyncLifetime
{
    private static readonly int[] TrackedRfcNumbers = [1035, 2119, 3986, 5681, 7231, 8446, 9000, 9110];

    private const string PostgresImage = "pgvector/pgvector:pg17";

    private PostgreSqlContainer? container;
    private NpgsqlDataSource? dataSource;
    private string? tempRfcDir;

    public ISearchService SearchService { get; private set; } = null!;
    public ISearchService VectorDataSearchService { get; private set; } = null!;


    public NpgsqlDataSource DataSource =>
        dataSource ?? throw new InvalidOperationException("Fixture is not initialized.");

    public async ValueTask InitializeAsync()
    {
        container = new PostgreSqlBuilder(PostgresImage).Build();
        await container.StartAsync(TestContext.Current.CancellationToken);

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(container.GetConnectionString());
        dataSourceBuilder.UseVector();
        dataSource = dataSourceBuilder.Build();
        await RfcRagMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);
        await dataSource.ReloadTypesAsync(CancellationToken.None);

        tempRfcDir = CreateTempRfcDirectory();
        await IndexRfcsAsync(dataSource, tempRfcDir);

        var repository = new SearchRepository(dataSource);
        var metadataRepository = new MetadataRepository(dataSource);
        var embeddingService = new EmbeddingService(
            new SemanticFakeEmbeddingGenerator(), new EmbeddingRetryPolicy(TimeProvider.System),
            5, embeddingDimensions: 1536, maxConcurrency: 1, NullLogger<EmbeddingService>.Instance);
        var searchOptions = Options.Create(new RfcRagOptions
        {
            RfcMirrorPath = tempRfcDir,
            PostgresConnectionString = container.GetConnectionString(),
        });
        SearchService = new SearchService(repository, metadataRepository, embeddingService, searchOptions);

        var vectorDataSearch = new VectorDataSearch(
            new PostgresCollection<Guid, RfcSectionRecord>(
                dataSource,
                "rfc_sections",
                ownsDataSource: false,
                new PostgresCollectionOptions { Schema = "rfc_rag" }),
            embeddingService);
        var vectorDataSearchOptions = Options.Create(new RfcRagOptions
        {
            RfcMirrorPath = tempRfcDir,
            PostgresConnectionString = container.GetConnectionString(),
            VectorDataSearchEnabled = true,
        });
        VectorDataSearchService = new SearchService(
            repository,
            metadataRepository,
            embeddingService,
            vectorDataSearchOptions,
            vectorDataSearch);
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
        string tempDir = Path.Join(Path.GetTempPath(), $"rfc-rag-quality-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        foreach (int rfcNumber in TrackedRfcNumbers)
        {
            string sourcePath = Path.Join("TestData", $"rfc{rfcNumber}.txt");
            if (File.Exists(sourcePath))
                File.Copy(sourcePath, Path.Join(tempDir, $"rfc{rfcNumber}.txt"));
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

[Trait("Category", "Integration")]
[Trait("Category", "RetrievalQuality")]
public sealed class RetrievalQualityTests(RetrievalQualityFixture fixture) : IClassFixture<RetrievalQualityFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Theory]
    [MemberData(nameof(GetFixtureQueries))]
    public async Task SearchAsync_WithFixtureQuery_HitsExpectedRfc(string query, int[] expectedRfcAny)
    {
        IReadOnlyList<SearchResult> results = await fixture.SearchService
            .SearchAsync(query, limit: 10, normativeKeyword: null, includeObsolete: false, CancellationToken.None);

        var rfcNumbers = results.Select(r => r.RfcNumber).ToHashSet();
        bool hit = expectedRfcAny.Any(rfcNumbers.Contains);

        Assert.True(hit,
            $"Query '{query}' — expected one of [{string.Join(", ", expectedRfcAny)}] in top-10. " +
            $"Got: [{string.Join(", ", rfcNumbers.OrderBy(n => n))}]");
    }

    [Fact]
    public async Task SearchAsync_RfcSectionReference_ReturnsReferencedSectionFirst()
    {
        IReadOnlyList<SearchResult> results = await fixture.SearchService
            .SearchAsync("What does RFC 9110 section 9.3.1 say?", limit: 10, normativeKeyword: null, includeObsolete: false, CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Equal(9110, results[0].RfcNumber);
        Assert.Equal("9.3.1", results[0].Section);
    }

    public static IEnumerable<object[]> GetFixtureQueries()
    {
        string fixturePath = Path.Join("eval", "retrieval_queries.json");
        string json = File.ReadAllText(fixturePath);
        var queries = JsonSerializer.Deserialize<EvalQueryJson[]>(json, JsonOptions) ?? [];

        return queries.Select(q => new object[] { q.Query, q.ExpectedRfcAny });
    }

    private sealed class EvalQueryJson
    {
        public string Query { get; set; } = string.Empty;
        public int[] ExpectedRfcAny { get; set; } = [];
    }

    [Fact]
    public async Task GoldenQuestions_TestdataCorpus_MeasuresHybridAndVectorDataModes()
    {
        string fixturePath = Path.Join("eval", "golden_questions.json");
        string json = await File.ReadAllTextAsync(fixturePath, CancellationToken.None);
        var allQuestions = JsonSerializer.Deserialize<GoldenQuestion[]>(json, JsonOptions) ?? [];

        // Only testdata questions with RFC expectations (skip no_answer: they have empty expectedRfcs)
        var scorableQuestions = allQuestions
            .Where(q => string.Equals(q.Corpus, "testdata", StringComparison.Ordinal)
                && q.ExpectedRfcs.Length > 0)
            .ToArray();

        Assert.True(scorableQuestions.Length >= 10, "Expected at least 10 scorable testdata questions.");

        RetrievalModeMetrics hybridMetrics = await MeasureGoldenQuestionsAsync(
            fixture.SearchService,
            scorableQuestions,
            CancellationToken.None);
        RetrievalModeMetrics vectorDataMetrics = await MeasureGoldenQuestionsAsync(
            fixture.VectorDataSearchService,
            scorableQuestions,
            CancellationToken.None);

        // Thresholds measured from SemanticFakeEmbeddingGenerator baseline (docs/eval/reports/baseline-testdata.json).
        // Do not lower without a measured regression justification.
        Assert.True(hybridMetrics.Aggregate.HitAt10 >= 0.90,
            $"hybrid hit@10={hybridMetrics.Aggregate.HitAt10:F3} is below the 0.90 baseline threshold.");
        Assert.True(hybridMetrics.Aggregate.Mrr >= 0.75,
            $"hybrid MRR={hybridMetrics.Aggregate.Mrr:F3} is below the 0.75 baseline threshold.");

        // VectorData is additive pure-vector retrieval. It is measured for visibility, but not
        // required to match the hybrid SQL retrieval baseline.
        Assert.Equal(scorableQuestions.Length, vectorDataMetrics.QueriesRun);
        Assert.InRange(vectorDataMetrics.Aggregate.HitAt10, 0.0, 1.0);
        Assert.InRange(vectorDataMetrics.Aggregate.Mrr, 0.0, 1.0);
    }

    private static async Task<RetrievalModeMetrics> MeasureGoldenQuestionsAsync(
        ISearchService searchService,
        GoldenQuestion[] scorableQuestions,
        CancellationToken cancellationToken)
    {
        var results = new List<RetrievalQueryResult>();
        foreach (var question in scorableQuestions)
        {
            IReadOnlyList<SearchResult> searchResults = await searchService
                .SearchAsync(question.Question, limit: 10, normativeKeyword: null, includeObsolete: question.IncludeObsolete, cancellationToken);

            int[] rankedRfcs = searchResults.Select(r => r.RfcNumber).Distinct().ToArray();

            results.Add(new RetrievalQueryResult(
                question.Id, question.Question, question.Corpus,
                HitAt1: RetrievalMetrics.HitAtK(rankedRfcs, question.ExpectedRfcs, k: 1),
                HitAt5: RetrievalMetrics.HitAtK(rankedRfcs, question.ExpectedRfcs, k: 5),
                HitAt10: RetrievalMetrics.HitAtK(rankedRfcs, question.ExpectedRfcs, k: 10),
                ReciprocalRank: RetrievalMetrics.ReciprocalRank(rankedRfcs, question.ExpectedRfcs),
                NdcgAt10: RetrievalMetrics.NdcgAtK(rankedRfcs, question.ExpectedRfcs, k: 10),
                LatencyMs: 0,
                TopKRfcs: rankedRfcs,
                Error: null));
        }

        RetrievalAggregateMetrics aggregate = RetrievalMetrics.Aggregate(results);

        return new RetrievalModeMetrics(aggregate, results.Count);
    }

    private sealed record class RetrievalModeMetrics(RetrievalAggregateMetrics Aggregate, int QueriesRun);
}
