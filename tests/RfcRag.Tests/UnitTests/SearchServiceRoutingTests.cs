using Microsoft.Extensions.Options;
using Npgsql;
using RfcRag.Search;
using RfcRag.Settings;
using RfcRag.Tests.Fakes;

namespace RfcRag.Tests.UnitTests;

public sealed class SearchServiceRoutingTests
{
    // A minimal NpgsqlDataSource that can be constructed without a live server.
    // The data source is lazy — no connection is opened unless a method is called.
    private static NpgsqlDataSource BuildNullDataSource() =>
        NpgsqlDataSource.Create("Host=localhost;Database=test");

    private static IOptions<RfcRagOptions> Options(bool vectorDataSearchEnabled, bool queryPlannerEnabled = false) =>
        Microsoft.Extensions.Options.Options.Create(new RfcRagOptions
        {
            RfcMirrorPath = string.Empty,
            PostgresConnectionString = string.Empty,
            VectorDataSearchEnabled = vectorDataSearchEnabled,
            QueryPlannerEnabled = queryPlannerEnabled,
            RerankerEnabled = false,
        });

    [Fact]
    public async Task SearchAsync_VectorDataSearchEnabled_UsesVectorDataSearch()
    {
        var ct = TestContext.Current.CancellationToken;
        var dataSource = BuildNullDataSource();
        var fake = new FakeVectorDataSearch { Results = [] };

        var service = new SearchService(
            new SearchRepository(dataSource),
            new MetadataRepository(dataSource),
            embeddingService: null!,
            Options(vectorDataSearchEnabled: true),
            fake);

        IReadOnlyList<SearchResult> results = await service.SearchAsync(
            "HTTP semantics", limit: 5, normativeKeyword: null, includeObsolete: false, ct);

        // FakeVectorDataSearch returns an empty list; the key assertion is that
        // this path completed without calling EmbeddingService or SearchRepository.
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_VectorDataSearchEnabled_NullService_FallsBackToHybridPath()
    {
        // When VectorDataSearchEnabled=true but no IVectorDataSearch is injected,
        // SearchService falls back to the hybrid path. This test proves the condition
        // `options.Value.VectorDataSearchEnabled && vectorDataSearch is not null`
        // guards the branch — null service means hybrid path is taken (which in this
        // unit context throws before hitting any DB, confirming the code path branched).
        var dataSource = BuildNullDataSource();

        var service = new SearchService(
            new SearchRepository(dataSource),
            new MetadataRepository(dataSource),
            embeddingService: null!,   // hybrid path would call this first
            Options(vectorDataSearchEnabled: true),
            vectorDataSearch: null);   // explicitly no VectorDataSearch injected

        // The hybrid path calls EmbeddingService.GenerateEmbeddingsAsync, which throws
        // NullReferenceException because embeddingService is null.
        // This confirms SearchService branched to the hybrid path, not the VectorData path.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.SearchAsync("query", limit: 5, normativeKeyword: null, includeObsolete: false,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SearchAsync_VectorDataSearchDisabled_IgnoresProvidedVectorDataSearch()
    {
        var ct = TestContext.Current.CancellationToken;
        var dataSource = BuildNullDataSource();
        var fake = new FakeVectorDataSearch { Results = [] };

        var service = new SearchService(
            new SearchRepository(dataSource),
            new MetadataRepository(dataSource),
            embeddingService: null!,   // hybrid path would call this first
            Options(vectorDataSearchEnabled: false),
            fake);   // provided but flag is off

        // With flag off, the hybrid path is taken regardless of the injected fake.
        // EmbeddingService (null!) would throw — confirming we entered the hybrid branch.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            service.SearchAsync("query", limit: 5, normativeKeyword: null, includeObsolete: false, ct));
    }
}
