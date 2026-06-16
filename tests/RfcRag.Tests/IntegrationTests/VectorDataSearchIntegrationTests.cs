using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Connectors.PgVector;
using RfcRag.Search;
using RfcRag.Settings;

namespace RfcRag.Tests.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class VectorDataSearchIntegrationTests : IClassFixture<MediumCorpusFixture>
{
    readonly MediumCorpusFixture fixture;

    public VectorDataSearchIntegrationTests(MediumCorpusFixture fixture)
    {
        this.fixture = fixture;
    }

    private PostgresCollection<Guid, RfcSectionRecord> BuildCollection()
    {
        return new PostgresCollection<Guid, RfcSectionRecord>(
            fixture.DataSource,
            "rfc_sections",
            ownsDataSource: false,
            new PostgresCollectionOptions { Schema = "rfc_rag" });
    }

    private VectorDataSearch BuildVectorDataSearch() =>
        new(BuildCollection(), fixture.EmbeddingService);

    [Fact]
    public async Task SearchAsync_RelevantQuery_ReturnsNonEmptyResults()
    {
        var ct = TestContext.Current.CancellationToken;
        var search = BuildVectorDataSearch();

        IReadOnlyList<SearchResult> results = await search.SearchAsync("HTTP request methods", 10, ct);

        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task SearchAsync_Results_AreOrderedByScoreDescending()
    {
        var ct = TestContext.Current.CancellationToken;
        var search = BuildVectorDataSearch();

        IReadOnlyList<SearchResult> results = await search.SearchAsync("TLS handshake", 10, ct);

        Assert.NotEmpty(results);
        for (int i = 1; i < results.Count; i++)
        {
            Assert.True(results[i - 1].Score >= results[i].Score,
                $"Result at index {i - 1} (score={results[i - 1].Score:F4}) should be >= result at index {i} (score={results[i].Score:F4})");
        }
    }

    [Fact]
    public async Task SearchAsync_ScoresAreInNormalizedRange()
    {
        var ct = TestContext.Current.CancellationToken;
        var search = BuildVectorDataSearch();

        IReadOnlyList<SearchResult> results = await search.SearchAsync("HTTP semantics", 5, ct);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.InRange(r.Score, 0.0, 1.0));
    }

    [Fact]
    public async Task SearchAsync_LimitIsHonored()
    {
        var ct = TestContext.Current.CancellationToken;
        var search = BuildVectorDataSearch();

        IReadOnlyList<SearchResult> results = await search.SearchAsync("TLS certificate", 3, ct);

        Assert.True(results.Count <= 3);
    }

    [Fact]
    public async Task SearchService_VectorDataSearchEnabled_ReturnsResults()
    {
        var ct = TestContext.Current.CancellationToken;
        var vectorSearch = BuildVectorDataSearch();

        var options = Options.Create(new RfcRagOptions
        {
            RfcMirrorPath = fixture.RfcMirrorPath,
            PostgresConnectionString = fixture.ConnectionString,
            EmbeddingDimensions = 1536,
            EmbeddingProvider = EmbeddingProvider.Local,
            VectorDataSearchEnabled = true,
        });

        var searchService = new SearchService(
            new SearchRepository(fixture.DataSource),
            new MetadataRepository(fixture.DataSource),
            fixture.EmbeddingService,
            options,
            vectorSearch);

        IReadOnlyList<SearchResult> results = await searchService.SearchAsync(
            "HTTP semantics", 10, null, false, ct);

        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task SearchService_VectorDataSearchDisabled_UsesHybridPath()
    {
        var ct = TestContext.Current.CancellationToken;

        // VectorDataSearchEnabled defaults to false — using fixture's SearchService
        IReadOnlyList<SearchResult> results = await fixture.SearchService.SearchAsync(
            "HTTP semantics", 10, null, false, ct);

        Assert.NotEmpty(results);
    }
}
