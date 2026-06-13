namespace RfcRag.Evaluation;

internal sealed record class GoldenQuestion(
    string Id,
    string Question,
    int[] ExpectedRfcs,
    string[] ExpectedSections,
    int[] MustCite,
    int[] ShouldNotCite,
    string AnswerType,
    string Corpus,
    bool IncludeObsolete = false);

internal sealed record class RetrievalQueryResult(
    string Id,
    string Question,
    string Corpus,
    bool HitAt1,
    bool HitAt5,
    bool HitAt10,
    double ReciprocalRank,
    double NdcgAt10,
    long LatencyMs,
    int[] TopKRfcs,
    string? Error);

internal sealed record class RetrievalAggregateMetrics(
    double HitAt1,
    double HitAt5,
    double HitAt10,
    double Mrr,
    double NdcgAt10,
    long AvgLatencyMs);

internal sealed record class RetrievalEvalReport(
    DateTimeOffset GeneratedAt,
    string Corpus,
    string? ManifestId,
    string? EmbeddingModel,
    string? ParserType,
    int QueriesRun,
    RetrievalAggregateMetrics Aggregate,
    IReadOnlyList<RetrievalQueryResult> Results,
    AnswerEvalReport? AnswerEval = null);

internal sealed record class AnswerEvaluationResult(
    string Id,
    string Question,
    string Corpus,
    int[] CitedRfcs,
    int[] MustCiteRfcs,
    int[] ForbiddenCitedRfcs,
    double CitationPrecision,
    double CitationRecall,
    double CitationF1,
    bool HasForbiddenCitations,
    bool? CorrectNoAnswer,
    string ExpectedAnswerType,
    long LatencyMs,
    string? Error,
    double QuoteFaithfulness = 0.0,
    double ObsoleteCitationRate = 0.0);

internal sealed record class AnswerAggregateMetrics(
    double AvgCitationPrecision,
    double AvgCitationRecall,
    double AvgCitationF1,
    double? NoAnswerAccuracy,
    double HallucinationRate,
    int TotalQuestions,
    int QuestionsWithCitations,
    double AvgQuoteFaithfulness = 0.0,
    double AvgObsoleteCitationRate = 0.0);

internal sealed record class AnswerEvalReport(
    DateTimeOffset GeneratedAt,
    string Corpus,
    int QueriesRun,
    AnswerAggregateMetrics Aggregate,
    IReadOnlyList<AnswerEvaluationResult> Results,
    string? ManifestId = null,
    string? EmbeddingModel = null,
    string? ParserType = null);
