namespace RfcRag.Models;

/// <summary>
/// RFC relation data for a batch of RFC numbers.
/// Aggregates forward references (what this RFC updates/obsoletes) and
/// back-references (what other RFCs update/obsolete this one).
/// </summary>
public sealed record class RfcRelationsBatch
{
    /// <summary>RFC number.</summary>
    public int RfcNumber { get; init; }

    /// <summary>RFCs updated by this RFC.</summary>
    public IReadOnlyList<int> Updates { get; init; } = [];

    /// <summary>RFCs obsoleted by this RFC.</summary>
    public IReadOnlyList<int> Obsoletes { get; init; } = [];

    /// <summary>RFCs that update this RFC.</summary>
    public IReadOnlyList<int> UpdatedBy { get; init; } = [];

    /// <summary>RFCs that obsolete this RFC.</summary>
    public IReadOnlyList<int> ObsoletedBy { get; init; } = [];
}
