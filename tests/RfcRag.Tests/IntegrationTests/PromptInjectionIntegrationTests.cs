using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using RfcRag.Answering;
using RfcRag.Indexing;
using RfcRag.Infrastructure;
using RfcRag.Parsing;
using RfcRag.Search;
using RfcRag.Settings;
using RfcRag.Tests.Fakes;
using Testcontainers.PostgreSql;

namespace RfcRag.Tests.IntegrationTests;

/// <summary>
/// Minimal fixture: indexes only rfc9998-injection.txt as rfc9998.txt so that
/// pipeline tests can assert on a corpus containing known hostile injection patterns.
/// </summary>
public sealed class PromptInjectionFixture : IAsyncLifetime
{
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

        tempRfcDir = Path.Join(Path.GetTempPath(), $"rfc-rag-injection-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRfcDir);

        // RfcSourceResolver matches rfc<number>.txt; copy the fixture under its canonical name.
        string injectionSource = Path.Join("TestData", "rfc9998-injection.txt");
        File.Copy(injectionSource, Path.Join(tempRfcDir, "rfc9998.txt"));

        var embeddingService = new EmbeddingService(
            new SemanticFakeEmbeddingGenerator(),
            new EmbeddingRetryPolicy(TimeProvider.System),
            batchSize: 5,
            embeddingDimensions: 1536,
            maxConcurrency: 1,
            NullLogger<EmbeddingService>.Instance);

        var indexOptions = Options.Create(new RfcRagOptions
        {
            RfcMirrorPath = tempRfcDir,
            PostgresConnectionString = container.GetConnectionString(),
            EmbeddingBatchSize = 5,
            MaxIndexingParallelism = 1,
        });

        var indexer = new RfcIndexer(
            dataSource,
            new IndexingRepository(dataSource),
            new RfcParser(),
            new RfcXmlParser(),
            embeddingService,
            indexOptions,
            NullLogger<RfcIndexer>.Instance);

        await indexer.IndexAllAsync(CancellationToken.None);

        var repository = new SearchRepository(dataSource);
        var metadataRepository = new MetadataRepository(dataSource);
        var searchOptions = Options.Create(new RfcRagOptions
        {
            RfcMirrorPath = tempRfcDir,
            PostgresConnectionString = container.GetConnectionString(),
        });
        SearchService = new SearchService(repository, metadataRepository, embeddingService, searchOptions);
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
}

[Trait("Category", "Integration")]
public sealed class PromptInjectionIntegrationTests(PromptInjectionFixture fixture) : IClassFixture<PromptInjectionFixture>
{
    private static readonly IOptions<RfcRagOptions> DefaultOptions = Options.Create(new RfcRagOptions
    {
        RfcMirrorPath = "",
        PostgresConnectionString = "",
        MaxIndexingParallelism = 1,
    });

    private AskService BuildAskService(IChatClient chatClient) =>
        new(
            fixture.SearchService,
            new ContextAssembler(fixture.SearchService),
            new AnswerGenerator(chatClient, DefaultOptions),
            DefaultOptions,
            new FakeTraceWriter());

    [Fact]
    public async Task Pipeline_InjectionFixture_HostileTextConfinedToEvidenceBlock()
    {
        // Scripted response: valid-JSON so the generator doesn't immediately short-circuit.
        const string scripted = """{"answer":"RFC 9998 is a test RFC.","citations":[]}""";
        var fakeClient = new FakeChatClient(scripted);
        var askService = BuildAskService(fakeClient);

        await askService.AskAsync(
            "What does RFC 9998 say in the instruction override text?",
            limit: 5,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(fakeClient.CapturedCalls.Count > 0,
            "FakeChatClient was never called — evidence pack was empty or the generator short-circuited unexpectedly.");

        FakeChatClient.ChatRequest firstCall = fakeClient.CapturedCalls[0];

        FakeChatClient.CapturedMessage? systemMessage = firstCall.Messages
            .FirstOrDefault(m => string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase));
        FakeChatClient.CapturedMessage? userMessage = firstCall.Messages
            .FirstOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(systemMessage);
        Assert.NotNull(userMessage);

        // Hostile text must NOT appear in the system message (static instructions only, no RFC content).
        Assert.DoesNotContain("IGNORE ALL PREVIOUS INSTRUCTIONS", systemMessage.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("[SYSTEM:", systemMessage.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("=== SYSTEM OVERRIDE ===", systemMessage.Text, StringComparison.Ordinal);

        // Hostile text MUST appear in the user message, inside the <evidence> data block.
        Assert.Contains("<evidence>", userMessage.Text, StringComparison.Ordinal);
        Assert.Contains("IGNORE ALL PREVIOUS INSTRUCTIONS", userMessage.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("What is the purpose of the RFC 9999 fictional test RFC?")]
    [InlineData("What does quantum cryptography say about TLS key exchange?")]
    [InlineData("What does the Mars colony RFC say about networking?")]
    public async Task Pipeline_NoAnswerQuestion_ReturnsNoAnswerWithEmptyCitations(string question)
    {
        // Empty queue — FakeChatClient returns "{}" on every call, which forces the
        // generator fallback path to NoAnswer=true after the JSON repair re-attempt.
        var fakeClient = new FakeChatClient();
        var askService = BuildAskService(fakeClient);

        GeneratedAnswer answer = await askService.AskAsync(
            question,
            limit: 10,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(answer.NoAnswer, $"Expected NoAnswer=true for question: '{question}'");
        Assert.Empty(answer.Citations);
    }
}
