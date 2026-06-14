using System.Text.Json;
using RfcRag.Answering;
using RfcRag.Cli;
using RfcRag.Models;
using RfcRag.Search;
using RfcRag.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace RfcRag.Tests.UnitTests;

public sealed class CliCommandTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task RunAsync_SearchVerb_WritesSearchResultsJson()
    {
        var fakeService = new FakeSearchService
        {
            SearchResults =
            [
                new SearchResult(Guid.NewGuid(), 9110, "HTTP Semantics", "1", null, "intro", "/rfc9110.txt", "https://example.com", 0.9)
            ]
        };
        var command = new CliCommand(fakeService, new ContextAssembler(fakeService), NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        await command.RunAsync(["search", "HTTP semantics"], writer, CancellationToken.None);

        var results = JsonSerializer.Deserialize<SearchResult[]>(writer.ToString(), JsonOptions);
        Assert.NotNull(results);
        Assert.Single(results!);
        Assert.Equal(9110, results![0].RfcNumber);
    }

    [Fact]
    public async Task RunAsync_SearchVerb_RespectsLimitFlag()
    {
        var fakeService = new FakeSearchService();
        var command = new CliCommand(fakeService, new ContextAssembler(fakeService), NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync(["search", "HTTP semantics", "--limit", "5"], writer, CancellationToken.None);
        Assert.Equal(0, returnCode);
    }

    [Fact]
    public async Task RunAsync_SectionVerb_WritesSectionJson()
    {
        var section = new RfcSection { RfcNumber = 9110, Section = "8.6", Text = "Content negotiation" };
        var fakeService = new FakeSearchService { SingleSection = section };
        var command = new CliCommand(fakeService, new ContextAssembler(fakeService), NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        await command.RunAsync(["section", "9110", "8.6"], writer, CancellationToken.None);

        var result = JsonSerializer.Deserialize<RfcSection>(writer.ToString(), JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(9110, result!.RfcNumber);
        Assert.Equal("8.6", result.Section);
    }

    [Fact]
    public async Task RunAsync_NormativeVerb_WritesResultsJson()
    {
        var fakeService = new FakeSearchService
        {
            SearchResults =
            [
                new SearchResult(Guid.NewGuid(), 2119, "Key words", "1", null, "MUST", "/rfc2119.txt", "https://example.com", 1.0)
            ]
        };
        var command = new CliCommand(fakeService, new ContextAssembler(fakeService), NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync(["normative", "MUST"], writer, CancellationToken.None);
        Assert.Equal(0, returnCode);
    }

    [Fact]
    public async Task RunAsync_StatsVerb_WritesStatsJson()
    {
        const string expectedStats = """{"indexedRfcs":42,"sections":1000}""";
        var fakeService = new FakeSearchService { StatsJson = expectedStats };
        var command = new CliCommand(fakeService, new ContextAssembler(fakeService), NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        await command.RunAsync(["stats"], writer, CancellationToken.None);
        Assert.Equal(expectedStats, writer.ToString().Trim());
    }

    [Fact]
    public async Task RunAsync_UnknownVerb_ReturnsNonZero()
    {
        var fakeService = new FakeSearchService();
        var command = new CliCommand(fakeService, new ContextAssembler(fakeService), NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync(["unknown-verb"], writer, CancellationToken.None);
        Assert.NotEqual(0, returnCode);
    }

    [Fact]
    public async Task RunAsync_NoArgs_ReturnsNonZero()
    {
        var fakeService2 = new FakeSearchService();
        var command = new CliCommand(fakeService2, new ContextAssembler(fakeService2), NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync([], writer, CancellationToken.None);
        Assert.NotEqual(0, returnCode);
    }

    [Fact]
    public async Task RunAsync_SearchVerbMissingQuery_ReturnsNonZero()
    {
        var fakeService3 = new FakeSearchService();
        var command = new CliCommand(fakeService3, new ContextAssembler(fakeService3), NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync(["search"], writer, CancellationToken.None);
        Assert.NotEqual(0, returnCode);
    }

    [Fact]
    public async Task RunAsync_SectionVerbMissingArgs_ReturnsNonZero()
    {
        var fakeService4 = new FakeSearchService();
        var command = new CliCommand(fakeService4, new ContextAssembler(fakeService4), NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync(["section", "notanumber", "1"], writer, CancellationToken.None);
        Assert.NotEqual(0, returnCode);
    }

    [Fact]
    public async Task RunAsync_EvidenceVerb_WritesEvidencePackJson()
    {
        var section = new RfcSection
        {
            RfcNumber = 9110,
            Section = "9.3.1",
            Heading = "GET",
            Text = "The GET method requests transfer...",
            Title = "HTTP Semantics",
        };
        var fakeService = new FakeSearchService
        {
            SearchResults =
            [
                new SearchResult(Guid.NewGuid(), 9110, "HTTP Semantics", "9.3.1",
                    "GET", "The GET method...", "/rfc9110.txt",
                    "https://www.rfc-editor.org/rfc/rfc9110", 0.95),
            ],
            SectionMap = new Dictionary<(int, string), RfcSection>
            {
                [(9110, "9.3.1")] = section,
            },
            TocMap = new Dictionary<string, string?>
            {
                ["9"] = "Methods",
                ["9.3"] = "Request Methods",
                ["9.3.1"] = "GET",
            },
        };

        var command = new CliCommand(fakeService, new ContextAssembler(fakeService),
            NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync(
            ["evidence", "GET method"], writer, CancellationToken.None);
        Assert.Equal(0, returnCode);

        var pack = JsonSerializer.Deserialize<EvidencePack>(writer.ToString(), JsonOptions);
        Assert.NotNull(pack);
        Assert.Equal("GET method", pack!.Query);
        Assert.Single(pack.Sections);
        Assert.Equal("9110#9.3.1", pack.Sections[0].EvidenceId);
    }

    [Fact]
    public async Task RunAsync_EvidenceVerb_IncludesEstimatedTokensAndRelationNotes()
    {
        var section = new RfcSection
        {
            RfcNumber = 9110,
            Section = "9.3.1",
            Heading = "GET",
            Text = "The GET method requests transfer...",
            Title = "HTTP Semantics",
        };
        var fakeService = new FakeSearchService
        {
            SearchResults =
            [
                new SearchResult(Guid.NewGuid(), 9110, "HTTP Semantics", "9.3.1",
                    "GET", "The GET method...", "/rfc9110.txt",
                    "https://www.rfc-editor.org/rfc/rfc9110", 0.95)
                {
                    Status = new RfcStatusBlock { Category = RfcStatusCategory.Obsoleted, ObsoletedBy = [9112] },
                },
            ],
            SectionMap = new Dictionary<(int, string), RfcSection>
            {
                [(9110, "9.3.1")] = section,
            },
            TocMap = new Dictionary<string, string?>
            {
                ["9"] = "Methods",
                ["9.3"] = "Request Methods",
                ["9.3.1"] = "GET",
            },
        };

        var command = new CliCommand(fakeService, new ContextAssembler(fakeService),
            NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync(
            ["evidence", "GET method"], writer, CancellationToken.None);
        Assert.Equal(0, returnCode);

        var pack = JsonSerializer.Deserialize<EvidencePack>(writer.ToString(), JsonOptions);
        Assert.NotNull(pack);
        Assert.True(pack!.EstimatedTokens > 0);
        Assert.Single(pack.RelationNotes);
        Assert.Contains("obsoleted by", pack.RelationNotes[0]);
    }

    [Fact]
    public async Task RunAsync_EvidenceVerbMissingQuery_ReturnsNonZero()
    {
        var fakeService = new FakeSearchService();
        var command = new CliCommand(fakeService, new ContextAssembler(fakeService),
            NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync(["evidence"], writer, CancellationToken.None);
        Assert.NotEqual(0, returnCode);
    }

    [Fact]
    public async Task RunAsync_EvidenceVerb_RespectsLimitFlag()
    {
        var results = new List<SearchResult>();
        for (int i = 1; i <= 5; i++)
        {
            results.Add(new SearchResult(
                Guid.NewGuid(), 9110, "HTTP Semantics", i.ToString(), $"Section {i}",
                "excerpt", "/rfc9110.txt", "https://example.com", 1.0 - i * 0.01));
        }

        var sections = results.ToDictionary(
            r => (r.RfcNumber, r.Section),
            r => new RfcSection
            {
                RfcNumber = r.RfcNumber,
                Section = r.Section,
                Heading = r.Heading,
                Text = $"Section text {r.Section}",
            });

        var fakeService = new FakeSearchService
        {
            SearchResults = results.ToArray(),
            SectionMap = sections,
            TocMap = results.ToDictionary(r => r.Section, r => r.Heading),
        };
        var command = new CliCommand(fakeService, new ContextAssembler(fakeService),
            NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync(
            ["evidence", "HTTP", "--limit", "3"], writer, CancellationToken.None);
        Assert.Equal(0, returnCode);

        var pack = JsonSerializer.Deserialize<EvidencePack>(writer.ToString(), JsonOptions);
        Assert.NotNull(pack);
        Assert.Equal(3, pack!.Sections.Count);
    }

    [Fact]
    public async Task RunAsync_EvidenceVerb_RespectsBudgetFlag()
    {
        var results = new List<SearchResult>();
        for (int i = 1; i <= 3; i++)
        {
            results.Add(new SearchResult(
                Guid.NewGuid(), 9110, "HTTP Semantics", i.ToString(), $"Section {i}",
                "excerpt", "/rfc9110.txt", "https://example.com", 1.0 - i * 0.01));
        }

        var sections = results.ToDictionary(
            r => (r.RfcNumber, r.Section),
            r => new RfcSection
            {
                RfcNumber = r.RfcNumber,
                Section = r.Section,
                Heading = r.Heading,
                Text = new string('x', 100), // 100 chars each
            });

        var fakeService = new FakeSearchService
        {
            SearchResults = results.ToArray(),
            SectionMap = sections,
            TocMap = results.ToDictionary(r => r.Section, r => r.Heading),
        };
        var command = new CliCommand(fakeService, new ContextAssembler(fakeService),
            NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        // Budget 120 chars — allows section 1 but not section 2 or 3
        int returnCode = await command.RunAsync(
            ["evidence", "HTTP", "--budget", "120"], writer, CancellationToken.None);
        Assert.Equal(0, returnCode);

        var pack = JsonSerializer.Deserialize<EvidencePack>(writer.ToString(), JsonOptions);
        Assert.NotNull(pack);
        Assert.True(pack!.BudgetExceeded);
        Assert.Single(pack.Sections);
        Assert.Equal(120, pack.BudgetChars);
    }

    [Fact]
    public async Task RunAsync_AskVerb_WritesAnswerJson()
    {
        var fakeService = new FakeSearchService();
        var fakeAsk = new FakeAskService
        {
            Result = new GeneratedAnswer
            {
                Answer = "RFC 9110 defines HTTP semantics.",
                Citations =
                [
                    new Citation { EvidenceId = "9110#9.3.1", Section = "9.3.1", RfcNumber = 9110 },
                ],
            },
        };
        var command = new CliCommand(fakeService, new ContextAssembler(fakeService),
            NullLogger<CliCommand>.Instance, askService: fakeAsk);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync(
            ["ask", "How does HTTP work?"], writer, CancellationToken.None);

        Assert.Equal(0, returnCode);
        var answer = JsonSerializer.Deserialize<GeneratedAnswer>(writer.ToString(), JsonOptions);
        Assert.NotNull(answer);
        Assert.Equal("RFC 9110 defines HTTP semantics.", answer!.Answer);
        Assert.Single(answer.Citations);
        Assert.Equal(1, fakeAsk.CallCount);
    }

    [Fact]
    public async Task RunAsync_AskVerbMissingQuestion_ReturnsNonZero()
    {
        var fakeAsk = new FakeAskService();
        var fakeService = new FakeSearchService();
        var command = new CliCommand(fakeService, new ContextAssembler(fakeService),
            NullLogger<CliCommand>.Instance, askService: fakeAsk);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync(["ask"], writer, CancellationToken.None);

        Assert.NotEqual(0, returnCode);
        Assert.Equal(0, fakeAsk.CallCount);
    }

    [Fact]
    public async Task RunAsync_AskVerbNoAskService_ReturnsNonZero()
    {
        var fakeService = new FakeSearchService();
        var command = new CliCommand(fakeService, new ContextAssembler(fakeService),
            NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync(
            ["ask", "How does HTTP work?"], writer, CancellationToken.None);

        Assert.NotEqual(0, returnCode);
    }
}
