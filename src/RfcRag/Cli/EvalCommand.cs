using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using RfcRag.Answering;
using RfcRag.Evaluation;

namespace RfcRag.Cli;

internal sealed class EvalCommand(
    ISearchService searchService,
    IndexingRepository indexingRepository,
    TimeProvider timeProvider,
    ILogger<EvalCommand> logger,
    IAskService? askService = null)
{
    private const int ObsoleteMetadataPageSize = 1_000;

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<int> RunRetrievalEvalAsync(
        string questionsFilePath,
        int topK,
        string corpus,
        CancellationToken cancellationToken) =>
        RunRetrievalEvalAsync(questionsFilePath, topK, corpus, Console.Out, cancellationToken);

    public async Task<int> RunRetrievalEvalAsync(
        string questionsFilePath,
        int topK,
        string corpus,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);

        GoldenQuestion[]? questions = await LoadQuestionsAsync(
            questionsFilePath,
            corpus,
            cancellationToken).ConfigureAwait(false);

        if (questions is null)
            return 1;

        logger.LogInformation(
            "Running retrieval eval: {Count} questions (corpus={Corpus}, top-{TopK})",
            questions.Length,
            corpus,
            topK);

        IndexManifest? manifest = await indexingRepository
            .GetLatestManifestAsync(cancellationToken).ConfigureAwait(false);

        var queryResults = new List<RetrievalQueryResult>(questions.Length);
        foreach (GoldenQuestion question in questions)
        {
            RetrievalQueryResult result = await RunRetrievalQueryAsync(
                question,
                topK,
                cancellationToken).ConfigureAwait(false);

            queryResults.Add(result);
        }

        RetrievalEvalReport report = CreateRetrievalReport(corpus, manifest, queryResults, answerEval: null);
        await output.WriteLineAsync(JsonSerializer.Serialize(report, JsonWriteOptions)).ConfigureAwait(false);
        return 0;
    }

    public Task<int> RunAnswerEvalAsync(
        string questionsFilePath,
        string corpus,
        CancellationToken cancellationToken) =>
        RunAnswerEvalAsync(questionsFilePath, topK: 10, corpus, Console.Out, cancellationToken);

    public Task<int> RunAnswerEvalAsync(
        string questionsFilePath,
        string corpus,
        TextWriter output,
        CancellationToken cancellationToken) =>
        RunAnswerEvalAsync(questionsFilePath, topK: 10, corpus, output, cancellationToken);

    public Task<int> RunAnswerEvalAsync(
        string questionsFilePath,
        int topK,
        string corpus,
        CancellationToken cancellationToken) =>
        RunAnswerEvalAsync(questionsFilePath, topK, corpus, Console.Out, cancellationToken);

    public async Task<int> RunAnswerEvalAsync(
        string questionsFilePath,
        int topK,
        string corpus,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (askService is null)
        {
            logger.LogError("Answer evaluation requires a configured LLM provider (IAskService is not registered)");
            return 1;
        }

        GoldenQuestion[]? questions = await LoadQuestionsAsync(
            questionsFilePath,
            corpus,
            cancellationToken).ConfigureAwait(false);

        if (questions is null)
            return 1;

        logger.LogInformation(
            "Running answer eval: {Count} questions (corpus={Corpus}, top-{TopK})",
            questions.Length,
            corpus,
            topK);

        IndexManifest? manifest = await indexingRepository
            .GetLatestManifestAsync(cancellationToken).ConfigureAwait(false);
        int[] obsoleteRfcs = await LoadObsoleteRfcsAsync(cancellationToken).ConfigureAwait(false);

        var retrievalResults = new List<RetrievalQueryResult>(questions.Length);
        var answerResults = new List<AnswerEvaluationResult>(questions.Length);

        foreach (GoldenQuestion question in questions)
        {
            RetrievalQueryResult retrievalResult = await RunRetrievalQueryAsync(
                question,
                topK,
                cancellationToken).ConfigureAwait(false);
            retrievalResults.Add(retrievalResult);

            AnswerEvaluationResult answerResult = await RunAnswerQueryAsync(
                question,
                topK,
                obsoleteRfcs,
                cancellationToken).ConfigureAwait(false);
            answerResults.Add(answerResult);
        }

        var answerEval = new AnswerEvalReport(
            GeneratedAt: timeProvider.GetUtcNow(),
            Corpus: corpus,
            QueriesRun: answerResults.Count,
            Aggregate: AnswerEvaluationMetrics.Aggregate(answerResults),
            Results: answerResults);

        RetrievalEvalReport report = CreateRetrievalReport(corpus, manifest, retrievalResults, answerEval);
        await output.WriteLineAsync(JsonSerializer.Serialize(report, JsonWriteOptions)).ConfigureAwait(false);
        return 0;
    }

    private async Task<GoldenQuestion[]?> LoadQuestionsAsync(
        string questionsFilePath,
        string corpus,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(questionsFilePath);

        if (!File.Exists(questionsFilePath))
        {
            logger.LogError("Golden questions file not found: {Path}", questionsFilePath);
            return null;
        }

        string json = await File.ReadAllTextAsync(questionsFilePath, cancellationToken).ConfigureAwait(false);
        var allQuestions = JsonSerializer.Deserialize<GoldenQuestion[]>(json, JsonReadOptions) ?? [];
        GoldenQuestion[] questions = string.Equals(corpus, "all", StringComparison.Ordinal)
            ? allQuestions
            : allQuestions
                .Where(q => string.Equals(q.Corpus, corpus, StringComparison.Ordinal))
                .ToArray();

        if (questions.Length == 0)
        {
            logger.LogWarning("No questions matched corpus '{Corpus}' in {Path}", corpus, questionsFilePath);
            return null;
        }

        return questions;
    }

    private RetrievalEvalReport CreateRetrievalReport(
        string corpus,
        IndexManifest? manifest,
        IReadOnlyList<RetrievalQueryResult> results,
        AnswerEvalReport? answerEval) =>
        new(
            GeneratedAt: timeProvider.GetUtcNow(),
            Corpus: corpus,
            ManifestId: manifest?.Id.ToString(),
            EmbeddingModel: manifest?.EmbeddingModel,
            ParserType: manifest?.ParserType,
            QueriesRun: results.Count,
            Aggregate: RetrievalMetrics.Aggregate(results),
            Results: results,
            AnswerEval: answerEval);

    private async Task<int[]> LoadObsoleteRfcsAsync(CancellationToken cancellationToken)
    {
        var obsoleteRfcs = new HashSet<int>();
        int offset = 0;

        while (true)
        {
            IReadOnlyList<RfcMetadata> page = await searchService
                .ListIndexedAsync(ObsoleteMetadataPageSize, offset, cancellationToken)
                .ConfigureAwait(false);

            foreach (RfcMetadata metadata in page)
            {
                foreach (int obsoleteRfc in metadata.Obsoletes)
                {
                    obsoleteRfcs.Add(obsoleteRfc);
                }
            }

            if (page.Count < ObsoleteMetadataPageSize)
                break;

            offset += page.Count;
        }

        return obsoleteRfcs.Order().ToArray();
    }

    private async Task<RetrievalQueryResult> RunRetrievalQueryAsync(
        GoldenQuestion question,
        int topK,
        CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();

        try
        {
            IReadOnlyList<SearchResult> results = await searchService
                .SearchAsync(question.Question, topK, normativeKeyword: null, includeObsolete: false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

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
            double reciprocalRank = hasSectionExpectations
                ? RetrievalMetrics.ReciprocalRank(rankedSections, question.ExpectedRfcs, question.ExpectedSections)
                : RetrievalMetrics.ReciprocalRank(rankedRfcs, question.ExpectedRfcs);
            double ndcgAt10 = hasSectionExpectations
                ? RetrievalMetrics.NdcgAtK(rankedSections, question.ExpectedRfcs, question.ExpectedSections, k: 10)
                : RetrievalMetrics.NdcgAtK(rankedRfcs, question.ExpectedRfcs, k: 10);

            return new RetrievalQueryResult(
                question.Id,
                question.Question,
                question.Corpus,
                hitAt1,
                hitAt5,
                hitAt10,
                reciprocalRank,
                ndcgAt10,
                timer.ElapsedMilliseconds,
                rankedRfcs,
                Error: null);
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
                question.Id,
                question.Question,
                question.Corpus,
                HitAt1: false,
                HitAt5: false,
                HitAt10: false,
                ReciprocalRank: 0.0,
                NdcgAt10: 0.0,
                LatencyMs: timer.ElapsedMilliseconds,
                TopKRfcs: [],
                Error: ex.Message);
        }
    }

    private async Task<AnswerEvaluationResult> RunAnswerQueryAsync(
        GoldenQuestion question,
        int topK,
        int[] obsoleteRfcs,
        CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();

        try
        {
            GeneratedAnswer answer = await askService!
                .AskAsync(
                    question.Question,
                    limit: topK,
                    includeObsolete: question.IncludeObsolete,
                    includeErrata: question.IncludeErrata,
                    errataStatus: question.ErrataStatus,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            timer.Stop();
            return AnswerEvaluationMetrics.Evaluate(
                question,
                answer,
                timer.ElapsedMilliseconds,
                obsoleteRfcs);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            timer.Stop();
            logger.LogError(ex, "Answer generation failed for question {Id}: {Question}", question.Id, question.Question);
            return new AnswerEvaluationResult(
                Id: question.Id,
                Question: question.Question,
                Corpus: question.Corpus,
                CitedRfcs: [],
                MustCiteRfcs: question.MustCite,
                ForbiddenCitedRfcs: [],
                CitationPrecision: 0.0,
                CitationRecall: 0.0,
                CitationF1: 0.0,
                HasForbiddenCitations: false,
                CorrectNoAnswer: null,
                ExpectedAnswerType: question.AnswerType,
                LatencyMs: timer.ElapsedMilliseconds,
                Error: ex.Message);
        }
    }
}
