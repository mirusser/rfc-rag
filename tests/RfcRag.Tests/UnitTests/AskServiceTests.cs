using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using RfcRag.Answering;
using RfcRag.Models;
using RfcRag.Search;
using RfcRag.Settings;
using RfcRag.Tests.Fakes;

namespace RfcRag.Tests.UnitTests;

public sealed class AskServiceTests
{
    private static readonly RfcRagOptions DefaultOptions = new()
    {
        RfcMirrorPath = "/tmp/test-mirror",
        PostgresConnectionString = "Host=localhost;Database=test",
        ChatModel = "test-model",
    };

    private const string ValidJsonResponse = """
    {
        "answer": "RFC 9110 defines HTTP semantics.",
        "citations": [
            { "evidenceId": "9110#9.3.1", "relevantText": "GET method" }
        ]
    }
    """;

    private static RfcSection MakeSection(int rfcNumber, string section, string? heading, string text)
    {
        return new RfcSection
        {
            RfcNumber = rfcNumber,
            Section = section,
            Heading = heading,
            Text = text,
            Title = $"RFC {rfcNumber}",
            SourcePath = $"/rfc{rfcNumber}.txt",
            Url = $"https://www.rfc-editor.org/rfc/rfc{rfcNumber}",
        };
    }

    private static SearchResult MakeResult(int rfcNumber, string section, string? heading, string excerpt, double score)
    {
        return new SearchResult(
            Guid.NewGuid(),
            rfcNumber,
            $"RFC {rfcNumber}",
            section,
            heading,
            excerpt,
            $"/rfc{rfcNumber}.txt",
            $"https://www.rfc-editor.org/rfc/rfc{rfcNumber}",
            score);
    }

    private static AskService CreateAskService(
        FakeSearchService? searchService = null,
        FakeChatClient? chatClient = null,
        FakeTraceWriter? traceWriter = null)
    {
        searchService ??= new FakeSearchService();
        chatClient ??= new FakeChatClient(ValidJsonResponse);
        traceWriter ??= new FakeTraceWriter();

        var options = Options.Create(DefaultOptions);
        var assembler = new ContextAssembler(searchService);
        var generator = new AnswerGenerator(chatClient, options);

        return new AskService(searchService, assembler, generator, options, traceWriter);
    }

    [Fact]
    public async Task AskAsync_ValidQuery_ReturnsAnswer()
    {
        var section = MakeSection(9110, "9.3.1", "GET", "GET method text for HTTP.");
        var result = MakeResult(9110, "9.3.1", "GET", "GET method...", 0.95);
        var toc = new Dictionary<string, string?> { ["9.3.1"] = "GET" };

        var searchService = new FakeSearchService
        {
            SearchResults = [result],
            SingleSection = section,
            TocMap = toc,
        };

        var chatClient = new FakeChatClient(ValidJsonResponse);
        var askService = CreateAskService(searchService, chatClient);

        var answer = await askService.AskAsync("How does HTTP work?", cancellationToken: CancellationToken.None);

        Assert.NotNull(answer);
        Assert.False(answer.NoAnswer);
        Assert.Equal("RFC 9110 defines HTTP semantics.", answer.Answer);
        Assert.Single(answer.Citations);
        Assert.Equal("9110#9.3.1", answer.Citations[0].EvidenceId);
    }

    [Fact]
    public async Task AskAsync_QueryPlannerEnabled_ReportsRetrievalPlan()
    {
        var section = MakeSection(9110, "9.3.1", "GET", "Forbidden HTTP method text.");
        var result = MakeResult(9110, "9.3.1", "GET", "Forbidden HTTP method...", 0.95);
        var searchService = new FakeSearchService
        {
            SearchResults = [result],
            SingleSection = section,
            TocMap = new Dictionary<string, string?> { ["9.3.1"] = "GET" },
        };
        var askService = CreateAskService(searchService);

        GeneratedAnswer answer = await askService.AskAsync(
            "Which behavior is forbidden for HTTP?",
            cancellationToken: CancellationToken.None);

        Assert.Equal("MUST NOT", searchService.LastNormativeKeyword);
        Assert.NotNull(answer.Retrieval);
        Assert.Equal("query-planner", answer.Retrieval.Strategy);
        Assert.Equal("MUST NOT", answer.Retrieval.Filters.NormativeKeyword);
        Assert.Equal("MUST NOT", answer.Retrieval.Plan?.SuggestedNormativeKeyword);
        Assert.Contains(9110, answer.Retrieval.Plan?.ProtocolRfcNumbers ?? []);
    }

    [Fact]
    public async Task AskAsync_NoSearchResults_ReturnsNoAnswer()
    {
        var askService = CreateAskService();

        var answer = await askService.AskAsync("Unknown topic", cancellationToken: CancellationToken.None);

        Assert.NotNull(answer);
        Assert.True(answer.NoAnswer);
        Assert.Equal("I could not find support for answering this question in the indexed RFC corpus.", answer.Answer);
    }

