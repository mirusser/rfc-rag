namespace RfcRag.Models;

/// <summary>
/// Lightweight normative occurrence used during evidence enrichment.
/// Unlike the full NormativeOccurrence entity, this carries only the fields relevant to evidence display.
/// </summary>
public sealed record class NormativeOccurrenceData
{
    /// <summary>The normative keyword: MUST, MUST NOT, SHOULD, etc.</summary>
    public string Keyword { get; init; } = string.Empty;

    /// <summary>Line offset within the section text where the keyword appears.</summary>
    public int LineOffset { get; init; }
}
