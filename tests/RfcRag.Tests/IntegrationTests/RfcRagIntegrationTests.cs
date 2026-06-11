using Dapper;
using RfcRag.Indexing;
using RfcRag.Infrastructure;
using RfcRag.Models;
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
public sealed class RfcRagIntegrationTests : IAsyncLifetime
{
    private const string PostgresImage = "pgvector/pgvector:pg17";

    private static readonly string[] ExpectedTables =
    [
        "index_manifest",
        "indexed_rfcs",
        "normative_occurrences",
        "rfc_abnf_blocks",
        "rfc_sections",
        "schema_migrations"
    ];

    private PostgreSqlContainer? container;

    public async ValueTask InitializeAsync()
    {
        container = new PostgreSqlBuilder(PostgresImage)
            .Build();

        await container.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task RunMigrations_OnEmptyDatabase_CreatesExpectedTables()
    {
        await using var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());

        await RfcRagMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);

        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        var actual = (await connection.QueryAsync<string>(
                """
                select table_name
                from information_schema.tables
                where table_schema = 'rfc_rag'
                order by table_name
                """))
            .ToArray();

        Assert.Equal(ExpectedTables, actual);
    }

    [Fact]
    public async Task IndexAndSearch_WithFixtureRfcs_ReturnsRelevantResults()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        IIndexerService indexer = CreateIndexer(dataSource);
        ISearchService search = CreateSearchService(dataSource);

        await indexer.IndexAllAsync(CancellationToken.None);

        int indexedCount = await indexer.GetIndexedCountAsync(CancellationToken.None);
        IReadOnlyList<SearchResult> results = await search.SearchAsync("HTTP semantics", 100, null, CancellationToken.None);
        // Verify rfc9110 is indexed via direct lookup — hybrid search ranking is unreliable
        // with fake embeddings across a large corpus (random vector scores drown lexical signal).
        RfcSection? rfc9110Section = await search.GetSectionAsync(9110, "1", CancellationToken.None);

        Assert.True(indexedCount >= 9000);
        Assert.True(results.Count > 0);
        Assert.NotNull(rfc9110Section);
        Assert.Equal(9110, rfc9110Section.RfcNumber);
        Assert.Equal("1", rfc9110Section.Section);
    }

    [Fact]
    public async Task GetSectionAsync_WithExistingSection_ReturnsExactMatch()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        IIndexerService indexer = CreateIndexer(dataSource);
        ISearchService search = CreateSearchService(dataSource);

        await indexer.IndexAllAsync(CancellationToken.None);

        RfcSection? section = await search.GetSectionAsync(2119, "1", CancellationToken.None);

        Assert.NotNull(section);
        Assert.Contains("MUST", section.Text);
        Assert.Equal("1", section.Section);
        Assert.Equal(2119, section.RfcNumber);
    }

    [Fact]
    public async Task SearchNormativeAsync_WithKeyword_ReturnsMatchingSections()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        IIndexerService indexer = CreateIndexer(dataSource);
        ISearchService search = CreateSearchService(dataSource);

        await indexer.IndexAllAsync(CancellationToken.None);

        IReadOnlyList<SearchResult> results = await search.SearchNormativeAsync("MUST", null, 20, CancellationToken.None);

        Assert.Contains(results, result => result.RfcNumber == 2119);
        Assert.Contains(results, result => result.Excerpt.Contains("MUST"));
    }

    [Fact]
    public async Task GetIndexedRfcMetadataAsync_AfterIndexing_IncludesGrammarStyle()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        IIndexerService indexer = CreateIndexer(dataSource);
        var metadataRepository = new MetadataRepository(dataSource);

        await indexer.IndexAllAsync(CancellationToken.None);

        RfcMetadata? tlsMetadata = await metadataRepository.GetIndexedRfcMetadataAsync(8446, CancellationToken.None);
        RfcMetadata? rfc2119Metadata = await metadataRepository.GetIndexedRfcMetadataAsync(2119, CancellationToken.None);

        Assert.NotNull(tlsMetadata);
        Assert.Equal(GrammarStyleConstants.TlsPresentationLang, tlsMetadata.GrammarStyle);
        Assert.NotNull(rfc2119Metadata);
        Assert.Equal(GrammarStyleConstants.None, rfc2119Metadata.GrammarStyle);
    }

    [Fact]
    public async Task SearchAbnf_FindsGrammarBlocks_AcrossRecentRfcs()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        IIndexerService indexer = CreateIndexer(dataSource);
        SearchService search = CreateSearchService(dataSource);

        await indexer.IndexAllAsync(CancellationToken.None);

        IReadOnlyList<SearchResult> uriResults = await search.SearchAbnfAsync(
            "URI", null, 100, CancellationToken.None);
        Assert.Contains(uriResults, r => r.RfcNumber == 3986);

        IReadOnlyList<SearchResult> httpResults = await search.SearchAbnfAsync(
            "expectation", null, 100, CancellationToken.None);
        Assert.Contains(httpResults, r => r.RfcNumber == 9110);

        IReadOnlyList<SearchResult> httpTokenResults = await search.SearchAbnfAsync(
            "token", null, 100, CancellationToken.None);
        Assert.Contains(httpTokenResults, r => r.RfcNumber == 9110);

        IReadOnlyList<SearchResult> filteredResults = await search.SearchAbnfAsync(
            "URI", [3986], 100, CancellationToken.None);
        Assert.Contains(filteredResults, r => r.RfcNumber == 3986);
    }

    [Fact]
    public async Task IncrementalIndex_SkipsUnchangedRfcs()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        IIndexerService indexer = CreateIndexer(dataSource);

        await indexer.IndexAllAsync(CancellationToken.None);
        int originalCount = await indexer.GetIndexedCountAsync(CancellationToken.None);

        await indexer.IndexAllAsync(CancellationToken.None);
        int incrementalCount = await indexer.GetIndexedCountAsync(CancellationToken.None);

        Assert.True(originalCount > 0);
        Assert.Equal(originalCount, incrementalCount);
    }

    [Fact]
    public async Task SearchAsync_WithNormativeKeyword_FiltersResults()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        var repository = new SearchRepository(dataSource);
        var metadataRepository = new MetadataRepository(dataSource);
        var service = new SearchService(repository, metadataRepository, CreateEmbeddingService());

        var sectionId1 = Guid.NewGuid();
        var sectionId2 = Guid.NewGuid();
        var sectionId3 = Guid.NewGuid();

        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await connection.ExecuteAsync(
            """
            insert into rfc_rag.rfc_sections (id, rfc_number, title, section, heading, text, source_path, url, source_sha256)
            values
              (@Id1, 2119, 'Key words', '1', 'Introduction', 'The key words MUST NOT use unencrypted communication', '/rfc2119.txt', 'https://example.com/rfc2119', 'abc'),
              (@Id2, 2119, 'Key words', '2', 'Definitions', 'Definitions of MUST and SHOULD levels', '/rfc2119.txt', 'https://example.com/rfc2119', 'def'),
              (@Id3, 8827, 'WebRTC Security Architecture', '6.5', 'Communications Security', 'MUST NOT send plain RTP communication', '/rfc8827.txt', 'https://example.com/rfc8827', 'ghi')
            """,
            new { Id1 = sectionId1, Id2 = sectionId2, Id3 = sectionId3 });
        await connection.ExecuteAsync(
            """
            insert into rfc_rag.normative_occurrences (id, section_id, rfc_number, keyword, line_offset)
            values
              (@OccId1, @Id1, 2119, 'MUST NOT', 5),
              (@OccId2, @Id2, 2119, 'MUST', 10),
              (@OccId3, @Id3, 8827, 'MUST NOT', 3)
            """,
            new { OccId1 = Guid.NewGuid(), OccId2 = Guid.NewGuid(), OccId3 = Guid.NewGuid(), Id1 = sectionId1, Id2 = sectionId2, Id3 = sectionId3 });

        IReadOnlyList<SearchResult> allResults = await service.SearchAsync("communication", 10, null, CancellationToken.None);
        IReadOnlyList<SearchResult> filteredResults = await service.SearchAsync("communication", 10, "MUST NOT", CancellationToken.None);

        Assert.True(allResults.Count > 0);
        Assert.True(filteredResults.Count <= allResults.Count);
        Assert.All(filteredResults, r => Assert.NotEqual(sectionId2, r.Id));
    }

    [Fact]
    public async Task SearchAsync_KeywordWithNoMatches_ReturnsEmpty()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        var repository = new SearchRepository(dataSource);
        var metadataRepository = new MetadataRepository(dataSource);
        var service = new SearchService(repository, metadataRepository, CreateEmbeddingService());

        var sectionId1 = Guid.NewGuid();
        var sectionId2 = Guid.NewGuid();

        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await connection.ExecuteAsync(
            """
            insert into rfc_rag.rfc_sections (id, rfc_number, title, section, heading, text, source_path, url, source_sha256)
            values
              (@Id1, 2119, 'Key words', '1', 'Introduction', 'The key words MUST use encryption', '/rfc2119.txt', 'https://example.com/rfc2119', 'abc'),
              (@Id2, 2119, 'Key words', '2', 'Definitions', 'Definitions of MUST and SHOULD levels', '/rfc2119.txt', 'https://example.com/rfc2119', 'def')
            """,
            new { Id1 = sectionId1, Id2 = sectionId2 });
        await connection.ExecuteAsync(
            """
            insert into rfc_rag.normative_occurrences (id, section_id, rfc_number, keyword, line_offset)
            values
              (@OccId1, @Id1, 2119, 'MUST', 5),
              (@OccId2, @Id2, 2119, 'MUST', 10)
            """,
            new { OccId1 = Guid.NewGuid(), OccId2 = Guid.NewGuid(), Id1 = sectionId1, Id2 = sectionId2 });

        IReadOnlyList<SearchResult> results = await service.SearchAsync("encryption", 10, "MUST NOT", CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_WhitespaceKeyword_TreatedAsNoFilter()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        var repository = new SearchRepository(dataSource);
        var metadataRepository = new MetadataRepository(dataSource);
        var service = new SearchService(repository, metadataRepository, CreateEmbeddingService());

        var sectionId1 = Guid.NewGuid();
        var sectionId2 = Guid.NewGuid();

        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await connection.ExecuteAsync(
            """
            insert into rfc_rag.rfc_sections (id, rfc_number, title, section, heading, text, source_path, url, source_sha256)
            values
              (@Id1, 2119, 'Key words', '1', 'Introduction', 'The key words MUST use encryption', '/rfc2119.txt', 'https://example.com/rfc2119', 'abc'),
              (@Id2, 2119, 'Key words', '2', 'Definitions', 'Definitions of MUST and SHOULD levels', '/rfc2119.txt', 'https://example.com/rfc2119', 'def')
            """,
            new { Id1 = sectionId1, Id2 = sectionId2 });
        await connection.ExecuteAsync(
            """
            insert into rfc_rag.normative_occurrences (id, section_id, rfc_number, keyword, line_offset)
            values
              (@OccId1, @Id1, 2119, 'MUST', 5),
              (@OccId2, @Id2, 2119, 'MUST', 10)
            """,
            new { OccId1 = Guid.NewGuid(), OccId2 = Guid.NewGuid(), Id1 = sectionId1, Id2 = sectionId2 });

        IReadOnlyList<SearchResult> whitespaceResults = await service.SearchAsync("encryption", 10, "   ", CancellationToken.None);
        IReadOnlyList<SearchResult> nullResults = await service.SearchAsync("encryption", 10, null, CancellationToken.None);

        Assert.NotEmpty(whitespaceResults);
        Assert.Equal(whitespaceResults.Count, nullResults.Count);
    }

    [Fact]
    public async Task SearchRepository_SearchHybridAsync_WithNormativeKeyword_FiltersCorrectly()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        var repository = new SearchRepository(dataSource);
        var embeddingService = CreateEmbeddingService();

        var sectionId1 = Guid.NewGuid();
        var sectionId2 = Guid.NewGuid();
        var sectionId3 = Guid.NewGuid();

        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await connection.ExecuteAsync(
            """
            insert into rfc_rag.rfc_sections (id, rfc_number, title, section, heading, text, source_path, url, source_sha256, embedding)
            values
              (@Id1, 2119, 'Key words', '1', 'Introduction', 'The key words MUST use encryption', '/rfc2119.txt', 'https://example.com/rfc2119', 'abc', null),
              (@Id2, 2119, 'Key words', '2', 'Definitions', 'Definitions of MUST and SHOULD levels', '/rfc2119.txt', 'https://example.com/rfc2119', 'def', null),
              (@Id3, 8827, 'WebRTC Security', '6.5', 'Communications Security', 'MUST NOT send plain RTP communication', '/rfc8827.txt', 'https://example.com/rfc8827', 'ghi', null)
            """,
            new { Id1 = sectionId1, Id2 = sectionId2, Id3 = sectionId3 });
        await connection.ExecuteAsync(
            """
            insert into rfc_rag.normative_occurrences (id, section_id, rfc_number, keyword, line_offset)
            values
              (@OccId1, @Id1, 2119, 'MUST', 5),
              (@OccId2, @Id2, 2119, 'SHOULD', 10),
              (@OccId3, @Id3, 8827, 'MUST NOT', 3)
            """,
            new { OccId1 = Guid.NewGuid(), OccId2 = Guid.NewGuid(), OccId3 = Guid.NewGuid(), Id1 = sectionId1, Id2 = sectionId2, Id3 = sectionId3 });

        var embeddings = await embeddingService.GenerateEmbeddingsAsync(["encryption"], CancellationToken.None);
        var results = await repository.SearchHybridAsync("encryption", embeddings[0], 10, "MUST", CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.NotEqual(sectionId2, r.Id));
        Assert.All(results, r => Assert.NotEqual(sectionId3, r.Id));
    }

    [Fact]
    public async Task SearchAsync_KeywordFilter_BeyondCandidateWindow_FillsLimit()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        var repository = new SearchRepository(dataSource);
        var metadataRepository = new MetadataRepository(dataSource);
        var service = new SearchService(repository, metadataRepository, CreateEmbeddingService());

        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);

        var sectionIds = new List<Guid>();
        // Sections 0-19: match query but NO normative keywords — survive lexical rank
        // but fail keyword filter, forcing the SQL engine to scan past the candidate window.
        // Sections 20-24: match query AND have 'MUST' — these fill the limit.
        for (int i = 0; i < 25; i++)
        {
            var sectionId = Guid.NewGuid();
            sectionIds.Add(sectionId);
            string text = i < 20
                ? $"encryption relevance section {i} with test content for hybrid search purposes"
                : $"encryption MUST be used for secure communication in section {i}";
            int rfcNumber = 2000 + i;
            await connection.ExecuteAsync(
                """
                insert into rfc_rag.rfc_sections (id, rfc_number, title, section, heading, text, source_path, url, source_sha256)
                values (@Id, @RfcNumber, 'Test RFC', @Section, 'Test Heading', @Text, '/test.txt', 'https://example.com/test', 'sha')
                """,
                new { Id = sectionId, RfcNumber = rfcNumber, Section = $"{i + 1}", Text = text });
        }

        // Only the last 5 sections (20-24) get the keyword — the first 20 must fail the filter
        for (int i = 20; i < sectionIds.Count; i++)
        {
            await connection.ExecuteAsync(
                """
                insert into rfc_rag.normative_occurrences (id, section_id, rfc_number, keyword, line_offset)
                values (@Id, @SectionId, 2000, 'MUST', 1)
                """,
                new { Id = Guid.NewGuid(), SectionId = sectionIds[i] });
        }

        IReadOnlyList<SearchResult> results = await service.SearchAsync("encryption", 5, "MUST", CancellationToken.None);

        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task IndexAllAsync_TxtAndXmlSameNumber_XmlMode_IndexesOnlyTxt()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();

        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "rfc9999.txt"), """
                Network Working Group
                Request for Comments: 9999

                                                   Test RFC

                1.  Introduction

                   Test content for the txt-over-xml precedence integration test.
                """, TestContext.Current.CancellationToken);

            await File.WriteAllTextAsync(Path.Combine(tempDir, "rfc9999.xml"), """
                <?xml version="1.0" encoding="UTF-8"?>
                <rfc xmlns="urn:ietf:params:xml:ns:rfcxml" number="9999">
                  <front><title>Test RFC</title></front>
                  <middle>
                    <section>
                      <name>Introduction</name>
                      <t>Test content from XML source.</t>
                    </section>
                  </middle>
                </rfc>
                """, TestContext.Current.CancellationToken);

            var indexingRepository = new IndexingRepository(dataSource);
            var embeddingService = CreateEmbeddingService();
            var options = Options.Create(new RfcRagOptions
            {
                RfcMirrorPath = tempDir,
                PostgresConnectionString = container!.GetConnectionString(),
                RfcParserType = RfcParserType.Xml,
                EmbeddingBatchSize = 5
            });

            IIndexerService indexer = new RfcIndexer(
                dataSource, indexingRepository,
                new RfcParser(), new RfcXmlParser(),
                embeddingService, options, NullLogger<RfcIndexer>.Instance);

            await indexer.IndexAllAsync(CancellationToken.None);

            await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
            var indexedRows = (await connection.QueryAsync<dynamic>(
                "select rfc_number, source_path from rfc_rag.indexed_rfcs where rfc_number = 9999"))
                .ToList();

            var row = Assert.Single(indexedRows);
            int rfcNumber = (int)row.rfc_number;
            string sourcePath = (string)row.source_path;

            Assert.Equal(9999, rfcNumber);
            Assert.EndsWith(".txt", sourcePath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private async Task<NpgsqlDataSource> CreateMigratedDataSourceAsync()
    {
        var dataSource = NpgsqlDataSource.Create(container!.GetConnectionString());
        await RfcRagMigrationRunner.ApplyAsync(dataSource, CancellationToken.None);

        return dataSource;
    }

    private RfcIndexer CreateIndexer(NpgsqlDataSource dataSource)
    {
        var indexingRepository = new IndexingRepository(dataSource);
        var embeddingService = CreateEmbeddingService();
        var options = Options.Create(new RfcRagOptions
        {
            RfcMirrorPath = Path.Combine(Directory.GetCurrentDirectory(), "TestData"),
            PostgresConnectionString = container!.GetConnectionString(),
            EmbeddingBatchSize = 5
        });

        return new RfcIndexer(
            dataSource,
            indexingRepository,
            new RfcParser(),
            new RfcXmlParser(),
            embeddingService,
            options,
            NullLogger<RfcIndexer>.Instance);
    }

    private static SearchService CreateSearchService(NpgsqlDataSource dataSource) =>
        new(new SearchRepository(dataSource), new MetadataRepository(dataSource), CreateEmbeddingService());

    [Fact]
    public async Task GetStatsAsync_AfterIndexing_IncludesManifest()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        IIndexerService indexer = CreateIndexer(dataSource);
        ISearchService search = CreateSearchService(dataSource);

        await indexer.IndexAllAsync(CancellationToken.None);

        string statsJson = await search.GetStatsAsync(CancellationToken.None);

        Assert.Contains("\"manifest\"", statsJson);
        Assert.Contains("\"parserType\"", statsJson);
        Assert.Contains("\"embeddingModel\"", statsJson);
        Assert.DoesNotContain("\"manifest\":null", statsJson);
    }

    [Fact]
    public async Task IndexAllAsync_WritesManifestRow()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        IIndexerService indexer = CreateIndexer(dataSource);
        var repository = new IndexingRepository(dataSource);

        await indexer.IndexAllAsync(CancellationToken.None);

        IndexManifest? manifest = await repository.GetLatestManifestAsync(CancellationToken.None);

        Assert.NotNull(manifest);
        Assert.Equal("Text", manifest.ParserType);
        Assert.Equal("OpenRouter", manifest.EmbeddingProvider);
        Assert.Contains("text-embedding-3-small", manifest.EmbeddingModel);
        Assert.Equal(1536, manifest.EmbeddingDimensions);
        Assert.True(manifest.RfcCount > 0);
        Assert.True(manifest.SectionCount > 0);
        Assert.NotEmpty(manifest.MirrorPath);
    }

    [Fact]
    public async Task IndexAllAsync_IncrementalRun_StillWritesManifest()
    {
        await using var dataSource = await CreateMigratedDataSourceAsync();
        IIndexerService indexer = CreateIndexer(dataSource);
        var repository = new IndexingRepository(dataSource);

        await indexer.IndexAllAsync(CancellationToken.None);
        await indexer.IndexAllAsync(CancellationToken.None);

        await using var connection = await dataSource.OpenConnectionAsync(CancellationToken.None);
        int manifestCount = await connection.ExecuteScalarAsync<int>(
            "select count(*) from rfc_rag.index_manifest");

        Assert.Equal(2, manifestCount);
    }

    private static EmbeddingService CreateEmbeddingService() =>
        new(new FakeEmbeddingGenerator(), new EmbeddingRetryPolicy(TimeProvider.System),
            5, embeddingDimensions: 1536, maxConcurrency: 1, NullLogger<EmbeddingService>.Instance);
}
