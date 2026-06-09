using System.Text.Json;
using RfcRag.Infrastructure;
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
        var command = new CliCommand(fakeService, NullLogger<CliCommand>.Instance);
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
        var command = new CliCommand(fakeService, NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync(["search", "HTTP semantics", "--limit", "5"], writer, CancellationToken.None);
        Assert.Equal(0, returnCode);
    }

    [Fact]
    public async Task RunAsync_SectionVerb_WritesSectionJson()
    {
        var section = new RfcSection { RfcNumber = 9110, Section = "8.6", Text = "Content negotiation" };
        var fakeService = new FakeSearchService { SingleSection = section };
        var command = new CliCommand(fakeService, NullLogger<CliCommand>.Instance);
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
        var command = new CliCommand(fakeService, NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync(["normative", "MUST"], writer, CancellationToken.None);
        Assert.Equal(0, returnCode);
    }

    [Fact]
    public async Task RunAsync_StatsVerb_WritesStatsJson()
    {
        const string expectedStats = """{"indexedRfcs":42,"sections":1000}""";
        var fakeService = new FakeSearchService { StatsJson = expectedStats };
        var command = new CliCommand(fakeService, NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        await command.RunAsync(["stats"], writer, CancellationToken.None);
        Assert.Equal(expectedStats, writer.ToString().Trim());
    }

    [Fact]
    public async Task RunAsync_UnknownVerb_ReturnsNonZero()
    {
        var command = new CliCommand(new FakeSearchService(), NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync(["unknown-verb"], writer, CancellationToken.None);
        Assert.NotEqual(0, returnCode);
    }

    [Fact]
    public async Task RunAsync_NoArgs_ReturnsNonZero()
    {
        var command = new CliCommand(new FakeSearchService(), NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync([], writer, CancellationToken.None);
        Assert.NotEqual(0, returnCode);
    }

    [Fact]
    public async Task RunAsync_SearchVerbMissingQuery_ReturnsNonZero()
    {
        var command = new CliCommand(new FakeSearchService(), NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync(["search"], writer, CancellationToken.None);
        Assert.NotEqual(0, returnCode);
    }

    [Fact]
    public async Task RunAsync_SectionVerbMissingArgs_ReturnsNonZero()
    {
        var command = new CliCommand(new FakeSearchService(), NullLogger<CliCommand>.Instance);
        using var writer = new StringWriter();
        int returnCode = await command.RunAsync(["section", "notanumber", "1"], writer, CancellationToken.None);
        Assert.NotEqual(0, returnCode);
    }
}
