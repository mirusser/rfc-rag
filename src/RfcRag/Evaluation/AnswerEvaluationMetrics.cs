using RfcRag.Answering;

namespace RfcRag.Evaluation;

internal static class AnswerEvaluationMetrics
{
    /// <summary>
    /// Computes citation precision: fraction of uniquely cited RFCs that are in the must-cite set.
    /// </summary>
    public static double CitationPrecision(int[] citedRfcs, int[] mustCiteRfcs)
    {
        if (citedRfcs.Length == 0)
            return 0.0;

        if (mustCiteRfcs.Length == 0)
            return 1.0;

        var mustSet = new HashSet<int>(mustCiteRfcs);
        int matched = citedRfcs.Count(mustSet.Contains);
        return (double)matched / citedRfcs.Length;
    }

    /// <summary>
    /// Computes citation recall: fraction of must-cite RFCs that appear in the cited set.
    /// </summary>
    public static double CitationRecall(int[] citedRfcs, int[] mustCiteRfcs)
    {
        if (mustCiteRfcs.Length == 0)
            return 1.0;

        if (citedRfcs.Length == 0)
            return 0.0;

        var citedSet = new HashSet<int>(citedRfcs);
        int matched = mustCiteRfcs.Count(citedSet.Contains);
        return (double)matched / mustCiteRfcs.Length;
    }

    /// <summary>Harmonic mean of precision and recall.</summary>
    public static double CitationF1(double precision, double recall)
    {
        if (precision <= 0.0 || recall <= 0.0)
            return 0.0;

        return 2.0 * precision * recall / (precision + recall);
    }

    /// <summary>
    /// Returns the subset of cited RFCs that appear in the should-not-cite set.
    /// </summary>
    public static int[] GetForbiddenCitedRfcs(int[] citedRfcs, int[] shouldNotCiteRfcs)
    {
        if (shouldNotCiteRfcs.Length == 0 || citedRfcs.Length == 0)
            return [];

        var forbidden = new HashSet<int>(shouldNotCiteRfcs);
        return citedRfcs.Where(forbidden.Contains).ToArray();
    }

    /// <summary>
    /// Returns true when the answer correctly declined a no-answer type question,
    /// or the answer produced citations when a factual/normative answer was expected.
    /// </summary>
    public static bool? CorrectlyDeclined(bool noAnswer, string expectedAnswerType)
    {
        if (!string.Equals(expectedAnswerType, "no_answer", StringComparison.Ordinal))
            return null; // N/A

        return noAnswer;
    }

    /// <summary>Computes quote-faithfulness: fraction of citations with non-null RelevantText.</summary>
    public static double ComputeQuoteFaithfulness(IReadOnlyList<Citation> citations)
    {
        if (citations.Count == 0)
            return 0.0;

        int withQuotes = citations.Count(c => !string.IsNullOrWhiteSpace(c.RelevantText));
        return (double)withQuotes / citations.Count;
    }

    public static double ComputeQuoteFaithfulness(IReadOnlyList<Citation> citations, EvidencePack evidencePack)
    {
        ArgumentNullException.ThrowIfNull(citations);
        ArgumentNullException.ThrowIfNull(evidencePack);

        if (citations.Count == 0)
            return 0.0;

        IReadOnlyList<Citation> verifiedCitations = CitationDiscipline.VerifyCitations(citations, evidencePack);
        return (double)verifiedCitations.Count / citations.Count;
    }

    /// <summary>
    /// Computes obsolete-citation rate: the fraction of unique cited RFCs that
    /// are known to be obsolete (present in <paramref name="obsoleteRfcs"/>).
    /// </summary>
    public static double ComputeObsoleteCitationRate(int[] citedRfcs, int[] obsoleteRfcs)
    {
        if (citedRfcs.Length == 0)
            return 0.0;

        if (obsoleteRfcs.Length == 0)
            return 0.0;

        var obsoleteSet = new HashSet<int>(obsoleteRfcs);
        int obsoleteCited = citedRfcs.Count(obsoleteSet.Contains);
        return (double)obsoleteCited / citedRfcs.Length;
    }

    /// <summary>
    /// Evaluates a single <see cref="GeneratedAnswer"/> against the expectations
    /// defined in a <see cref="GoldenQuestion"/>.
    /// </summary>
    public static AnswerEvaluationResult Evaluate(
        GoldenQuestion question,
        GeneratedAnswer answer,
        long latencyMs,
        int[]? obsoleteRfcs = null,
        EvidencePack? evidencePack = null)
    {
        int[] citedRfcs = answer.Citations
            .Select(c => c.RfcNumber)
            .Distinct()
            .Order()
            .ToArray();

        double precision = CitationPrecision(citedRfcs, question.MustCite);
        double recall = CitationRecall(citedRfcs, question.MustCite);
        double f1 = CitationF1(precision, recall);

        int[] forbiddenCited = GetForbiddenCitedRfcs(citedRfcs, question.ShouldNotCite);
        bool hasForbidden = forbiddenCited.Length > 0;

        bool? correctNoAnswer = CorrectlyDeclined(answer.NoAnswer, question.AnswerType);

        double quoteFaithfulness = evidencePack is null
            ? ComputeQuoteFaithfulness(answer.Citations)
            : ComputeQuoteFaithfulness(answer.Citations, evidencePack);
        double obsoleteCitationRate = ComputeObsoleteCitationRate(citedRfcs, obsoleteRfcs ?? []);

        return new AnswerEvaluationResult(
            question.Id,
            question.Question,
            question.Corpus,
            citedRfcs,
            question.MustCite,
            forbiddenCited,
            precision,
            recall,
            f1,
            hasForbidden,
            correctNoAnswer,
            question.AnswerType,
            latencyMs,
            null,
            quoteFaithfulness,
            obsoleteCitationRate);
    }

    /// <summary>
    /// Aggregates individual answer evaluation results into summary metrics.
    /// </summary>
    public static AnswerAggregateMetrics Aggregate(IReadOnlyList<AnswerEvaluationResult> results)
    {
        if (results.Count == 0)
        {
            return new AnswerAggregateMetrics(0.0, 0.0, 0.0, null, 0.0, 0, 0);
        }

        double avgPrecision = results.Average(r => r.CitationPrecision);
        double avgRecall = results.Average(r => r.CitationRecall);
        double avgF1 = results.Average(r => r.CitationF1);

        int questionsWithCitations = results.Count(r => r.CitedRfcs.Length > 0);

        double hallucinationRate = results.Count(r => r.HasForbiddenCitations) / (double)results.Count;

        var noAnswerResults = results
            .Where(r => r.CorrectNoAnswer is not null)
            .ToList();

        double? noAnswerAccuracy = noAnswerResults.Count > 0
            ? noAnswerResults.Average(r => r.CorrectNoAnswer is true ? 1.0 : 0.0)
            : null;

        double avgQuoteFaithfulness = results.Average(r => r.QuoteFaithfulness);
        double avgObsoleteCitationRate = results.Average(r => r.ObsoleteCitationRate);

        return new AnswerAggregateMetrics(
            avgPrecision,
            avgRecall,
            avgF1,
            noAnswerAccuracy,
            hallucinationRate,
            results.Count,
            questionsWithCitations,
            avgQuoteFaithfulness,
            avgObsoleteCitationRate);
    }
}
