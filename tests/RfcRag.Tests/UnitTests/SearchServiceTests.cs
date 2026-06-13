using RfcRag.Models;
using RfcRag.Search;

namespace RfcRag.Tests.UnitTests;

public sealed class SearchServiceTests
{
    private static SearchResult MakeResult(int rfcNumber, string section = "1")
    {
        return new SearchResult(
            Guid.NewGuid(),
            rfcNumber,
            $"RFC {rfcNumber}",
            section,
            null,
            "excerpt",
            $"/rfc{rfcNumber}.txt",
            $"https://www.rfc-editor.org/rfc/rfc{rfcNumber}",
            0.9);
    }

    [Fact]
    public void EnrichWithStatus_EmptyStatuses_ReturnsSameResults()
    {
        IReadOnlyList<SearchResult> results = [MakeResult(9110), MakeResult(7230)];
        IReadOnlyDictionary<int, RfcRelationsBatch> statuses = new Dictionary<int, RfcRelationsBatch>();

        var enriched = SearchService.EnrichWithStatus(results, statuses);

        Assert.Same(results, enriched);
        Assert.All(enriched, r => Assert.Null(r.Status));
    }

    [Fact]
    public void EnrichWithStatus_ResultWithObsoletedRfc_SetsObsoletedStatus()
    {
        var result = MakeResult(2616);
        IReadOnlyDictionary<int, RfcRelationsBatch> statuses = new Dictionary<int, RfcRelationsBatch>
        {
            [2616] = new() { ObsoletedBy = [7230], UpdatedBy = [] },
        };

        var enriched = SearchService.EnrichWithStatus([result], statuses);

        Assert.Single(enriched);
        Assert.NotNull(enriched[0].Status);
        Assert.Equal(RfcStatusCategory.Obsoleted, enriched[0].Status!.Category);
        Assert.Equal([7230], enriched[0].Status!.ObsoletedBy);
    }

    [Fact]
    public void EnrichWithStatus_ResultWithUpdatedRfc_SetsUpdatedStatus()
    {
        var result = MakeResult(9110);
        IReadOnlyDictionary<int, RfcRelationsBatch> statuses = new Dictionary<int, RfcRelationsBatch>
        {
            [9110] = new() { ObsoletedBy = [], UpdatedBy = [9111] },
        };

        var enriched = SearchService.EnrichWithStatus([result], statuses);

        Assert.Equal(RfcStatusCategory.Updated, enriched[0].Status!.Category);
        Assert.Equal([9111], enriched[0].Status!.UpdatedBy);
    }

    [Fact]
    public void EnrichWithStatus_ResultNotInStatuses_LeavesStatusNull()
    {
        var result = MakeResult(9110);
        IReadOnlyDictionary<int, RfcRelationsBatch> statuses = new Dictionary<int, RfcRelationsBatch>
        {
            [7230] = new() { ObsoletedBy = [9110], UpdatedBy = [] },
        };

        var enriched = SearchService.EnrichWithStatus([result], statuses);

        Assert.Null(enriched[0].Status);
    }

    [Fact]
    public void EnrichWithStatus_MixedResults_EnrichesOnlyMatchingRfcs()
    {
        IReadOnlyList<SearchResult> results = [MakeResult(2616), MakeResult(9110)];
        IReadOnlyDictionary<int, RfcRelationsBatch> statuses = new Dictionary<int, RfcRelationsBatch>
        {
            [2616] = new() { ObsoletedBy = [7230], UpdatedBy = [] },
        };

        var enriched = SearchService.EnrichWithStatus(results, statuses);

        Assert.Equal(RfcStatusCategory.Obsoleted, enriched.First(r => r.RfcNumber == 2616).Status!.Category);
        Assert.Null(enriched.First(r => r.RfcNumber == 9110).Status);
    }
}
