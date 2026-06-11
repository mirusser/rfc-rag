using RfcRag.Evaluation;

namespace RfcRag.Tests.UnitTests;

public sealed class RetrievalMetricsTests
{
    // HitAtK tests

    [Theory]
    [InlineData(new[] { 2119, 9110, 3986 }, new[] { 2119 }, 1, true)]
    [InlineData(new[] { 2119, 9110, 3986 }, new[] { 2119 }, 5, true)]
    [InlineData(new[] { 9110, 3986, 2119 }, new[] { 2119 }, 1, false)]
    [InlineData(new[] { 9110, 3986, 2119 }, new[] { 2119 }, 3, true)]
    [InlineData(new[] { 9110, 3986 }, new[] { 2119 }, 10, false)]
    [InlineData(new int[0], new[] { 2119 }, 10, false)]
    public void HitAtK_VariousRankings_ReturnsExpected(
        int[] rankedRfcs, int[] expectedRfcs, int k, bool expectedHit)
    {
        bool hit = RetrievalMetrics.HitAtK(rankedRfcs, expectedRfcs, k);

        Assert.Equal(expectedHit, hit);
    }

    [Fact]
    public void HitAtK_EmptyExpected_ReturnsFalse()
    {
        bool hit = RetrievalMetrics.HitAtK([2119, 9110], [], k: 10);

        Assert.False(hit);
    }

    [Fact]
    public void HitAtK_KLargerThanResults_StillChecksAll()
    {
        bool hit = RetrievalMetrics.HitAtK([2119], [2119], k: 100);

        Assert.True(hit);
    }

    // ReciprocalRank tests

    [Theory]
    [InlineData(new[] { 2119, 9110, 3986 }, new[] { 2119 }, 1.0)]
    [InlineData(new[] { 9110, 2119, 3986 }, new[] { 2119 }, 0.5)]
    [InlineData(new[] { 9110, 3986, 2119 }, new[] { 2119 }, 1.0 / 3)]
    [InlineData(new[] { 9110, 3986, 8446 }, new[] { 2119 }, 0.0)]
    [InlineData(new int[0], new[] { 2119 }, 0.0)]
    public void ReciprocalRank_VariousRankings_ReturnsExpected(
        int[] rankedRfcs, int[] expectedRfcs, double expectedRr)
    {
        double rr = RetrievalMetrics.ReciprocalRank(rankedRfcs, expectedRfcs);

        Assert.Equal(expectedRr, rr, precision: 10);
    }

    [Fact]
    public void ReciprocalRank_EmptyExpected_ReturnsZero()
    {
        double rr = RetrievalMetrics.ReciprocalRank([2119, 9110], []);

        Assert.Equal(0.0, rr);
    }

    // NdcgAtK tests

    [Fact]
    public void NdcgAtK_PerfectRanking_ReturnsOne()
    {
        double ndcg = RetrievalMetrics.NdcgAtK([2119, 9110, 3986], [2119], k: 10);

        Assert.Equal(1.0, ndcg, precision: 10);
    }

    [Fact]
    public void NdcgAtK_NoRelevantInResults_ReturnsZero()
    {
        double ndcg = RetrievalMetrics.NdcgAtK([8446, 9000, 3986], [2119], k: 10);

        Assert.Equal(0.0, ndcg, precision: 10);
    }

    [Fact]
    public void NdcgAtK_RelevantAtRankThree_LessThanPerfect()
    {
        double ndcgRank1 = RetrievalMetrics.NdcgAtK([2119, 9110, 3986], [2119], k: 10);
        double ndcgRank3 = RetrievalMetrics.NdcgAtK([9110, 3986, 2119], [2119], k: 10);

        Assert.True(ndcgRank3 < ndcgRank1);
        Assert.True(ndcgRank3 > 0.0);
    }

    [Fact]
    public void NdcgAtK_EmptyResults_ReturnsZero()
    {
        double ndcg = RetrievalMetrics.NdcgAtK([], [2119], k: 10);

        Assert.Equal(0.0, ndcg);
    }

    [Fact]
    public void NdcgAtK_EmptyExpected_ReturnsZero()
    {
        double ndcg = RetrievalMetrics.NdcgAtK([2119, 9110], [], k: 10);

        Assert.Equal(0.0, ndcg);
    }

    // AggregateMetrics tests

    [Fact]
    public void AggregateMetrics_AllPerfect_ReturnsOneForAll()
    {
        var results = new[]
        {
            new RetrievalQueryResult("q1", "q?", "testdata", true, true, true, 1.0, 1.0, 10, [2119], null),
            new RetrievalQueryResult("q2", "q?", "testdata", true, true, true, 1.0, 1.0, 20, [9110], null)
        };

        var agg = RetrievalMetrics.Aggregate(results);

        Assert.Equal(1.0, agg.HitAt1);
        Assert.Equal(1.0, agg.HitAt5);
        Assert.Equal(1.0, agg.HitAt10);
        Assert.Equal(1.0, agg.Mrr);
        Assert.Equal(1.0, agg.NdcgAt10);
        Assert.Equal(15L, agg.AvgLatencyMs);
    }

