namespace RfcRag.Search;

internal static class DeterministicReranker
{
    private const double RfcNumberMatchBonus = 0.12;
    private const double SectionMatchBonus = 0.10;
    private const double UpdatedByRelevanceBonus = 0.06;
    private const double HeadingTermMatchBonus = 0.05;
    private const double ProtocolRfcBonus = 0.04;
    private const double ObsoletedRfcPenalty = -0.10;

    private const int MinQueryTermLength = 3;

    private static readonly char[] queryTermSeparators =
        [' ', '\t', '\n', '.', ',', '?', '!', '(', ')', '[', ']', ':', ';', '"', '\''];

    internal static IReadOnlyList<SearchResult> Rerank(
        string query,
        IReadOnlyList<HybridCandidate> candidates,
        QueryPlan? queryPlan,
        IReadOnlyDictionary<int, RfcRelationsBatch> rfcStatuses,
        int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(rfcStatuses);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        if (candidates.Count == 0)
            return [];

        var rfcNumberSet = queryPlan is not null
            ? queryPlan.RfcNumbers.ToHashSet()
            : (HashSet<int>)[];

        var sectionRefSet = queryPlan is not null
            ? queryPlan.SectionReferences
                .Select(r => (r.RfcNumber, r.Section))
                .ToHashSet()
            : (HashSet<(int, string)>)[];

        var protocolRfcSet = queryPlan is not null
            ? queryPlan.ProtocolRfcNumbers.ToHashSet()
            : (HashSet<int>)[];

        bool includeObsolete = queryPlan?.IncludeObsolete ?? false;
        HashSet<string> queryTerms = BuildQueryTermSet(query);

        return candidates
            .Select(c => (Candidate: c, Score: ScoreCandidate(
                c, queryTerms, rfcNumberSet, sectionRefSet, protocolRfcSet, rfcStatuses, includeObsolete)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Candidate.RfcNumber)
            .ThenBy(x => x.Candidate.Section, StringComparer.Ordinal)
            .Take(limit)
            .Select(x => new SearchResult(
                x.Candidate.Id,
                x.Candidate.RfcNumber,
                x.Candidate.Title,
                x.Candidate.Section,
                x.Candidate.Heading,
                x.Candidate.Excerpt,
                x.Candidate.SourcePath,
                x.Candidate.Url,
                x.Score))
            .ToArray();
    }

    private static double ScoreCandidate(
        HybridCandidate candidate,
        HashSet<string> queryTerms,
        HashSet<int> rfcNumberSet,
        HashSet<(int RfcNumber, string Section)> sectionRefSet,
        HashSet<int> protocolRfcSet,
        IReadOnlyDictionary<int, RfcRelationsBatch> rfcStatuses,
        bool includeObsolete)
    {
        double score = candidate.RrfScore;

        if (rfcNumberSet.Contains(candidate.RfcNumber))
            score += RfcNumberMatchBonus;

        if (sectionRefSet.Contains((candidate.RfcNumber, candidate.Section)))
            score += SectionMatchBonus;

        if (protocolRfcSet.Contains(candidate.RfcNumber))
            score += ProtocolRfcBonus;

        if (candidate.Heading is not null && queryTerms.Count > 0)
        {
            foreach (string term in queryTerms)
            {
                if (candidate.Heading.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    score += HeadingTermMatchBonus;
                    break;
                }
            }
        }

        if (rfcStatuses.TryGetValue(candidate.RfcNumber, out var status))
        {
            if (!includeObsolete && status.ObsoletedBy.Count > 0)
                score += ObsoletedRfcPenalty;

            if (rfcNumberSet.Count > 0
                && (status.Obsoletes.Any(rfcNumberSet.Contains)
                    || status.Updates.Any(rfcNumberSet.Contains)))
            {
                score += UpdatedByRelevanceBonus;
            }
        }

        return score;
    }

    private static HashSet<string> BuildQueryTermSet(string query)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string word in query.Split(queryTermSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length >= MinQueryTermLength)
                terms.Add(word);
        }
        return terms;
    }
}