    [Fact]
    public async Task AskAsync_Cancellation_ThrowsOperationCanceled()
    {
        var searchService = new FakeSearchService
        {
            SearchResults = [MakeResult(9110, "1", "Intro", "text", 0.9)],
            SingleSection = MakeSection(9110, "1", "Intro", "text"),
        };
        var chatClient = new FakeChatClient(ValidJsonResponse);
        var askService = CreateAskService(searchService, chatClient);

        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            askService.AskAsync("test", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task AskAsync_ChatFailure_PropagatesException()
    {
        var section = MakeSection(9110, "1", "Intro", "Some text about HTTP.");
        var result = MakeResult(9110, "1", "Intro", "Some text...", 0.9);
        var toc = new Dictionary<string, string?> { ["1"] = "Intro" };

        var searchService = new FakeSearchService
        {
            SearchResults = [result],
            SingleSection = section,
            TocMap = toc,
        };

        // A chat client that throws on any call
        var failingChat = new ThrowingChatClient(new InvalidOperationException("API failure"));

        var options = Options.Create(DefaultOptions);
        var assembler = new ContextAssembler(searchService);
        var generator = new AnswerGenerator(failingChat, options);
        var askService = new AskService(searchService, assembler, generator, options, new FakeTraceWriter());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            askService.AskAsync("How does HTTP work?", cancellationToken: CancellationToken.None));
        Assert.Equal("API failure", ex.Message);
    }

    /// <summary>Fake that throws when GetResponseAsync is called.</summary>
    private sealed class ThrowingChatClient : IChatClient
    {
        private readonly Exception _exception;

        public ThrowingChatClient(Exception exception) => _exception = exception;

        public void Dispose() { }
        public object? GetService(Type? serviceType, object? key = null) => null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw _exception;

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw _exception;
    }

    [Fact]
    public async Task AskAsync_TraceCaptured_HasAllStages()
    {
        var section = MakeSection(9110, "9.3.1", "GET", "GET method text for HTTP.");
        var result = MakeResult(9110, "9.3.1", "GET", "GET method...", 0.95);
        var toc = new Dictionary<string, string?> { ["9.3.1"] = "GET" };

        var searchService = new FakeSearchService
        {
            SearchResults = [result],
            SingleSection = section,
            TocMap = toc,
        };
        var traceWriter = new FakeTraceWriter();
        var askService = CreateAskService(searchService, traceWriter: traceWriter);

        var answer = await askService.AskAsync("How does HTTP work?", cancellationToken: CancellationToken.None);

        Assert.NotNull(traceWriter.LastTrace);
        Assert.Equal("How does HTTP work?", traceWriter.LastTrace.Question);
        Assert.True(traceWriter.LastTrace.AnswerGenerated);
        Assert.NotEmpty(traceWriter.LastTrace.Stages);
        Assert.Contains(9110, traceWriter.LastTrace.CandidateRfcNumbers);

        Assert.Equal(3, traceWriter.LastTrace.Stages.Count);
        Assert.Equal("search", traceWriter.LastTrace.Stages[0].Name);
        Assert.Equal("assemble", traceWriter.LastTrace.Stages[1].Name);
        Assert.Equal("generate", traceWriter.LastTrace.Stages[2].Name);

        Assert.True(traceWriter.LastTrace.Stages[0].CompletedAtUtc >= traceWriter.LastTrace.Stages[0].StartedAtUtc);
        Assert.True(traceWriter.LastTrace.Stages[1].CompletedAtUtc >= traceWriter.LastTrace.Stages[1].StartedAtUtc);
        Assert.True(traceWriter.LastTrace.Stages[2].CompletedAtUtc >= traceWriter.LastTrace.Stages[2].StartedAtUtc);
    }

    [Fact]
    public async Task AskAsync_TraceCaptured_HasTimestampAndTraceId()
    {
        var section = MakeSection(9110, "9.3.1", "GET", "GET method text for HTTP.");
        var result = MakeResult(9110, "9.3.1", "GET", "GET method...", 0.95);
        var toc = new Dictionary<string, string?> { ["9.3.1"] = "GET" };

        var searchService = new FakeSearchService
        {
            SearchResults = [result],
            SingleSection = section,
            TocMap = toc,
        };
        var traceWriter = new FakeTraceWriter();
        var askService = CreateAskService(searchService, traceWriter: traceWriter);

        await askService.AskAsync("test", cancellationToken: CancellationToken.None);

        Assert.NotNull(traceWriter.LastTrace);
        Assert.NotNull(traceWriter.LastTrace.TraceId);
        Assert.NotEmpty(traceWriter.LastTrace.TraceId);
        // TimestampUtc should be close to now
        var age = DateTime.UtcNow - traceWriter.LastTrace.TimestampUtc;
        Assert.True(age < TimeSpan.FromMinutes(1), "Trace timestamp should be recent");
    }

    [Fact]
    public async Task AskAsync_NoResults_TraceCapturedWithEmptyCandidates()
    {
        var traceWriter = new FakeTraceWriter();
        var askService = CreateAskService(traceWriter: traceWriter);

        var answer = await askService.AskAsync("Unknown topic", cancellationToken: CancellationToken.None);

        Assert.NotNull(traceWriter.LastTrace);
        Assert.Empty(traceWriter.LastTrace.CandidateRfcNumbers);
        Assert.True(answer.NoAnswer);
    }
}
