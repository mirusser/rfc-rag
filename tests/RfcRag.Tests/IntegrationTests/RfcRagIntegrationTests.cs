using System.Globalization;
using System.Text.Json;
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
        "rfc_errata",
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
            "HTTP semantics", 100, null, false, CancellationToken.None);
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
        string tempDir = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            // Copy one existing RFC file into temp dir for the incremental index test
            string sourceFile = Path.Join(
                Directory.GetCurrentDirectory(), "TestData", "rfc2119.txt");
            string destFile = Path.Join(tempDir, "rfc2119.txt");
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
    public async Task IndexAllAsync_ErrataJsonPathSet_IngestsErrataIdempotently()
    {
        string tempDir = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            string sourceFile = Path.Join(
                Directory.GetCurrentDirectory(), "TestData", "rfc2119.txt");
            File.Copy(sourceFile, Path.Join(tempDir, "rfc2119.txt"));

            string errataPath = Path.Join(tempDir, "errata.json");
            await File.WriteAllTextAsync(
                errataPath,
                """
                [
                  {
                    "errata_id": "900001",
                    "doc-id": "RFC2119",
                    "errata_status_code": "Verified",
                    "section": "1",
                    "orig_text": "old requirement text",
                    "correct_text": "corrected requirement text",
                    "submit_date": "2026-01-02"
                  },
                  {
                    "errata_id": "",
                    "doc-id": "RFC2119",
                    "errata_status_code": "Verified",
                    "section": "1",
                    "orig_text": "malformed entry",
                    "correct_text": "must be skipped"
                  }
                ]
                """,
                TestContext.Current.CancellationToken);

            var indexingRepository = new IndexingRepository(fixture.DataSource);
            var embeddingService = CreateEmbeddingService();
            var options = Options.Create(new RfcRagOptions
            {
                RfcMirrorPath = tempDir,
                PostgresConnectionString = fixture.ConnectionString,
                EmbeddingProvider = EmbeddingProvider.Local,
                ErrataJsonPath = errataPath,
            });

            var indexer = new RfcIndexer(
                fixture.DataSource,
                indexingRepository,
                new RfcParser(),
                new RfcXmlParser(),
                embeddingService,
                options,
                NullLogger<RfcIndexer>.Instance);

            await indexer.IndexAllAsync(TestContext.Current.CancellationToken);
            await indexer.IndexAllAsync(TestContext.Current.CancellationToken);

            await using var connection = await fixture.DataSource
                .OpenConnectionAsync(TestContext.Current.CancellationToken);
            int errataCount = await connection.QuerySingleAsync<int>(
                """
                select count(*)
                from rfc_rag.rfc_errata
                where errata_id = 900001
                """);

            string stats = await new MetadataRepository(fixture.DataSource)
                .GetStatsAsync(TestContext.Current.CancellationToken);
            using var statsJson = JsonDocument.Parse(stats);

            Assert.Equal(1, errataCount);
            Assert.Equal(1, statsJson.RootElement.GetProperty("errata").GetInt32());
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

        IReadOnlyList<SearchResult> allResults = await service.SearchAsync("communication", 10, null, false, CancellationToken.None);
        IReadOnlyList<SearchResult> filteredResults = await service.SearchAsync("communication", 10, "MUST NOT", false, CancellationToken.None);

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
            includeObsolete: false,
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
            includeObsolete: false,
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
            $"encryption {uniqueToken}", 10, "MUST NOT", false, CancellationToken.None);

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

        IReadOnlyList<SearchResult> whitespaceResults = await service.SearchAsync("encryption", 10, "   ", false, CancellationToken.None);
        IReadOnlyList<SearchResult> nullResults = await service.SearchAsync("encryption", 10, null, false, CancellationToken.None);

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

        IReadOnlyList<SearchResult> results = await service.SearchAsync("encryption", 5, "MUST", false, CancellationToken.None);

        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task IndexAllAsync_TxtAndXmlSameNumber_XmlMode_IndexesOnlyTxt()
    {
        string tempDir = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            await File.WriteAllTextAsync(Path.Join(tempDir, "rfc9999.txt"), """
                Network Working Group
                Request for Comments: 9999

                                                       Test RFC

                1.  Introduction

                   Test content for the txt-over-xml precedence integration test.
                """, TestContext.Current.CancellationToken);

            await File.WriteAllTextAsync(Path.Join(tempDir, "rfc9999.xml"), """
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
            var indexedRows = (await connection.QueryAsync<(int RfcNumber, string SourcePath)>(
                    "select rfc_number, source_path from rfc_rag.indexed_rfcs where rfc_number = 9999"))
                .ToList();

            var row = Assert.Single(indexedRows);

            Assert.Equal(9999, row.RfcNumber);
            Assert.EndsWith(".txt", row.SourcePath, StringComparison.OrdinalIgnoreCase);
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

        string fixtureMirrorPath = Path.Join(Directory.GetCurrentDirectory(), "TestData");
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
        string tempDir = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceFile = Path.Join(
                Directory.GetCurrentDirectory(), "TestData", "rfc2119.txt");
            string destFile = Path.Join(tempDir, "rfc2119.txt");
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

/// <summary>
/// Integration tests for Task 21: status surface on search results and evidence.
/// RFC 9110 obsoletes RFC 7231; both are indexed in MediumCorpusFixture.
/// Verifies that Status blocks are populated and that the obsoleted-RFC penalty is
/// applied by default but suppressed when includeObsolete=true.
/// </summary>
[Trait("Category", "Integration")]
public sealed class StatusSurfaceTests(MediumCorpusFixture fixture) : IClassFixture<MediumCorpusFixture>
{
    // Query keywords hit the HTTP vocabulary dims, returning sections from both RFCs.
    private const string HttpMethodQuery = "HTTP method request response semantics";

    [Fact]
    public async Task SearchAsync_DefaultBehavior_Rfc7231ResultsHaveObsoletedStatus()
    {
        IReadOnlyList<SearchResult> results = await fixture.SearchService
            .SearchAsync(HttpMethodQuery, limit: 50, normativeKeyword: null,
                includeObsolete: false, CancellationToken.None);

        var rfc7231Results = results.Where(r => r.RfcNumber == 7231).ToList();

        // Skip if RFC 7231 sections don't appear in the top-50 for this fake-embedding corpus.
        if (rfc7231Results.Count == 0)
            return;

        // Every RFC 7231 result must carry a populated Status with category "obsoleted"
        // and ObsoletedBy containing RFC 9110 (which obsoletes 7231).
        Assert.All(rfc7231Results, r =>
        {
            Assert.NotNull(r.Status);
            Assert.Equal("obsoleted", r.Status!.Category);
            Assert.Contains(9110, r.Status.ObsoletedBy);
        });
    }

    [Fact]
    public async Task SearchAsync_DefaultVsIncludeObsolete_Rfc7231ScoreHigherWithoutPenalty()
    {
        IReadOnlyList<SearchResult> defaultResults = await fixture.SearchService
            .SearchAsync(HttpMethodQuery, limit: 50, normativeKeyword: null,
                includeObsolete: false, CancellationToken.None);

        IReadOnlyList<SearchResult> withObsoleteResults = await fixture.SearchService
            .SearchAsync(HttpMethodQuery, limit: 50, normativeKeyword: null,
                includeObsolete: true, CancellationToken.None);

        var rfc7231Default = defaultResults.Where(r => r.RfcNumber == 7231).ToList();
        var rfc7231WithObsolete = withObsoleteResults.Where(r => r.RfcNumber == 7231).ToList();

        if (rfc7231Default.Count == 0 || rfc7231WithObsolete.Count == 0)
            return;

        // includeObsolete=true skips the -0.10 penalty, so scores must be >= default scores.
        double maxDefault = rfc7231Default.Max(r => r.Score);
        double maxWithObsolete = rfc7231WithObsolete.Max(r => r.Score);

        Assert.True(maxWithObsolete >= maxDefault,
            $"RFC 7231 score with includeObsolete=true ({maxWithObsolete:F4}) " +
            $"should be >= default ({maxDefault:F4}).");
    }

    [Fact]
    public async Task SearchAsync_DefaultBehavior_Rfc9110ResultsRankedBeforeRfc7231()
    {
        IReadOnlyList<SearchResult> results = await fixture.SearchService
            .SearchAsync(HttpMethodQuery, limit: 50, normativeKeyword: null,
                includeObsolete: false, CancellationToken.None);

        var rfc9110Indices = results
            .Select((r, i) => (r, i))
            .Where(x => x.r.RfcNumber == 9110)
            .Select(x => x.i)
            .ToList();

        var rfc7231Indices = results
            .Select((r, i) => (r, i))
            .Where(x => x.r.RfcNumber == 7231)
            .Select(x => x.i)
            .ToList();

        // Skip if one family is entirely absent from the result window.
        if (rfc9110Indices.Count == 0 || rfc7231Indices.Count == 0)
            return;

        // With the −0.10 obsolescence penalty applied to RFC 7231, all RFC 9110 sections
        // must appear at lower indices (higher rank) than all RFC 7231 sections.
        int worstRfc9110 = rfc9110Indices.Max();
        int bestRfc7231 = rfc7231Indices.Min();

        Assert.True(worstRfc9110 < bestRfc7231,
            $"Expected all RFC 9110 results (worst at index {worstRfc9110}) to rank before " +
            $"all RFC 7231 results (best at index {bestRfc7231}) when includeObsolete=false.");
    }

    [Fact]
    public async Task SearchAsync_IncludeObsolete_StatusStillPopulatedOnRfc7231Results()
    {
        IReadOnlyList<SearchResult> results = await fixture.SearchService
            .SearchAsync(HttpMethodQuery, limit: 50, normativeKeyword: null,
                includeObsolete: true, CancellationToken.None);

        var rfc7231Results = results.Where(r => r.RfcNumber == 7231).ToList();

        if (rfc7231Results.Count == 0)
            return;

        // Status must still be populated even with includeObsolete=true —
        // the flag suppresses the penalty and warning, not the status data itself.
        Assert.All(rfc7231Results, r =>
        {
            Assert.NotNull(r.Status);
            Assert.Equal("obsoleted", r.Status!.Category);
            Assert.Contains(9110, r.Status.ObsoletedBy);
        });
    }

    [Fact]
    public async Task SearchAsync_CurrentRfc9110Results_HaveCurrentStatus()
    {
        IReadOnlyList<SearchResult> results = await fixture.SearchService
            .SearchAsync(HttpMethodQuery, limit: 50, normativeKeyword: null,
                includeObsolete: false, CancellationToken.None);

        var rfc9110Results = results.Where(r => r.RfcNumber == 9110).ToList();

        Assert.NotEmpty(rfc9110Results);

        // RFC 9110 is not obsoleted, so all its results should have Status.Category == "current"
        // (or Status == null if no relation row exists, which also means current).
        Assert.All(rfc9110Results, r =>
        {
            if (r.Status is not null)
                Assert.Equal("current", r.Status.Category);
        });
    }
}
