using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RfcRag.Answering;
using RfcRag.Cli;
using RfcRag.Evaluation;
using RfcRag.Indexing;
using RfcRag.Infrastructure;
using RfcRag.Models;
using RfcRag.Search;
using RfcRag.Tests.Fakes;
using Testcontainers.PostgreSql;

namespace RfcRag.Tests.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class EvalCommandTests : IAsyncLifetime
{
    private const string PostgresImage = "pgvector/pgvector:pg17";

    private PostgreSqlContainer? container;
    private NpgsqlDataSource? dataSource;

    public async ValueTask InitializeAsync()
    {
        container = new PostgreSqlBuilder(PostgresImage).Build();

        await container.StartAsync(TestContext.Current.CancellationToken);

        dataSource = NpgsqlDataSource.Create(container.GetConnectionString());
        await RfcRagMigrationRunner.ApplyAsync(dataSource, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (dataSource is not null)
        {
            await dataSource.DisposeAsync();
        }

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    [Fact]
    public async Task RunAnswerEvalAsync_WithObsoleteCitation_EmitsCombinedReportAndUsesIndexedObsoleteRfcs()
    {
        var repository = new IndexingRepository(dataSource!);
        await repository.InsertManifestAsync(
            mirrorPath: "/tmp/rfc-mirror",
            parserType: "text",
            parserVersion: "test",
            embeddingProvider: "fake",
            embeddingModel: "fake-embedding",
            embeddingDimensions: 3,
            embeddingBatchSize: 1,
            rfcCount: 2,
            sectionCount: 1,
            TestContext.Current.CancellationToken);

        var searchService = new FakeSearchService
        {
            SearchResults =
            [
                new SearchResult(
                    Guid.NewGuid(),
                    7231,
                    "HTTP/1.1 Semantics and Content",
                    "4.3.1",
                    "GET",
                    "The GET method means retrieve whatever information is identified by the Request-URI.",
                    "/tmp/rfc7231.txt",
                    "https://www.rfc-editor.org/rfc/rfc7231",
                    0.99),
            ],
            IndexedRfcList =
            [
                new RfcMetadata
                {
                    Number = 9110,
                    Title = "HTTP Semantics",
                    Obsoletes = [7231],
                },
            ],
        };

        var askService = new RecordingAskService(
            new GeneratedAnswer
            {
                Answer = "RFC 7231 defines GET semantics. [7231#4.3.1]",
                Citations =
                [
                    new Citation
                    {
                        EvidenceId = "7231#4.3.1",
                        RfcNumber = 7231,
                        Section = "4.3.1",
                        RelevantText = "The GET method means retrieve whatever information is identified by the Request-URI.",
                    },
                ],
            });

        string questionsFilePath = Path.Combine(Path.GetTempPath(), $"rfc-rag-eval-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            questionsFilePath,
            """
            [
              {
                "id": "q1",
                "question": "What does RFC 7231 say GET does?",
                "expectedRfcs": [7231],
                "expectedSections": ["4.3.1"],
                "mustCite": [7231],
                "shouldNotCite": [],
                "answerType": "factual",
                "corpus": "testdata"
              }
            ]
            """,
            TestContext.Current.CancellationToken);

        using var output = new StringWriter();
        var command = new EvalCommand(
            searchService,
            repository,
            TimeProvider.System,
            NullLogger<EvalCommand>.Instance,
            askService);

        int exitCode = await command
            .RunAnswerEvalAsync(
                questionsFilePath,
                topK: 3,
                corpus: "testdata",
                output: output,
                cancellationToken: TestContext.Current.CancellationToken);

        using JsonDocument report = JsonDocument.Parse(output.ToString());

        Assert.Equal(0, exitCode);
        Assert.Equal(3, askService.LastLimit);
        Assert.True(report.RootElement.TryGetProperty("manifestId", out JsonElement manifestId));
        Assert.False(string.IsNullOrWhiteSpace(manifestId.GetString()));
        Assert.True(report.RootElement.TryGetProperty("aggregate", out _));
        JsonElement answerEval = report.RootElement.GetProperty("answerEval");
        Assert.Equal(1.0, answerEval.GetProperty("aggregate").GetProperty("avgObsoleteCitationRate").GetDouble());

        var routerAskService = new RecordingAskService(askService.Answer);
        var router = new CliCommandRouter(
            searchService,
            repository,
            NullLoggerFactory.Instance,
            routerAskService);

        bool handled = await router.TryHandleAsync(
            ["--eval", questionsFilePath, "--answers", "--limit", "3"],
            TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(3, routerAskService.LastLimit);
    }

    private sealed class RecordingAskService : IAskService
    {
        public RecordingAskService(GeneratedAnswer answer)
        {
            Answer = answer;
        }

        public GeneratedAnswer Answer { get; }

        public int? LastLimit { get; private set; }

        public Task<GeneratedAnswer> AskAsync(
            string question,
            int? limit = null,
            string? normativeKeyword = null,
            CancellationToken cancellationToken = default)
        {
            LastLimit = limit;
            return Task.FromResult(Answer);
        }
    }
}
