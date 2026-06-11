using RfcRag.Answering;
using RfcRag.Evaluation;

namespace RfcRag.Tests.UnitTests;

public sealed class AnswerEvaluationEvidenceMetricsTests
{
    [Fact]
    public void ComputeQuoteFaithfulness_WithEvidencePack_RequiresVerbatimEvidenceSupport()
    {
        var pack = new EvidencePack
        {
            Query = "What does RFC 2119 say about MUST?",
            Sections =
            [
                new EvidenceSection
                {
                    EvidenceId = "2119#1",
                    RfcNumber = 2119,
                    Section = "1",
                    Text = "The key words MUST, MUST NOT, REQUIRED, SHALL, SHALL NOT, SHOULD, SHOULD NOT, RECOMMENDED, MAY, and OPTIONAL are to be interpreted as described here.",
                },
            ],
        };

        Citation[] citations =
        [
            new()
            {
                EvidenceId = "2119#1",
                RfcNumber = 2119,
                Section = "1",
                RelevantText = "MUST, MUST NOT, REQUIRED",
            },
            new()
            {
                EvidenceId = "2119#1",
                RfcNumber = 2119,
                Section = "1",
                RelevantText = "MUST means mandatory",
            },
            new()
            {
                EvidenceId = "2119#missing",
                RfcNumber = 2119,
                Section = "missing",
                RelevantText = "MUST, MUST NOT, REQUIRED",
            },
            new()
            {
                EvidenceId = "2119#1",
                RfcNumber = 2119,
                Section = "1",
                RelevantText = "",
            },
        ];

        double result = AnswerEvaluationMetrics.ComputeQuoteFaithfulness(citations, pack);

        Assert.Equal(0.25, result, precision: 10);
    }

    [Fact]
    public void Evaluate_WithEvidencePack_UsesEvidenceBackedQuoteFaithfulness()
    {
        var question = new GoldenQuestion(
            "q1",
            "What does RFC 2119 say about MUST?",
            ExpectedRfcs: [2119],
            ExpectedSections: ["1"],
            MustCite: [2119],
            ShouldNotCite: [],
            AnswerType: "normative_explanation",
            Corpus: "testdata");
        var pack = new EvidencePack
        {
            Query = question.Question,
            Sections =
            [
                new EvidenceSection
                {
                    EvidenceId = "2119#1",
                    RfcNumber = 2119,
                    Section = "1",
                    Text = "The key words MUST and SHOULD are to be interpreted as described here.",
                },
            ],
        };
        var answer = new GeneratedAnswer
        {
            Answer = "RFC 2119 defines MUST. [2119#1]",
            Citations =
            [
                new Citation
                {
                    EvidenceId = "2119#1",
                    RfcNumber = 2119,
                    Section = "1",
                    RelevantText = "MUST and SHOULD",
                },
                new Citation
                {
                    EvidenceId = "2119#1",
                    RfcNumber = 2119,
                    Section = "1",
                    RelevantText = "MUST means absolutely required",
                },
            ],
        };

        AnswerEvaluationResult result = AnswerEvaluationMetrics.Evaluate(
            question,
            answer,
            latencyMs: 10,
            obsoleteRfcs: [],
            evidencePack: pack);

        Assert.Equal(0.5, result.QuoteFaithfulness, precision: 10);
    }
}
