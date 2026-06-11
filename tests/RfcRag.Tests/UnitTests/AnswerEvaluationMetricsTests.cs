using RfcRag.Answering;
using RfcRag.Evaluation;

namespace RfcRag.Tests.UnitTests;

public sealed class AnswerEvaluationMetricsTests
{
    // CitationPrecision tests

    [Theory]
    [InlineData(new[] { 2119, 9110 }, new[] { 2119 }, 0.5)]
    [InlineData(new[] { 2119, 9110 }, new[] { 2119, 9110 }, 1.0)]
    [InlineData(new[] { 2119 }, new[] { 2119 }, 1.0)]
    [InlineData(new[] { 8446 }, new[] { 2119 }, 0.0)]
    [InlineData(new int[0], new[] { 2119 }, 0.0)]
    [InlineData(new[] { 2119, 9110 }, new int[0], 1.0)]
    [InlineData(new[] { 2119, 8446, 9110 }, new[] { 2119, 9110 }, 2.0 / 3)]
    public void CitationPrecision_VariousSets_ReturnsExpected(int[] citedRfcs, int[] mustCite, double expected)
    {
        double precision = AnswerEvaluationMetrics.CitationPrecision(citedRfcs, mustCite);

        Assert.Equal(expected, precision, precision: 10);
    }

    // CitationRecall tests

    [Theory]
    [InlineData(new[] { 2119 }, new[] { 2119 }, 1.0)]
    [InlineData(new[] { 2119, 9110 }, new[] { 2119 }, 1.0)]
    [InlineData(new[] { 8446 }, new[] { 2119 }, 0.0)]
    [InlineData(new[] { 2119, 8446 }, new[] { 2119, 9110 }, 0.5)]
    [InlineData(new int[0], new[] { 2119 }, 0.0)]
    [InlineData(new[] { 2119 }, new int[0], 1.0)]
    [InlineData(new[] { 2119, 9110, 8446 }, new[] { 2119, 8446, 9000 }, 2.0 / 3)]
    public void CitationRecall_VariousSets_ReturnsExpected(int[] citedRfcs, int[] mustCite, double expected)
    {
        double recall = AnswerEvaluationMetrics.CitationRecall(citedRfcs, mustCite);

        Assert.Equal(expected, recall, precision: 10);
    }

    // CitationF1 tests

