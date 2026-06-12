using RfcRag.Models;
using RfcRag.Search;

namespace RfcRag.Tests.UnitTests;

public sealed class DeterministicRerankerTests
{
    private static HybridCandidate Candidate(
        int rfcNumber,
        string section,
        double rrfScore,
        string? heading = null,
        Guid? id = null) =>
        new(
            id ?? Guid.NewGuid(),
            rfcNumber,
            $"RFC {rfcNumber}",
            section,
            heading,
            $"Excerpt from {rfcNumber}/{section}",
            $"rfc{rfcNumber}.txt",
            $"https://www.rfc-editor.org/rfc/rfc{rfcNumber}",
            LexicalRank: 1,
            VectorRank: 1,
            RrfScore: rrfScore);

    private static QueryPlan EmptyPlan() =>
        new([], [], [], null, false, false, true, []);

    private static QueryPlan PlanWithRfcNumbers(params int[] rfcNumbers) =>
        new(rfcNumbers, [], [], null, false, false, true, []);

    private static QueryPlan PlanWithSectionRef(int rfcNumber, string section) =>
        new([], [new QuerySectionReference(rfcNumber, section)], [], null, false, false, true, []);

    private static QueryPlan PlanWithProtocolRfcs(params int[] protocolRfcs) =>
        new([], [], protocolRfcs, null, false, false, true, []);

    private static QueryPlan HistoricalPlan(params int[] rfcNumbers) =>
        new(rfcNumbers, [], [], null, false, IncludeObsolete: true, NeedsCurrentSpec: false, []);

    private static IReadOnlyDictionary<int, RfcRelationsBatch> NoStatuses() =>
        new Dictionary<int, RfcRelationsBatch>();

    private static IReadOnlyDictionary<int, RfcRelationsBatch> ObsoletedStatus(int rfcNumber, int obsoletedByRfc) =>
        new Dictionary<int, RfcRelationsBatch>
        {
            [rfcNumber] = new RfcRelationsBatch
            {
                RfcNumber = rfcNumber,
                ObsoletedBy = [obsoletedByRfc]
            }
        };

    private static IReadOnlyDictionary<int, RfcRelationsBatch> ObsoletesStatus(int rfcNumber, int obsoletesRfc) =>
        new Dictionary<int, RfcRelationsBatch>
        {
            [rfcNumber] = new RfcRelationsBatch
            {
                RfcNumber = rfcNumber,
                Obsoletes = [obsoletesRfc]
            }
        };

    [Fact]
    public void Rerank_EmptyCandidates_ReturnsEmpty()
    {
        IReadOnlyList<SearchResult> results = DeterministicReranker.Rerank(
            "test query", [], null, NoStatuses(), limit: 10);

        Assert.Empty(results);
    }

