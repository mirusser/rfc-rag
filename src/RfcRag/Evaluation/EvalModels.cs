namespace RfcRag.Evaluation;

internal sealed record class GoldenQuestion(
    string Id,
    string Question,
    int[] ExpectedRfcs,
    string[] ExpectedSections,
    int[] MustCite,
    int[] ShouldNotCite,
    string AnswerType,
    string Corpus);

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
    IReadOnlyList<RetrievalQueryResult> Results);