    [Theory]
    [InlineData(1.0, 1.0, 1.0)]
    [InlineData(1.0, 0.5, 2.0 / 3)]
    [InlineData(0.5, 1.0, 2.0 / 3)]
    [InlineData(0.0, 1.0, 0.0)]
    [InlineData(1.0, 0.0, 0.0)]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(0.8, 0.6, 0.6857142857142857)] // 2*0.8*0.6 / (0.8+0.6) = 0.96/1.4
    public void CitationF1_VariousPrecisionRecall_ReturnsExpected(double precision, double recall, double expected)
    {
        double f1 = AnswerEvaluationMetrics.CitationF1(precision, recall);

        Assert.Equal(expected, f1, precision: 10);
    }

    // GetForbiddenCitedRfcs tests

    [Fact]
    public void GetForbiddenCitedRfcs_NoForbidden_ReturnsEmpty()
    {
        int[] forbidden = AnswerEvaluationMetrics.GetForbiddenCitedRfcs(
            [2119, 9110], [7231]);

        Assert.Empty(forbidden);
    }

    [Fact]
    public void GetForbiddenCitedRfcs_SomeForbidden_ReturnsMatches()
    {
        int[] forbidden = AnswerEvaluationMetrics.GetForbiddenCitedRfcs(
            [2119, 7231, 9110], [7231]);

        Assert.Equal([7231], forbidden);
    }

    [Fact]
    public void GetForbiddenCitedRfcs_AllForbidden_ReturnsAll()
    {
        int[] forbidden = AnswerEvaluationMetrics.GetForbiddenCitedRfcs(
            [7231, 7230], [7231, 7230]);

        Assert.Equal([7231, 7230], forbidden);
    }

    [Fact]
    public void GetForbiddenCitedRfcs_EmptyCiteSet_ReturnsEmpty()
    {
        int[] forbidden = AnswerEvaluationMetrics.GetForbiddenCitedRfcs([], [7231]);

        Assert.Empty(forbidden);
    }

    [Fact]
    public void GetForbiddenCitedRfcs_EmptyForbiddenSet_ReturnsEmpty()
    {
        int[] forbidden = AnswerEvaluationMetrics.GetForbiddenCitedRfcs([2119], []);

        Assert.Empty(forbidden);
    }

    // CorrectlyDeclined tests

    [Theory]
    [InlineData(true, "no_answer", true)]
    [InlineData(false, "no_answer", false)]
    [InlineData(true, "factual", null)]
    [InlineData(false, "factual", null)]
    [InlineData(true, "normative_explanation", null)]
    public void CorrectlyDeclined_ReturnsExpected(bool noAnswer, string answerType, bool? expected)
    {
        bool? result = AnswerEvaluationMetrics.CorrectlyDeclined(noAnswer, answerType);

        Assert.Equal(expected, result);
    }

    // ComputeQuoteFaithfulness tests

    [Fact]
    public void ComputeQuoteFaithfulness_AllHaveRelevantText_ReturnsOne()
    {
        var citations = new[]
        {
            new Citation { EvidenceId = "2119#1", RfcNumber = 2119, Section = "1", RelevantText = "MUST" },
            new Citation { EvidenceId = "9110#1", RfcNumber = 9110, Section = "1", RelevantText = "SHOULD" },
        };

        double result = AnswerEvaluationMetrics.ComputeQuoteFaithfulness(citations);

        Assert.Equal(1.0, result);
    }

    [Fact]
    public void ComputeQuoteFaithfulness_NoneHaveRelevantText_ReturnsZero()
    {
        var citations = new[]
        {
            new Citation { EvidenceId = "2119#1", RfcNumber = 2119, Section = "1" },
            new Citation { EvidenceId = "9110#1", RfcNumber = 9110, Section = "1" },
        };

        double result = AnswerEvaluationMetrics.ComputeQuoteFaithfulness(citations);

        Assert.Equal(0.0, result);
    }

    [Fact]
    public void ComputeQuoteFaithfulness_Mixed_ReturnsCorrectFraction()
    {
        var citations = new[]
        {
            new Citation { EvidenceId = "2119#1", RfcNumber = 2119, Section = "1", RelevantText = "MUST" },
            new Citation { EvidenceId = "7231#1", RfcNumber = 7231, Section = "1" },
            new Citation { EvidenceId = "8446#1", RfcNumber = 8446, Section = "1", RelevantText = "SHOULD" },
        };

        double result = AnswerEvaluationMetrics.ComputeQuoteFaithfulness(citations);

        Assert.Equal(2.0 / 3, result, precision: 10);
    }

    [Fact]
    public void ComputeQuoteFaithfulness_EmptyCitations_ReturnsZero()
    {
        double result = AnswerEvaluationMetrics.ComputeQuoteFaithfulness([]);

        Assert.Equal(0.0, result);
    }

    // ComputeObsoleteCitationRate tests

    [Theory]
    [InlineData(new[] { 7231 }, new[] { 7231 }, 1.0)]
    [InlineData(new[] { 7231, 7230 }, new[] { 7231 }, 0.5)]
    [InlineData(new[] { 2119 }, new[] { 7231 }, 0.0)]
    [InlineData(new int[0], new[] { 7231 }, 0.0)]
    [InlineData(new[] { 2119 }, new int[0], 0.0)]
    [InlineData(new[] { 7231, 7230, 2119 }, new[] { 7231, 7230 }, 2.0 / 3)]
    public void ComputeObsoleteCitationRate_VariousSets_ReturnsExpected(int[] citedRfcs, int[] obsoleteRfcs, double expected)
    {
        double result = AnswerEvaluationMetrics.ComputeObsoleteCitationRate(citedRfcs, obsoleteRfcs);

        Assert.Equal(expected, result, precision: 10);
    }

    // Evaluate — new field verification

    [Fact]
    public void Evaluate_SetsQuoteFaithfulness()
    {
        var question = new GoldenQuestion("q1", "test", [2119], [], [2119], [], "factual", "testdata");
        var answer = new GeneratedAnswer
        {
            Answer = "RFC 2119 defines MUST.",
            Citations =
            [
                new Citation { EvidenceId = "2119#1", RfcNumber = 2119, Section = "1", RelevantText = "MUST" },
            ],
        };

        var result = AnswerEvaluationMetrics.Evaluate(question, answer, latencyMs: 100);

        Assert.Equal(1.0, result.QuoteFaithfulness, precision: 10);
    }

    [Fact]
    public void Evaluate_WithObsoleteRfcs_SetsObsoleteCitationRate()
    {
        var question = new GoldenQuestion("q1", "test", [7231, 2119], [], [7231, 2119], [], "factual", "testdata");
        var answer = new GeneratedAnswer
        {
            Answer = "RFC 7231 and 2119 are relevant.",
            Citations =
            [
                new Citation { EvidenceId = "7231#1", RfcNumber = 7231, Section = "1" },
                new Citation { EvidenceId = "2119#1", RfcNumber = 2119, Section = "1" },
            ],
        };

        var result = AnswerEvaluationMetrics.Evaluate(question, answer, latencyMs: 100, obsoleteRfcs: [7231]);

        Assert.Equal(0.5, result.ObsoleteCitationRate, precision: 10);
    }

    [Fact]
    public void Evaluate_WithoutObsoleteRfcs_ObsoleteCitationRateIsZero()
    {
        var question = new GoldenQuestion("q1", "test", [7231], [], [7231], [], "factual", "testdata");
        var answer = new GeneratedAnswer
        {
            Answer = "RFC 7231 is relevant.",
            Citations =
            [
                new Citation { EvidenceId = "7231#1", RfcNumber = 7231, Section = "1" },
            ],
        };

        var result = AnswerEvaluationMetrics.Evaluate(question, answer, latencyMs: 100);

        Assert.Equal(0.0, result.ObsoleteCitationRate, precision: 10);
    }

    // Evaluate (full integration) tests

    [Fact]
    public void Evaluate_PerfectAnswer_ReturnsPerfectScores()
    {
        var question = new GoldenQuestion("q1", "test", [2119], [], [2119], [], "factual", "testdata");
        var answer = new GeneratedAnswer
        {
            Answer = "RFC 2119 defines MUST.",
            Citations =
            [
                new Citation { EvidenceId = "2119#1", RfcNumber = 2119, Section = "1" },
            ],
        };

        var result = AnswerEvaluationMetrics.Evaluate(question, answer, latencyMs: 100);

        Assert.Equal(1.0, result.CitationPrecision, precision: 10);
        Assert.Equal(1.0, result.CitationRecall, precision: 10);
        Assert.Equal(1.0, result.CitationF1, precision: 10);
        Assert.False(result.HasForbiddenCitations);
        Assert.Null(result.CorrectNoAnswer);
        Assert.Equal("factual", result.ExpectedAnswerType);
    }

    [Fact]
    public void Evaluate_NoCitations_ReturnsZeroRecall()
    {
        var question = new GoldenQuestion("q1", "test", [2119], [], [2119], [], "factual", "testdata");
        var answer = new GeneratedAnswer
        {
            Answer = "I don't know.",
            Citations = [],
        };

        var result = AnswerEvaluationMetrics.Evaluate(question, answer, latencyMs: 50);

        Assert.Equal(0.0, result.CitationPrecision, precision: 10);
        Assert.Equal(0.0, result.CitationRecall, precision: 10);
        Assert.Equal(0.0, result.CitationF1, precision: 10);
        Assert.Empty(result.CitedRfcs);
    }

    [Fact]
    public void Evaluate_ForbiddenCitation_DetectsHallucination()
    {
        var question = new GoldenQuestion("q1", "test", [2119], [], [2119], [7231], "factual", "testdata");
        var answer = new GeneratedAnswer
        {
            Answer = "RFC 7231 is relevant.",
            Citations =
            [
                new Citation { EvidenceId = "7231#1", RfcNumber = 7231, Section = "1" },
                new Citation { EvidenceId = "2119#1", RfcNumber = 2119, Section = "1" },
            ],
        };

        var result = AnswerEvaluationMetrics.Evaluate(question, answer, latencyMs: 75);

        Assert.True(result.HasForbiddenCitations);
        Assert.Equal([7231], result.ForbiddenCitedRfcs);
        Assert.Equal(0.5, result.CitationPrecision, precision: 10); // 1/2 correct
        Assert.Equal(1.0, result.CitationRecall, precision: 10);    // 1/1 must-cite found
    }

    [Fact]
    public void Evaluate_NoAnswerType_CorrectlyDeclined()
    {
        var question = new GoldenQuestion("q1", "test", [], [], [], [], "no_answer", "testdata");
        var answer = new GeneratedAnswer
        {
            Answer = "I could not find support for answering this question.",
            Citations = [],
            NoAnswer = true,
        };

        var result = AnswerEvaluationMetrics.Evaluate(question, answer, latencyMs: 30);

        Assert.True(result.CorrectNoAnswer);
        Assert.Equal("no_answer", result.ExpectedAnswerType);
        Assert.Equal(0.0, result.CitationPrecision, precision: 10);
    }

    [Fact]
    public void Evaluate_NoAnswerType_FailedToDecline_ReturnsFalse()
    {
        var question = new GoldenQuestion("q1", "test", [], [], [], [], "no_answer", "testdata");
        var answer = new GeneratedAnswer
        {
            Answer = "I think the answer is 42.",
            Citations =
            [
                new Citation { EvidenceId = "2119#1", RfcNumber = 2119, Section = "1" },
            ],
            NoAnswer = false,
        };

        var result = AnswerEvaluationMetrics.Evaluate(question, answer, latencyMs: 40);

        Assert.False(result.CorrectNoAnswer);
        Assert.False(result.HasForbiddenCitations);
    }

    // Aggregate tests

    [Fact]
    public void Aggregate_PerfectResults_ReturnsOne()
    {
        var results = new[]
        {
            new AnswerEvaluationResult("q1", "?", "testdata", [2119], [2119], [], 1.0, 1.0, 1.0, false, null, "factual", 10, null),
            new AnswerEvaluationResult("q2", "?", "testdata", [9110], [9110], [], 1.0, 1.0, 1.0, false, null, "factual", 20, null),
        };

        var agg = AnswerEvaluationMetrics.Aggregate(results);

        Assert.Equal(1.0, agg.AvgCitationPrecision);
        Assert.Equal(1.0, agg.AvgCitationRecall);
        Assert.Equal(1.0, agg.AvgCitationF1);
        Assert.Equal(0.0, agg.HallucinationRate);
        Assert.Equal(2, agg.TotalQuestions);
        Assert.Equal(2, agg.QuestionsWithCitations);
    }

    [Fact]
    public void Aggregate_MixedResults_ReturnsCorrectMeans()
    {
        var results = new[]
        {
            new AnswerEvaluationResult("q1", "?", "testdata", [2119], [2119], [], 1.0, 1.0, 1.0, false, null, "factual", 10, null),
            new AnswerEvaluationResult("q2", "?", "testdata", [7231], [2119], [7231], 0.0, 0.0, 0.0, true, null, "factual", 20, null),
        };

        var agg = AnswerEvaluationMetrics.Aggregate(results);

        Assert.Equal(0.5, agg.AvgCitationPrecision);
        Assert.Equal(0.5, agg.AvgCitationRecall);
        Assert.Equal(0.5, agg.AvgCitationF1);
        Assert.Equal(0.5, agg.HallucinationRate);
    }

    [Fact]
    public void Aggregate_EmptyResults_ReturnsDefaults()
    {
        var agg = AnswerEvaluationMetrics.Aggregate([]);

        Assert.Equal(0.0, agg.AvgCitationPrecision);
        Assert.Equal(0.0, agg.AvgCitationRecall);
        Assert.Equal(0.0, agg.AvgCitationF1);
        Assert.Null(agg.NoAnswerAccuracy);
        Assert.Equal(0.0, agg.HallucinationRate);
        Assert.Equal(0, agg.TotalQuestions);
    }

    [Fact]
    public void Aggregate_NoAnswerQuestions_ComputesAccuracy()
    {
        var results = new[]
        {
            new AnswerEvaluationResult("q1", "?", "testdata", [], [], [], 0.0, 0.0, 0.0, false, true, "no_answer", 10, null),
            new AnswerEvaluationResult("q2", "?", "testdata", [], [], [], 0.0, 0.0, 0.0, false, false, "no_answer", 20, null),
            new AnswerEvaluationResult("q3", "?", "testdata", [2119], [2119], [], 1.0, 1.0, 1.0, false, null, "factual", 30, null),
        };

        var agg = AnswerEvaluationMetrics.Aggregate(results);

        Assert.NotNull(agg.NoAnswerAccuracy);
        Assert.Equal(0.5, agg.NoAnswerAccuracy!.Value, precision: 10);
    }
}