    [Fact]
    public void Rerank_NullQuery_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DeterministicReranker.Rerank(null!, [], null, NoStatuses(), limit: 10));
    }

    [Fact]
    public void Rerank_WhitespaceQuery_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            DeterministicReranker.Rerank("   ", [], null, NoStatuses(), limit: 10));
    }

    [Fact]
    public void Rerank_NullStatuses_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DeterministicReranker.Rerank("test query", [], null, null!, limit: 10));
    }

    [Fact]
    public void Rerank_ZeroLimit_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeterministicReranker.Rerank("test query", [], null, NoStatuses(), limit: 0));
    }

    [Fact]
    public void Rerank_NoPlanNoStatuses_OrdersByRrfScoreDescending()
    {
        var candidates = new[]
        {
            Candidate(9110, "5.1", rrfScore: 0.03),
            Candidate(9110, "1.1", rrfScore: 0.05),
            Candidate(9110, "3.1", rrfScore: 0.01),
        };

        IReadOnlyList<SearchResult> results = DeterministicReranker.Rerank(
            "test query", candidates, null, NoStatuses(), limit: 10);

        Assert.Equal(["1.1", "5.1", "3.1"], results.Select(r => r.Section).ToArray());
    }

    [Fact]
    public void Rerank_LimitApplied_ReturnsOnlyRequestedCount()
    {
        var candidates = Enumerable.Range(1, 10)
            .Select(i => Candidate(9110, $"{i}", rrfScore: 0.10 - i * 0.01))
            .ToArray();

        IReadOnlyList<SearchResult> results = DeterministicReranker.Rerank(
            "test query", candidates, EmptyPlan(), NoStatuses(), limit: 3);

        Assert.Equal(3, results.Count);
    }

    [Theory]
    [InlineData(9110, true)]
    [InlineData(3986, false)]
    public void Rerank_RfcNumberInPlan_AppliesOrNotAppliesBonus(int candidateRfc, bool expectBonus)
    {
        var baseline = Candidate(candidateRfc, "1", rrfScore: 0.05);
        var other = Candidate(9999, "1", rrfScore: 0.05);
        var candidates = new[] { baseline, other };

        QueryPlan plan = PlanWithRfcNumbers(9110);
        IReadOnlyList<SearchResult> results = DeterministicReranker.Rerank(
            "RFC 9110 request", candidates, plan, NoStatuses(), limit: 2);

        if (expectBonus)
        {
            // RFC 9110 result should rank first (got bonus, scores equal before bonus)
            Assert.Equal(candidateRfc, results[0].RfcNumber);
            Assert.True(results[0].Score > results[1].Score);
        }
        else
        {
            // No bonus; both at 0.05 — tied, ordered by rfc number then section
            Assert.Equal(results[0].Score, results[1].Score, precision: 6);
        }
    }

    [Theory]
    [InlineData("9.3.1", true)]
    [InlineData("5.2", false)]
    public void Rerank_SectionReferenceInPlan_AppliesOrNotAppliesSectionBonus(string candidateSection, bool expectBonus)
    {
        var candidate = Candidate(9110, candidateSection, rrfScore: 0.05);
        var other = Candidate(9110, "1.1", rrfScore: 0.05);
        var candidates = new[] { candidate, other };

        QueryPlan plan = PlanWithSectionRef(9110, "9.3.1");
        IReadOnlyList<SearchResult> results = DeterministicReranker.Rerank(
            "RFC 9110 section 9.3.1", candidates, plan, NoStatuses(), limit: 2);

        if (expectBonus)
        {
            Assert.Equal(candidateSection, results[0].Section);
            Assert.True(results[0].Score > results[1].Score);
        }
        else
        {
            Assert.Equal(results[0].Score, results[1].Score, precision: 6);
        }
    }

    [Theory]
    [InlineData(9110, true)]
    [InlineData(9999, false)]
    public void Rerank_ProtocolRfcInPlan_AppliesOrNotAppliesProtocolBonus(int candidateRfc, bool expectBonus)
    {
        var candidate = Candidate(candidateRfc, "1", rrfScore: 0.05);
        var other = Candidate(8888, "1", rrfScore: 0.05);
        var candidates = new[] { candidate, other };

        QueryPlan plan = PlanWithProtocolRfcs(9110);
        IReadOnlyList<SearchResult> results = DeterministicReranker.Rerank(
            "HTTP request headers", candidates, plan, NoStatuses(), limit: 2);

        if (expectBonus)
        {
            Assert.Equal(candidateRfc, results[0].RfcNumber);
            Assert.True(results[0].Score > results[1].Score);
        }
        else
        {
            Assert.Equal(results[0].Score, results[1].Score, precision: 6);
        }
    }

    [Theory]
    [InlineData("GET request method", "GET", true)]
    [InlineData("GET request method", "POST", false)]
    [InlineData("request method", null, false)]
    public void Rerank_QueryTermInHeading_AppliesOrNotAppliesHeadingBonus(string query, string? heading, bool expectBonus)
    {
        var withHeading = Candidate(9110, "1", rrfScore: 0.05, heading: heading);
        var other = Candidate(9110, "2", rrfScore: 0.05, heading: "unrelated");
        var candidates = new[] { withHeading, other };

        IReadOnlyList<SearchResult> results = DeterministicReranker.Rerank(
            query, candidates, null, NoStatuses(), limit: 2);

        if (expectBonus)
        {
            Assert.Equal("1", results[0].Section);
            Assert.True(results[0].Score > results[1].Score);
        }
        else
        {
            Assert.Equal(results[0].Score, results[1].Score, precision: 6);
        }
    }

    [Fact]
    public void Rerank_ObsoletedRfc_AppliesObsoletePenalty()
    {
        var obsoleted = Candidate(7231, "1", rrfScore: 0.06);
        var current = Candidate(9110, "1", rrfScore: 0.05);
        var candidates = new[] { obsoleted, current };

        var statuses = ObsoletedStatus(rfcNumber: 7231, obsoletedByRfc: 9110);
        IReadOnlyList<SearchResult> results = DeterministicReranker.Rerank(
            "HTTP request", candidates, EmptyPlan(), statuses, limit: 2);

        // Obsoleted RFC gets penalised; current should rank first despite lower base RRF
        Assert.Equal(9110, results[0].RfcNumber);
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public void Rerank_IncludeObsolete_SuppressesObsoletePenalty()
    {
        var obsoleted = Candidate(7231, "1", rrfScore: 0.06);
        var current = Candidate(9110, "1", rrfScore: 0.05);
        var candidates = new[] { obsoleted, current };

        var statuses = ObsoletedStatus(rfcNumber: 7231, obsoletedByRfc: 9110);
        QueryPlan plan = HistoricalPlan();
        IReadOnlyList<SearchResult> results = DeterministicReranker.Rerank(
            "old HTTP request obsolete", candidates, plan, statuses, limit: 2);

        // Penalty suppressed; base RRF score determines order → obsoleted stays first
        Assert.Equal(7231, results[0].RfcNumber);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rerank_SuccessorObsoletesQueryRfc_AppliesOrNotAppliesUpdatedByRelevanceBonus(bool obsoletesQueryRfc)
    {
        var candidate = Candidate(9110, "1", rrfScore: 0.05);
        var other = Candidate(9999, "1", rrfScore: 0.05);
        var candidates = new[] { candidate, other };

        // RFC 9110 obsoletes RFC 7231 when obsoletesQueryRfc=true, otherwise no relation
        var statuses = obsoletesQueryRfc
            ? ObsoletesStatus(rfcNumber: 9110, obsoletesRfc: 7231)
            : NoStatuses();

        QueryPlan plan = PlanWithRfcNumbers(7231);
        IReadOnlyList<SearchResult> results = DeterministicReranker.Rerank(
            "RFC 7231 request", candidates, plan, statuses, limit: 2);

        if (obsoletesQueryRfc)
        {
            Assert.Equal(9110, results[0].RfcNumber);
            Assert.True(results[0].Score > results[1].Score);
        }
        else
        {
            Assert.Equal(results[0].Score, results[1].Score, precision: 6);
        }
    }

    [Fact]
    public void Rerank_CombinedSignals_ProducesCorrectOrdering()
    {
        // candidate A: exact RFC match + section match → highest
        var a = Candidate(9110, "9.3.1", rrfScore: 0.03);
        // candidate B: only RFC match → middle
        var b = Candidate(9110, "5.1", rrfScore: 0.03);
        // candidate C: no signals but highest base → lowest after signals
        var c = Candidate(9999, "1", rrfScore: 0.08);
        var candidates = new[] { c, b, a };

        var plan = new QueryPlan(
            RfcNumbers: [9110],
            SectionReferences: [new QuerySectionReference(9110, "9.3.1")],
            ProtocolRfcNumbers: [],
            SuggestedNormativeKeyword: null,
            HasAbnfIntent: false,
            IncludeObsolete: false,
            NeedsCurrentSpec: true,
            Rationale: []);

        IReadOnlyList<SearchResult> results = DeterministicReranker.Rerank(
            "RFC 9110 section 9.3.1 GET method", candidates, plan, NoStatuses(), limit: 3);

        Assert.Equal(["9.3.1", "5.1", "1"], results.Select(r => r.Section).ToArray());
        Assert.Equal([9110, 9110, 9999], results.Select(r => r.RfcNumber).ToArray());
    }
}
