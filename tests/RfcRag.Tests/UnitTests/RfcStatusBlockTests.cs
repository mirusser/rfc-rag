using RfcRag.Models;
using RfcRag.Search;

namespace RfcRag.Tests.UnitTests;

public sealed class RfcStatusBlockTests
{
    [Fact]
    public void From_WithObsoletedBy_ReturnsCategoryObsoleted()
    {
        var rel = new RfcRelationsBatch { ObsoletedBy = [9112, 9113], UpdatedBy = [] };

        var result = RfcStatusBlock.From(rel);

        Assert.Equal(RfcStatusCategory.Obsoleted, result.Category);
        Assert.Equal([9112, 9113], result.ObsoletedBy);
        Assert.Empty(result.UpdatedBy);
    }

    [Fact]
    public void From_WithUpdatedByAndNoObsoletedBy_ReturnsCategoryUpdated()
    {
        var rel = new RfcRelationsBatch { ObsoletedBy = [], UpdatedBy = [7230] };

        var result = RfcStatusBlock.From(rel);

        Assert.Equal(RfcStatusCategory.Updated, result.Category);
        Assert.Empty(result.ObsoletedBy);
        Assert.Equal([7230], result.UpdatedBy);
    }

    [Fact]
    public void From_WithNoRelations_ReturnsCategoryCurrent()
    {
        var rel = new RfcRelationsBatch { ObsoletedBy = [], UpdatedBy = [] };

        var result = RfcStatusBlock.From(rel);

        Assert.Equal(RfcStatusCategory.Current, result.Category);
        Assert.Empty(result.ObsoletedBy);
        Assert.Empty(result.UpdatedBy);
    }

    [Fact]
    public void From_ObsoletedByTakesPrecedenceOverUpdatedBy()
    {
        var rel = new RfcRelationsBatch { ObsoletedBy = [9112], UpdatedBy = [7230] };

        var result = RfcStatusBlock.From(rel);

        Assert.Equal(RfcStatusCategory.Obsoleted, result.Category);
    }
}
