using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using RfcRag.Evaluation;

namespace RfcRag.Cli;

internal sealed class EvalCommand(
    ISearchService searchService,
    IndexingRepository indexingRepository,
    TimeProvider timeProvider,
    ILogger<EvalCommand> logger)
{
    private static readonly JsonSerializerOptions JsonReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Task<int> RunAsync(
        string questionsFilePath,
        int topK,
        string corpus,
        CancellationToken cancellationToken) =>
        RunAsync(questionsFilePath, topK, corpus, Console.Out, cancellationToken);

    public async Task<int> RunAsync(
        string questionsFilePath,
        int topK,
        string corpus,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(questionsFilePath);
        ArgumentNullException.ThrowIfNull(output);

        if (!File.Exists(questionsFilePath))
        {
            logger.LogError("Golden questions file not found: {Path}", questionsFilePath);
            return 1;
        }

        string json = await File.ReadAllTextAsync(questionsFilePath, cancellationToken).ConfigureAwait(false);
        var allQuestions = JsonSerializer.Deserialize<GoldenQuestion[]>(json, JsonReadOptions) ?? [];

        var questions = string.Equals(corpus, "all", StringComparison.Ordinal)
            ? allQuestions
            : allQuestions.Where(q => string.Equals(q.Corpus, corpus, StringComparison.Ordinal)).ToArray();

        if (questions.Length == 0)
        {
            logger.LogWarning("No questions matched corpus '{Corpus}' in {Path}", corpus, questionsFilePath);
            return 1;
        }

        logger.LogInformation(
            "Running eval: {Count} questions (corpus={Corpus}, top-{TopK})",
            questions.Length, corpus, topK);

        IndexManifest? manifest = await indexingRepository
            .GetLatestManifestAsync(cancellationToken).ConfigureAwait(false);

        var queryResults = new List<RetrievalQueryResult>(questions.Length);

        foreach (var question in questions)
        {
            var result = await RunQueryAsync(question, topK, cancellationToken).ConfigureAwait(false);
            queryResults.Add(result);
        }

        var aggregate = RetrievalMetrics.Aggregate(queryResults);
        var report = new RetrievalEvalReport(
            GeneratedAt: timeProvider.GetUtcNow(),
            Corpus: corpus,
            ManifestId: manifest?.Id.ToString(),
            EmbeddingModel: manifest?.EmbeddingModel,
            ParserType: manifest?.ParserType,
            QueriesRun: queryResults.Count,
            Aggregate: aggregate,
            Results: queryResults);

        await output.WriteLineAsync(JsonSerializer.Serialize(report, JsonWriteOptions))
            .ConfigureAwait(false);

        return 0;
    }

    private async Task<RetrievalQueryResult> RunQueryAsync(
        GoldenQuestion question,
        int topK,
        CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        IReadOnlyList<SearchResult> results;

        try
        {
            results = await searchService
                .SearchAsync(question.Question, topK, normativeKeyword: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            timer.Stop();
            logger.LogError(ex, "Search failed for question {Id}: {Question}", question.Id, question.Question);
            return new RetrievalQueryResult(
                question.Id, question.Question, question.Corpus,
                false, false, false, 0.0, 0.0, timer.ElapsedMilliseconds, [], ex.Message);
        }

        timer.Stop();

        int[] rankedRfcs = results.Select(r => r.RfcNumber).Distinct().ToArray();
        var rankedSections = results.Select(r => (Rfc: r.RfcNumber, r.Section)).ToArray();

        bool hasSectionExpectations = question.ExpectedSections.Length > 0;

        bool hitAt1 = hasSectionExpectations
            ? RetrievalMetrics.HitAtK(rankedSections, question.ExpectedRfcs, question.ExpectedSections, k: 1)
            : RetrievalMetrics.HitAtK(rankedRfcs, question.ExpectedRfcs, k: 1);
        bool hitAt5 = hasSectionExpectations
            ? RetrievalMetrics.HitAtK(rankedSections, question.ExpectedRfcs, question.ExpectedSections, k: 5)
            : RetrievalMetrics.HitAtK(rankedRfcs, question.ExpectedRfcs, k: 5);
        bool hitAt10 = hasSectionExpectations
            ? RetrievalMetrics.HitAtK(rankedSections, question.ExpectedRfcs, question.ExpectedSections, k: 10)
            : RetrievalMetrics.HitAtK(rankedRfcs, question.ExpectedRfcs, k: 10);
        double rr = hasSectionExpectations
            ? RetrievalMetrics.ReciprocalRank(rankedSections, question.ExpectedRfcs, question.ExpectedSections)
            : RetrievalMetrics.ReciprocalRank(rankedRfcs, question.ExpectedRfcs);
        double ndcg = hasSectionExpectations
            ? RetrievalMetrics.NdcgAtK(rankedSections, question.ExpectedRfcs, question.ExpectedSections, k: 10)
            : RetrievalMetrics.NdcgAtK(rankedRfcs, question.ExpectedRfcs, k: 10);

        return new RetrievalQueryResult(
            question.Id, question.Question, question.Corpus,
            hitAt1, hitAt5, hitAt10, rr, ndcg, timer.ElapsedMilliseconds, rankedRfcs, null);
    }
}
