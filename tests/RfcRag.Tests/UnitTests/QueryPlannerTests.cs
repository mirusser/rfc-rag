using RfcRag.Search;

namespace RfcRag.Tests.UnitTests;

public sealed class QueryPlannerTests
{
    [Theory]
    [InlineData("What does RFC 9110 say?", 9110)]
    [InlineData("what changed in rfc9110?", 9110)]
    public void Plan_RfcNumberMention_DetectsRfcNumber(string query, int expectedRfcNumber)
    {
        QueryPlan plan = QueryPlanner.Plan(query);

        int rfcNumber = Assert.Single(plan.RfcNumbers);
        Assert.Equal(expectedRfcNumber, rfcNumber);
    }

    [Theory]
    [InlineData("What does RFC 9110 section 9.3.1 say?", 9110, "9.3.1")]
    [InlineData("RFC9110 §9.3.1 semantics", 9110, "9.3.1")]
    public void Plan_RfcSectionReference_DetectsSectionReference(
        string query,
        int expectedRfcNumber,
        string expectedSection)
    {
        QueryPlan plan = QueryPlanner.Plan(query);

        QuerySectionReference sectionReference = Assert.Single(plan.SectionReferences);
        Assert.Equal(expectedRfcNumber, sectionReference.RfcNumber);
        Assert.Equal(expectedSection, sectionReference.Section);
    }

    [Theory]
    [InlineData("What must an HTTP cache do?", "MUST")]
    [InlineData("Which behavior is forbidden?", "MUST NOT")]
    [InlineData("What is allowed for clients?", "MAY")]
    [InlineData("What is required for compliance?", "MUST")]
    public void Plan_StrongNormativeIntent_SuggestsNormativeKeyword(
        string query,
        string expectedNormativeKeyword)
    {
        QueryPlan plan = QueryPlanner.Plan(query);

        Assert.Equal(expectedNormativeKeyword, plan.SuggestedNormativeKeyword);
    }

    [Theory]
    [InlineData("RFC terminology without a number")]
    [InlineData("What may happen if a peer closes the connection?")]
    public void Plan_AmbiguousSignals_DoesNotOverDetect(string query)
    {
        QueryPlan plan = QueryPlanner.Plan(query);

        Assert.Empty(plan.RfcNumbers);
        Assert.Null(plan.SuggestedNormativeKeyword);
    }

    [Theory]
    [InlineData("What does RFC 2119 define for the keyword MUST?")]
    [InlineData("What is the difference between SHOULD and MUST in RFC specifications?")]
    public void Plan_NormativeKeywordTopic_DoesNotSuggestNormativeKeyword(string query)
    {
        QueryPlan plan = QueryPlanner.Plan(query);

        Assert.Null(plan.SuggestedNormativeKeyword);
    }

    [Theory]
    [InlineData("How does HTTP define GET?", 9110)]
    [InlineData("Explain the TLS handshake", 8446)]
    [InlineData("How does QUIC recover lost packets?", 9002)]
    public void Plan_ProtocolHint_DetectsSeedRfc(string query, int expectedRfcNumber)
    {
        QueryPlan plan = QueryPlanner.Plan(query);

        Assert.Contains(expectedRfcNumber, plan.ProtocolRfcNumbers);
    }

    [Theory]
    [InlineData("Show the ABNF for HTTP field names")]
    [InlineData("What grammar defines URI syntax?")]
    public void Plan_AbnfOrGrammarIntent_SetsAbnfIntent(string query)
    {
        QueryPlan plan = QueryPlanner.Plan(query);

        Assert.True(plan.HasAbnfIntent);
    }

    [Theory]
    [InlineData("What did the old HTTP spec say?")]
    [InlineData("Which RFC is obsolete for TLS?")]
    [InlineData("What changed from RFC 7231 to RFC 9110?")]
    public void Plan_HistoricalIntent_IncludesObsoleteAndDisablesCurrentSpecPreference(string query)
    {
        QueryPlan plan = QueryPlanner.Plan(query);

        Assert.True(plan.IncludeObsolete);
        Assert.False(plan.NeedsCurrentSpec);
    }
}
