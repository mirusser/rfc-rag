using RfcRag.Search;

namespace RfcRag.Tests.UnitTests;

public sealed class VectorDataSearchTests
{
    [Fact]
    public void NormalizeScore_ZeroDistance_ReturnsOne()
    {
        double score = VectorDataSearch.NormalizeScore(0.0);

        Assert.Equal(1.0, score, precision: 10);
    }

    [Theory]
    [InlineData(1.0, 0.5)]
    [InlineData(3.0, 0.25)]
    [InlineData(0.5, 1.0 / 1.5)]
    public void NormalizeScore_KnownDistance_ReturnsOneOverOnePlusDistance(double distance, double expected)
    {
        double score = VectorDataSearch.NormalizeScore(distance);

        Assert.Equal(expected, score, precision: 10);
    }

    [Fact]
    public void NormalizeScore_HighDistance_ScoreIsPositive()
    {
        double score = VectorDataSearch.NormalizeScore(2.0);

        Assert.InRange(score, 0.0, 1.0);
    }
}
