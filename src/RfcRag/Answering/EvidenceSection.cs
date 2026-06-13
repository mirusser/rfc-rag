namespace RfcRag.Answering;

/// <summary>
/// A Section with its full text and heading chain packaged for use as evidence in answer generation.
/// Each EvidenceSection carries a stable evidence id (e.g. "9110#9.3.1") that citations can reference,
/// independent of the underlying database id.
/// </summary>
internal sealed record class EvidenceSection
{
    /// <summary>RFC number (e.g., 9110).</summary>
    public int RfcNumber { get; init; }

    /// <summary>Section identifier (e.g., "9.3.1").</summary>
    public string Section { get; init; } = string.Empty;

    /// <summary>Section heading text (e.g., "GET").</summary>
    public string? Heading { get; init; }

    /// <summary>
    /// Chain of ancestor headings from the RFC root to this section's immediate parent,
    /// ordered outermost-first; useful for context when the parent section is not itself in the evidence pack.
    /// Empty for top-level sections.
    /// </summary>
    public IReadOnlyList<string> ParentHeadings { get; init; } = [];

    /// <summary>Full text content of this section.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Relevance score from the search/rerank pipeline.</summary>
    public double Score { get; init; }

    /// <summary>
    /// Stable, citation-friendly identifier: "{RfcNumber}#{Section}" (e.g. "9110#9.3.1").
    /// This is the id that citations reference and is part of the ask_rfc wire contract.
    /// </summary>
    public string EvidenceId { get; init; } = string.Empty;

    /// <summary>Normative Occurrences found in this section (populated during enrichment).</summary>
    public IReadOnlyList<Models.NormativeOccurrenceData> NormativeOccurrences { get; init; } = [];

    /// <summary>Relation note for this section's RFC (e.g., obsoletion warning). Populated during enrichment.</summary>
    public string? RelationNote { get; init; }

    /// <summary>RFC status block populated during enrichment. Null when status is unavailable.</summary>
    public RfcStatusBlock? Status { get; init; }
}
