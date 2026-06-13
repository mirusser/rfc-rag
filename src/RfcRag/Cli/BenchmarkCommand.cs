using System.Diagnostics;
using System.Text.Json;

namespace RfcRag.Cli;

internal sealed class BenchmarkCommand(ISearchService searchService, ILogger<BenchmarkCommand> logger)
{
    private static readonly JsonSerializerOptions jsonWriteOptions = new() { WriteIndented = false };
    private static readonly JsonSerializerOptions jsonReadOptions = new() { PropertyNameCaseInsensitive = true };

    public Task RunAsync(string queriesFilePath, int topK, CancellationToken cancellationToken) =>
        RunAsync(queriesFilePath, topK, Console.Out, cancellationToken);

    public async Task RunAsync(string queriesFilePath, int topK, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queriesFilePath);
        ArgumentNullException.ThrowIfNull(output);

        if (!File.Exists(queriesFilePath))
        {
            logger.LogError("Benchmark queries file not found: {Path}", queriesFilePath);
            return;
        }

        string json = await File.ReadAllTextAsync(queriesFilePath, cancellationToken).ConfigureAwait(false);
        var queries = JsonSerializer.Deserialize<BenchmarkQuery[]>(json, jsonReadOptions) ?? [];

        logger.LogInformation("Running benchmark with {Count} queries, top-{TopK}", queries.Length, topK);

        var queryResults = new List<BenchmarkQueryResult>(queries.Length);
        var totalTimer = Stopwatch.StartNew();

        foreach (var query in queries)
        {
            var result = await RunQueryAsync(query, topK, cancellationToken).ConfigureAwait(false);
            queryResults.Add(result);
        }

        totalTimer.Stop();

        int hitCount = queryResults.Count(r => r.Hit);
        double hitRate = queries.Length > 0 ? (double)hitCount / queries.Length : 0.0;
        long avgTotalMs = queryResults.Count > 0 ? (long)queryResults.Average(r => r.TotalMs) : 0L;

        var report = new BenchmarkReport(
            QueriesRun: queryResults.Count,
            HitCount: hitCount,
            HitRate: Math.Round(hitRate, 4, MidpointRounding.AwayFromZero),
            AvgTotalMs: avgTotalMs,
            TotalElapsedMs: totalTimer.ElapsedMilliseconds,
            Results: queryResults);

        await output.WriteLineAsync(JsonSerializer.Serialize(report, jsonWriteOptions)).ConfigureAwait(false);
    }

    private async Task<BenchmarkQueryResult> RunQueryAsync(
        BenchmarkQuery query,
        int topK,
        CancellationToken cancellationToken)
    {
        var totalTimer = Stopwatch.StartNew();

        IReadOnlyList<SearchResult> results;
        try
        {
            results = await searchService.SearchAsync(
                query.Query, topK, normativeKeyword: null, includeObsolete: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Search failed for query: {Query}", query.Query);
            totalTimer.Stop();
            return new BenchmarkQueryResult(
                Query: query.Query,
                TotalMs: totalTimer.ElapsedMilliseconds,
                TopKRfcs: [],
                Hit: false,
                Error: ex.Message);
        }

        totalTimer.Stop();

        int[] topRfcs = results.Select(r => r.RfcNumber).Distinct().ToArray();
        bool hit = query.ExpectedRfcAny is { Length: > 0 }
            && query.ExpectedRfcAny.Any(expected => topRfcs.Contains(expected));

        return new BenchmarkQueryResult(
            Query: query.Query,
            TotalMs: totalTimer.ElapsedMilliseconds,
            TopKRfcs: topRfcs,
            Hit: hit,
            Error: null);
    }

    private sealed class BenchmarkQuery
    {
        public string Query { get; init; } = string.Empty;
        public int[] ExpectedRfcAny { get; init; } = [];
    }

    private sealed record class BenchmarkQueryResult(
        string Query,
        long TotalMs,
        int[] TopKRfcs,
        bool Hit,
        string? Error);

    private sealed record class BenchmarkReport(
        int QueriesRun,
        int HitCount,
        double HitRate,
        long AvgTotalMs,
        long TotalElapsedMs,
        IReadOnlyList<BenchmarkQueryResult> Results);
}
