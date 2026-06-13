namespace RfcRag.Search;

/// <summary>
/// RFC status block surfaced on search results and evidence sections.
/// Indicates whether the RFC is current, obsoleted, or updated, and by which RFCs.
/// </summary>
public sealed record class RfcStatusBlock
{
    /// <summary>
    /// Status category: "current", "obsoleted", or "updated".
    /// "obsoleted" means the RFC has been fully superseded; "updated" means partial updates exist.
    /// </summary>
    public string Category { get; init; } = RfcStatusCategory.Current;

    /// <summary>RFC numbers that obsolete this RFC.</summary>
    public IReadOnlyList<int> ObsoletedBy { get; init; } = [];

    /// <summary>RFC numbers that update (but do not fully obsolete) this RFC.</summary>
    public IReadOnlyList<int> UpdatedBy { get; init; } = [];

    internal static RfcStatusBlock From(RfcRelationsBatch rel)
    {
        string category = rel.ObsoletedBy.Count > 0
            ? RfcStatusCategory.Obsoleted
            : rel.UpdatedBy.Count > 0
                ? RfcStatusCategory.Updated
                : RfcStatusCategory.Current;

        return new RfcStatusBlock
        {
            Category = category,
            ObsoletedBy = rel.ObsoletedBy,
            UpdatedBy = rel.UpdatedBy,
        };
    }
}

public static class RfcStatusCategory
{
    public const string Current = "current";
    public const string Obsoleted = "obsoleted";
    public const string Updated = "updated";
}
