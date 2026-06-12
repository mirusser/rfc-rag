using System.Globalization;
using Dapper;
using RfcRag.Indexing;
using RfcRag.Models;
using RfcRag.Parsing;
using RfcRag.Search;
using RfcRag.Settings;
using RfcRag.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace RfcRag.Tests.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class RfcRagIntegrationTests : IClassFixture<MediumCorpusFixture>
{
    private static readonly string[] ExpectedTables =
    [
        "index_manifest",
        "indexed_rfcs",
        "normative_occurrences",
        "rfc_abnf_blocks",
        "rfc_sections",
        "schema_migrations"
    ];

    private readonly MediumCorpusFixture fixture;

    public RfcRagIntegrationTests(MediumCorpusFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task RunMigrations_OnEmptyDatabase_CreatesExpectedTables()
    {
        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken.None);
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
        IReadOnlyList<SearchResult> results = await fixture.SearchService.SearchAsync(
            "HTTP semantics", 100, null, CancellationToken.None);
        RfcSection? rfc9110Section = await fixture.SearchService.GetSectionAsync(
            9110, "1", CancellationToken.None);

        Assert.True(fixture.IndexedCount >= 190);
        Assert.True(results.Count > 0);
        Assert.NotNull(rfc9110Section);
        Assert.Equal(9110, rfc9110Section.RfcNumber);
        Assert.Equal("1", rfc9110Section.Section);
    }

    [Fact]
    public async Task GetSectionAsync_WithExistingSection_ReturnsExactMatch()
    {
        RfcSection? section = await fixture.SearchService.GetSectionAsync(
            2119, "1", CancellationToken.None);

        Assert.NotNull(section);
        Assert.Contains("MUST", section.Text);
        Assert.Equal("1", section.Section);
        Assert.Equal(2119, section.RfcNumber);
    }

    [Fact]
    public async Task SearchNormativeAsync_WithKeyword_ReturnsMatchingSections()
    {
        IReadOnlyList<SearchResult> results = await fixture.SearchService.SearchNormativeAsync(
            "MUST", null, 20, CancellationToken.None);

        Assert.Contains(results, result => result.RfcNumber == 2119);
        Assert.Contains(results, result => result.Excerpt.Contains("MUST"));
    }

    [Fact]
    public async Task GetIndexedRfcMetadataAsync_AfterIndexing_IncludesGrammarStyle()
    {
        var metadataRepository = new MetadataRepository(fixture.DataSource);

        RfcMetadata? tlsMetadata = await metadataRepository.GetIndexedRfcMetadataAsync(
            8446, CancellationToken.None);
        RfcMetadata? rfc2119Metadata = await metadataRepository.GetIndexedRfcMetadataAsync(
            2119, CancellationToken.None);

        Assert.NotNull(tlsMetadata);
        Assert.Equal(GrammarStyleConstants.TlsPresentationLang, tlsMetadata.GrammarStyle);
        Assert.NotNull(rfc2119Metadata);
        Assert.Equal(GrammarStyleConstants.None, rfc2119Metadata.GrammarStyle);
    }

    [Fact]
    public async Task SearchAbnf_FindsGrammarBlocks_AcrossRecentRfcs()
    {
        IReadOnlyList<SearchResult> uriResults = await fixture.SearchService.SearchAbnfAsync(
            "URI", null, 100, CancellationToken.None);
        Assert.Contains(uriResults, r => r.RfcNumber == 3986);

        IReadOnlyList<SearchResult> httpResults = await fixture.SearchService.SearchAbnfAsync(
            "expectation", null, 100, CancellationToken.None);
        Assert.Contains(httpResults, r => r.RfcNumber == 9110);

        IReadOnlyList<SearchResult> httpTokenResults = await fixture.SearchService.SearchAbnfAsync(
            "token", null, 100, CancellationToken.None);
        Assert.Contains(httpTokenResults, r => r.RfcNumber == 9110);

        IReadOnlyList<SearchResult> filteredResults = await fixture.SearchService.SearchAbnfAsync(
            "URI", [3986], 100, CancellationToken.None);
        Assert.Contains(filteredResults, r => r.RfcNumber == 3986);
    }

    [Fact]
    public async Task IncrementalIndex_SkipsUnchangedRfcs()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            // Copy one existing RFC file into temp dir for the incremental index test
            string sourceFile = Path.Combine(
                Directory.GetCurrentDirectory(), "TestData", "rfc2119.txt");
            string destFile = Path.Combine(tempDir, "rfc2119.txt");
            File.Copy(sourceFile, destFile);

            // Create an indexer that only sees this temp dir
            var indexingRepository = new IndexingRepository(fixture.DataSource);
            var embeddingService = CreateEmbeddingService();
            var options = Options.Create(new RfcRagOptions
            {
                RfcMirrorPath = tempDir,
                PostgresConnectionString = fixture.ConnectionString,
                EmbeddingBatchSize = 5
            });

            IIndexerService indexer = new RfcIndexer(
                fixture.DataSource, indexingRepository,
                new RfcParser(), new RfcXmlParser(),
                embeddingService, options, NullLogger<RfcIndexer>.Instance);

            await indexer.IndexAllAsync(CancellationToken.None);
            int originalCount = await indexer.GetIndexedCountAsync(CancellationToken.None);

            await indexer.IndexAllAsync(CancellationToken.None);
            int incrementalCount = await indexer.GetIndexedCountAsync(CancellationToken.None);

            Assert.True(originalCount > 0);
            Assert.Equal(originalCount, incrementalCount);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_WithNormativeKeyword_FiltersResults()
    {
        var repository = new SearchRepository(fixture.DataSource);
        var metadataRepository = new MetadataRepository(fixture.DataSource);
        var service = new SearchService(repository, metadataRepository, CreateEmbeddingService(), CreateSearchOptions());

        var sectionId1 = Guid.NewGuid();
        var sectionId2 = Guid.NewGuid();
        var sectionId3 = Guid.NewGuid();

        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken.None);
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
    public async Task SearchAsync_ExplicitNormativeKeyword_OverridesPlannerSuggestion()
    {
        var repository = new SearchRepository(fixture.DataSource);
        var metadataRepository = new MetadataRepository(fixture.DataSource);
        var service = new SearchService(repository, metadataRepository, CreateEmbeddingService(), CreateSearchOptions());

        var mustSectionId = Guid.NewGuid();
        var mustNotSectionId = Guid.NewGuid();

        string mustEmbedding = await GenerateVectorLiteralAsync("Forbidden transport clients MUST use encryption");
        string mustNotEmbedding = await GenerateVectorLiteralAsync("Forbidden transport clients MUST NOT use plaintext");

        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken.None);
        await connection.ExecuteAsync(
            $"""
            insert into rfc_rag.rfc_sections (id, rfc_number, title, section, heading, text, source_path, url, source_sha256, embedding)
            values
              (@MustSectionId, 2119, 'Key words', '1', 'Requirements', 'Forbidden transport clients MUST use encryption', '/rfc2119.txt', 'https://example.com/rfc2119', 'abc', {mustEmbedding}),
              (@MustNotSectionId, 2119, 'Key words', '2', 'Prohibitions', 'Forbidden transport clients MUST NOT use plaintext', '/rfc2119.txt', 'https://example.com/rfc2119', 'def', {mustNotEmbedding})
            """,
            new { MustSectionId = mustSectionId, MustNotSectionId = mustNotSectionId });
        await connection.ExecuteAsync(
            """
            insert into rfc_rag.normative_occurrences (id, section_id, rfc_number, keyword, line_offset)
            values
              (@MustOccurrenceId, @MustSectionId, 2119, 'MUST', 0),
              (@MustNotOccurrenceId, @MustNotSectionId, 2119, 'MUST NOT', 0)
            """,
            new
            {
                MustOccurrenceId = Guid.NewGuid(),
                MustNotOccurrenceId = Guid.NewGuid(),
                MustSectionId = mustSectionId,
                MustNotSectionId = mustNotSectionId,
            });

        IReadOnlyList<SearchResult> results = await service.SearchAsync(
            "forbidden encryption",
            limit: 10,
            normativeKeyword: "MUST",
            CancellationToken.None);

        Assert.NotEmpty(results);
        // Keyword "MUST" must include section 1 (has MUST) and exclude section 2 (has MUST NOT).
        // With left-join fusion, corpus sections matching FTS + keyword may also appear.
        Assert.Contains(results, r => r.Section == "1");
        Assert.DoesNotContain(results, r => r.Section == "2");
    }

    [Fact]
    public async Task SearchAsync_PlannerSuggestedNormativeKeyword_FiltersWhenKeywordMissing()
    {
        var repository = new SearchRepository(fixture.DataSource);
        var metadataRepository = new MetadataRepository(fixture.DataSource);
        var service = new SearchService(repository, metadataRepository, CreateEmbeddingService(), CreateSearchOptions());

        var mustSectionId = Guid.NewGuid();
        var mustNotSectionId = Guid.NewGuid();

        string mustEmbedding = await GenerateVectorLiteralAsync("Forbidden transport clients MUST use encryption");
        string mustNotEmbedding = await GenerateVectorLiteralAsync("Forbidden transport clients MUST NOT use plaintext");

        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken.None);
        await connection.ExecuteAsync(
            $"""
            insert into rfc_rag.rfc_sections (id, rfc_number, title, section, heading, text, source_path, url, source_sha256, embedding)
            values
              (@MustSectionId, 2119, 'Key words', '1', 'Requirements', 'Forbidden transport clients MUST use encryption', '/rfc2119.txt', 'https://example.com/rfc2119', 'abc', {mustEmbedding}),
              (@MustNotSectionId, 2119, 'Key words', '2', 'Prohibitions', 'Forbidden transport clients MUST NOT use plaintext', '/rfc2119.txt', 'https://example.com/rfc2119', 'def', {mustNotEmbedding})
            """,
            new { MustSectionId = mustSectionId, MustNotSectionId = mustNotSectionId });
        await connection.ExecuteAsync(
            """
            insert into rfc_rag.normative_occurrences (id, section_id, rfc_number, keyword, line_offset)
            values
              (@MustOccurrenceId, @MustSectionId, 2119, 'MUST', 0),
              (@MustNotOccurrenceId, @MustNotSectionId, 2119, 'MUST NOT', 0)
            """,
            new
            {
                MustOccurrenceId = Guid.NewGuid(),
                MustNotOccurrenceId = Guid.NewGuid(),
                MustSectionId = mustSectionId,
                MustNotSectionId = mustNotSectionId,
            });

        IReadOnlyList<SearchResult> results = await service.SearchAsync(
            "forbidden transport",
            limit: 10,
            normativeKeyword: null,
            CancellationToken.None);

        Assert.NotEmpty(results);
        // Planner suggests "MUST NOT" from the query text. Section 2 (MUST NOT) must be included;
        // section 1 (MUST) must be excluded.
        Assert.Contains(results, r => r.Section == "2");
        Assert.DoesNotContain(results, r => r.Section == "1");
    }

    [Fact]
    public async Task SearchAsync_KeywordWithNoMatches_ReturnsEmpty()
    {
        var repository = new SearchRepository(fixture.DataSource);
        var metadataRepository = new MetadataRepository(fixture.DataSource);
        var service = new SearchService(repository, metadataRepository, CreateEmbeddingService(), CreateSearchOptions());

        var sectionId1 = Guid.NewGuid();
        var sectionId2 = Guid.NewGuid();

        string uniqueToken = "UNIQUEMARKER_ENCRYPTION_";

        string embedding1 = await GenerateVectorLiteralAsync($"The key words MUST use encryption {uniqueToken}");
        string embedding2 = await GenerateVectorLiteralAsync($"Definitions of MUST and SHOULD levels {uniqueToken}");

        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken.None);
        await connection.ExecuteAsync(
            $"""
            insert into rfc_rag.rfc_sections (id, rfc_number, title, section, heading, text, source_path, url, source_sha256, embedding)
            values
              (@Id1, 2119, 'Key words', '1', 'Introduction', 'The key words MUST use encryption {uniqueToken}', '/rfc2119.txt', 'https://example.com/rfc2119', 'abc', {embedding1}),
              (@Id2, 2119, 'Key words', '2', 'Definitions', 'Definitions of MUST and SHOULD levels {uniqueToken}', '/rfc2119.txt', 'https://example.com/rfc2119', 'def', {embedding2})
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

        IReadOnlyList<SearchResult> results = await service.SearchAsync(
            $"encryption {uniqueToken}", 10, "MUST NOT", CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_WhitespaceKeyword_TreatedAsNoFilter()
    {
        var repository = new SearchRepository(fixture.DataSource);
        var metadataRepository = new MetadataRepository(fixture.DataSource);
        var service = new SearchService(repository, metadataRepository, CreateEmbeddingService(), CreateSearchOptions());

        var sectionId1 = Guid.NewGuid();
        var sectionId2 = Guid.NewGuid();

        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken.None);
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
        var repository = new SearchRepository(fixture.DataSource);
        var embeddingService = CreateEmbeddingService();

        var sectionId1 = Guid.NewGuid();
        var sectionId2 = Guid.NewGuid();
        var sectionId3 = Guid.NewGuid();

        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken.None);
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
        var repository = new SearchRepository(fixture.DataSource);
        var metadataRepository = new MetadataRepository(fixture.DataSource);
        var service = new SearchService(repository, metadataRepository, CreateEmbeddingService(), CreateSearchOptions());

        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken.None);

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

            var indexingRepository = new IndexingRepository(fixture.DataSource);
            var embeddingService = CreateEmbeddingService();
            var options = Options.Create(new RfcRagOptions
            {
                RfcMirrorPath = tempDir,
                PostgresConnectionString = fixture.ConnectionString,
                RfcParserType = RfcParserType.Xml,
                EmbeddingBatchSize = 5
            });

            IIndexerService indexer = new RfcIndexer(
                fixture.DataSource, indexingRepository,
                new RfcParser(), new RfcXmlParser(),
                embeddingService, options, NullLogger<RfcIndexer>.Instance);

            await indexer.IndexAllAsync(CancellationToken.None);

            await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken.None);
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

    [Fact]
    public async Task GetStatsAsync_AfterIndexing_IncludesManifest()
    {
        string statsJson = await fixture.SearchService.GetStatsAsync(CancellationToken.None);

        Assert.Contains("\"manifest\"", statsJson);
        Assert.Contains("\"parserType\"", statsJson);
        Assert.Contains("\"embeddingModel\"", statsJson);
        Assert.DoesNotContain("\"manifest\":null", statsJson);
    }

    [Fact]
    public async Task IndexAllAsync_WritesManifestRow()
    {
        var repository = new IndexingRepository(fixture.DataSource);

        string fixtureMirrorPath = Path.Combine(Directory.GetCurrentDirectory(), "TestData");
        IndexManifest? manifest = await repository.GetLatestManifestAsync(CancellationToken.None, fixtureMirrorPath);

        Assert.NotNull(manifest);
        Assert.Equal("Text", manifest.ParserType);
        Assert.Equal("Local", manifest.EmbeddingProvider);
        Assert.Equal(1536, manifest.EmbeddingDimensions);
        Assert.True(manifest.RfcCount > 0);
        Assert.True(manifest.SectionCount > 0);
        Assert.NotEmpty(manifest.MirrorPath);
    }

    [Fact]
    public async Task IndexAllAsync_IncrementalRun_StillWritesManifest()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceFile = Path.Combine(
                Directory.GetCurrentDirectory(), "TestData", "rfc2119.txt");
            string destFile = Path.Combine(tempDir, "rfc2119.txt");
            File.Copy(sourceFile, destFile);

            var indexingRepository = new IndexingRepository(fixture.DataSource);
            var embeddingService = CreateEmbeddingService();
            var options = Options.Create(new RfcRagOptions
            {
                RfcMirrorPath = tempDir,
                PostgresConnectionString = fixture.ConnectionString,
                EmbeddingBatchSize = 5
            });

            IIndexerService indexer = new RfcIndexer(
                fixture.DataSource, indexingRepository,
                new RfcParser(), new RfcXmlParser(),
                embeddingService, options, NullLogger<RfcIndexer>.Instance);

            await indexer.IndexAllAsync(CancellationToken.None);
            await indexer.IndexAllAsync(CancellationToken.None);

            await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken.None);
            int manifestCount = await connection.ExecuteScalarAsync<int>(
                "select count(*) from rfc_rag.index_manifest where mirror_path = @MirrorPath",
                new { MirrorPath = tempDir });

            Assert.Equal(2, manifestCount);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static EmbeddingService CreateEmbeddingService() =>
        new(new FakeEmbeddingGenerator(), new EmbeddingRetryPolicy(TimeProvider.System),
            5, embeddingDimensions: 1536, maxConcurrency: 1, NullLogger<EmbeddingService>.Instance);

    private static IOptions<RfcRagOptions> CreateSearchOptions() =>
        Options.Create(new RfcRagOptions
        {
            RfcMirrorPath = string.Empty,
            PostgresConnectionString = string.Empty,
        });

    private static async Task<string> GenerateVectorLiteralAsync(string text)
    {
        var generator = new FakeEmbeddingGenerator();
        var embeddings = await generator.GenerateAsync([text], cancellationToken: CancellationToken.None);
        float[] vector = embeddings[0].Vector.ToArray();
        return $"'[{string.Join(",", vector.Select(v => v.ToString(CultureInfo.InvariantCulture)))}]'::vector";
    }
}
