using System.Text.Json;
using RfcRag.Infrastructure;
using RfcRag.Search;
using RfcRag.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace RfcRag.Tests.UnitTests;

public sealed class BenchmarkCommandTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task RunAsync_WithMatchingResults_OutputsJsonWithHit()
    {
        string queriesFile = CreateTempQueriesFile(
            new { query = "HTTP semantics", expectedRfcAny = new[] { 9110 } });
        var fakeService = new FakeSearchService
        {
            SearchResults = [new SearchResult(Guid.NewGuid(), 9110, "HTTP Semantics", "1", null, "intro", "/rfc9110.txt", "https://example.com", 0.9)]
        };
        var command = new BenchmarkCommand(fakeService, NullLogger<BenchmarkCommand>.Instance);
        using var writer = new StringWriter();
        await command.RunAsync(queriesFile, topK: 10, writer, CancellationToken.None);

        var report = JsonSerializer.Deserialize<BenchmarkReportDto>(writer.ToString(), JsonOptions);
        Assert.NotNull(report);
        Assert.Equal(1, report!.QueriesRun);
        Assert.Equal(1, report.HitCount);
        Assert.Equal(1.0, report.HitRate);
    }

    [Fact]
    public async Task RunAsync_WithMissingResult_OutputsJsonWithMiss()
    {
        string queriesFile = CreateTempQueriesFile(
            new { query = "TLS handshake", expectedRfcAny = new[] { 8446 } });
        var fakeService = new FakeSearchService
        {
            SearchResults = [new SearchResult(Guid.NewGuid(), 9110, "HTTP Semantics", "1", null, "intro", "/rfc9110.txt", "https://example.com", 0.5)]
        };
        var command = new BenchmarkCommand(fakeService, NullLogger<BenchmarkCommand>.Instance);
        using var writer = new StringWriter();
        await command.RunAsync(queriesFile, topK: 10, writer, CancellationToken.None);

        var report = JsonSerializer.Deserialize<BenchmarkReportDto>(writer.ToString(), JsonOptions);
        Assert.NotNull(report);
        Assert.Equal(0, report!.HitCount);
        Assert.Equal(0.0, report.HitRate);
    }

    [Fact]
    public async Task RunAsync_NonExistentFile_DoesNotThrow()
    {
        var command = new BenchmarkCommand(new FakeSearchService(), NullLogger<BenchmarkCommand>.Instance);
        using var writer = new StringWriter();
        var ex = await Record.ExceptionAsync(() =>
            command.RunAsync("/tmp/does-not-exist-benchmark.json", topK: 10, writer, CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task RunAsync_MultipleQueries_ReportsAggregateStats()
    {
        string queriesFile = CreateTempQueriesFile(
            new { query = "HTTP semantics", expectedRfcAny = new[] { 9110 } },
            new { query = "TLS handshake", expectedRfcAny = new[] { 8446 } });
        var fakeService = new FakeSearchService
        {
            SearchResults = [new SearchResult(Guid.NewGuid(), 9110, "HTTP Semantics", "1", null, "intro", "/rfc9110.txt", "https://example.com", 0.9)]
        };
        var command = new BenchmarkCommand(fakeService, NullLogger<BenchmarkCommand>.Instance);
        using var writer = new StringWriter();
        await command.RunAsync(queriesFile, topK: 10, writer, CancellationToken.None);

        var report = JsonSerializer.Deserialize<BenchmarkReportDto>(writer.ToString(), JsonOptions);
        Assert.NotNull(report);
        Assert.Equal(2, report!.QueriesRun);
        Assert.Equal(1, report.HitCount);
        Assert.Equal(0.5, report.HitRate);
    }

    private static string CreateTempQueriesFile(params object[] queries)
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"bench-{Guid.NewGuid():N}.json");
        File.WriteAllText(tempFile, JsonSerializer.Serialize(queries));
        return tempFile;
    }

    private sealed class BenchmarkReportDto
    {
        public int QueriesRun { get; set; }
        public int HitCount { get; set; }
        public double HitRate { get; set; }
        public long AvgTotalMs { get; set; }
        public long TotalElapsedMs { get; set; }
    }
}