    [Fact]
    public void AggregateMetrics_MixedResults_ReturnsCorrectMeans()
    {
        var results = new[]
        {
            new RetrievalQueryResult("q1", "q?", "testdata", true, true, true, 1.0, 1.0, 10, [2119], null),
            new RetrievalQueryResult("q2", "q?", "testdata", false, false, false, 0.0, 0.0, 30, [9110], null)
        };

        var agg = RetrievalMetrics.Aggregate(results);

        Assert.Equal(0.5, agg.HitAt1);
        Assert.Equal(0.5, agg.HitAt5);
        Assert.Equal(0.5, agg.HitAt10);
        Assert.Equal(0.5, agg.Mrr);
        Assert.Equal(0.5, agg.NdcgAt10);
        Assert.Equal(20L, agg.AvgLatencyMs);
    }

    [Fact]
    public void AggregateMetrics_EmptyResults_ReturnsZeros()
    {
        var agg = RetrievalMetrics.Aggregate([]);

        Assert.Equal(0.0, agg.HitAt1);
        Assert.Equal(0.0, agg.HitAt5);
        Assert.Equal(0.0, agg.HitAt10);
        Assert.Equal(0.0, agg.Mrr);
        Assert.Equal(0.0, agg.NdcgAt10);
        Assert.Equal(0L, agg.AvgLatencyMs);
    }

    [Fact]
    public void HitAtK_MultipleExpectedRfcs_HitsFirstMatch()
    {
        bool hit = RetrievalMetrics.HitAtK([9110, 8446, 2119], [2119, 8446], k: 2);

        Assert.True(hit);
    }

    [Fact]
    public void ReciprocalRank_MultipleExpectedRfcs_ReturnsRankOfFirstMatch()
    {
        double rr = RetrievalMetrics.ReciprocalRank([9110, 8446, 2119], [2119, 9000]);

        Assert.Equal(1.0 / 3, rr, precision: 10);
    }

    [Fact]
    public void NdcgAtK_MultipleRelevant_ScoresHigherThanSingleRelevant()
    {
        double ndcgMulti = RetrievalMetrics.NdcgAtK([2119, 8446, 9110], [2119, 8446], k: 10);
        double ndcgSingle = RetrievalMetrics.NdcgAtK([8446, 2119, 9110], [2119], k: 10);

        Assert.True(ndcgMulti > ndcgSingle);
    }

    [Fact]
    public void NdcgAtK_MultipleRelevant_RelevantAtRanksTwoAndThree_LessThanPerfect()
    {
        double ndcg = RetrievalMetrics.NdcgAtK([9110, 2119, 8446], [2119, 8446], k: 10);

        Assert.True(ndcg < 1.0);
        Assert.True(ndcg > 0.0);
    }

    [Fact]
    public void HitAtK_SectionLevel_ExactSectionMatch_Hits()
    {
        var ranked = new[] { (Rfc: 8446, Section: "4.1.2"), (Rfc: 9110, Section: "9.3.1") };

        bool hit = RetrievalMetrics.HitAtK(ranked, [8446], ["4.1.2"], k: 2);

        Assert.True(hit);
    }

    [Fact]
    public void HitAtK_SectionLevel_WrongRfcRightSection_NoHit()
    {
        var ranked = new[] { (Rfc: 9110, Section: "4.1.2") };

        bool hit = RetrievalMetrics.HitAtK(ranked, [8446], ["4.1.2"], k: 1);

        Assert.False(hit);
    }

    [Fact]
    public void HitAtK_SectionLevel_EmptyExpectedSections_ReturnsFalse()
    {
        var ranked = new[] { (Rfc: 2119, Section: "1") };

        bool hit = RetrievalMetrics.HitAtK(ranked, [2119], [], k: 10);

        Assert.False(hit);
    }

    [Fact]
    public void ReciprocalRank_SectionLevel_ReturnsCorrectRank()
    {
        var ranked = new[] { (Rfc: 9110, Section: "9.3.1"), (Rfc: 8446, Section: "4.1.2") };

        double rr = RetrievalMetrics.ReciprocalRank(ranked, [8446], ["4.1.2"]);

        Assert.Equal(0.5, rr, precision: 10);
    }

    [Fact]
    public void NdcgAtK_SectionLevel_PerfectMatch_ReturnsOne()
    {
        var ranked = new[] { (Rfc: 8446, Section: "4.1.2"), (Rfc: 9110, Section: "9.3.1") };
        double ndcg = RetrievalMetrics.NdcgAtK(ranked, [8446], ["4.1.2"], k: 10);

        Assert.Equal(1.0, ndcg, precision: 10);
    }

    [Fact]
    public void NdcgAtK_SectionLevel_NoMatch_ReturnsZero()
    {
        var ranked = new[] { (Rfc: 9110, Section: "9.3.1") };
        double ndcg = RetrievalMetrics.NdcgAtK(ranked, [8446], ["4.1.2"], k: 10);

        Assert.Equal(0.0, ndcg, precision: 10);
    }
}
