namespace RfcRag.Evaluation;

internal static class RetrievalMetrics
{
    public static bool HitAtK(int[] rankedRfcs, int[] expectedRfcs, int k)
    {
        if (expectedRfcs.Length == 0)
            return false;

        var top = rankedRfcs.Take(k).ToHashSet();
        return expectedRfcs.Any(top.Contains);
    }

    public static double ReciprocalRank(int[] rankedRfcs, int[] expectedRfcs)
    {
        if (expectedRfcs.Length == 0)
            return 0.0;

        var expected = new HashSet<int>(expectedRfcs);
        for (int i = 0; i < rankedRfcs.Length; i++)
        {
            if (expected.Contains(rankedRfcs[i]))
                return 1.0 / (i + 1);
        }

        return 0.0;
    }

    public static double NdcgAtK(int[] rankedRfcs, int[] expectedRfcs, int k)
    {
        if (expectedRfcs.Length == 0 || rankedRfcs.Length == 0)
            return 0.0;

        var expected = new HashSet<int>(expectedRfcs);
        double dcg = ComputeDcg(rankedRfcs, expected, k);
        double idcg = ComputeIdealDcg(expectedRfcs.Length, k);

        return idcg <= double.Epsilon ? 0.0 : dcg / idcg;
    }

    public static bool HitAtK((int Rfc, string Section)[] ranked, int[] expectedRfcs, string[] expectedSections, int k)
    {
        if (expectedRfcs.Length == 0 || expectedSections.Length == 0)
            return false;

        var expectedPairs = BuildExpectedPairs(expectedRfcs, expectedSections);
        var top = ranked.Take(k).ToHashSet();
        return expectedPairs.Any(top.Contains);
    }

    public static double ReciprocalRank((int Rfc, string Section)[] ranked, int[] expectedRfcs, string[] expectedSections)
    {
        if (expectedRfcs.Length == 0 || expectedSections.Length == 0)
            return 0.0;

        var expectedPairs = BuildExpectedPairs(expectedRfcs, expectedSections);
        for (int i = 0; i < ranked.Length; i++)
        {
            if (expectedPairs.Contains(ranked[i]))
                return 1.0 / (i + 1);
        }

        return 0.0;
    }

    public static double NdcgAtK((int Rfc, string Section)[] ranked, int[] expectedRfcs, string[] expectedSections, int k)
    {
        if (expectedRfcs.Length == 0 || expectedSections.Length == 0 || ranked.Length == 0)
            return 0.0;

        var expectedPairs = BuildExpectedPairs(expectedRfcs, expectedSections);
        double dcg = 0.0;
        int limit = Math.Min(ranked.Length, k);
        for (int i = 0; i < limit; i++)
        {
            if (expectedPairs.Contains(ranked[i]))
                dcg += 1.0 / Math.Log2(i + 2);
        }

        double idcg = ComputeIdealDcg(expectedPairs.Count, k);
        return idcg <= double.Epsilon ? 0.0 : dcg / idcg;
    }

    public static RetrievalAggregateMetrics Aggregate(IReadOnlyList<RetrievalQueryResult> results)
    {
        if (results.Count == 0)
            return new RetrievalAggregateMetrics(0.0, 0.0, 0.0, 0.0, 0.0, 0L);

        double hitAt1 = results.Average(r => r.HitAt1 ? 1.0 : 0.0);
        double hitAt5 = results.Average(r => r.HitAt5 ? 1.0 : 0.0);
        double hitAt10 = results.Average(r => r.HitAt10 ? 1.0 : 0.0);
        double mrr = results.Average(r => r.ReciprocalRank);
        double ndcgAt10 = results.Average(r => r.NdcgAt10);
        long avgLatencyMs = (long)results.Average(r => r.LatencyMs);

        return new RetrievalAggregateMetrics(hitAt1, hitAt5, hitAt10, mrr, ndcgAt10, avgLatencyMs);
    }

    private static HashSet<(int Rfc, string Section)> BuildExpectedPairs(int[] expectedRfcs, string[] expectedSections)
    {
        var pairs = new HashSet<(int, string)>(capacity: expectedRfcs.Length * expectedSections.Length);
        foreach (int rfc in expectedRfcs)
        {
            foreach (string section in expectedSections)
            {
                pairs.Add((rfc, section));
            }
        }

        return pairs;
    }

    private static double ComputeDcg(int[] rankedRfcs, HashSet<int> relevant, int k)
    {
        double dcg = 0.0;
        int limit = Math.Min(rankedRfcs.Length, k);
        for (int i = 0; i < limit; i++)
        {
            if (relevant.Contains(rankedRfcs[i]))
                dcg += 1.0 / Math.Log2(i + 2);
        }

        return dcg;
    }

    private static double ComputeIdealDcg(int relevantCount, int k)
    {
        double idcg = 0.0;
        int limit = Math.Min(relevantCount, k);
        for (int i = 0; i < limit; i++)
            idcg += 1.0 / Math.Log2(i + 2);

        return idcg;
    }
}
