namespace RfcRag.Answering;

/// <summary>
/// Structured evidence for an RFC question, assembled from ranked search results.
/// This is the output of the Context Assembler — the deep module that hides deduplication,
/// overlap collapse, budget enforcement, and enrichment behind a single entry point.
/// Callers get a ready-to-consume pack; they know nothing about assembly internals.
/// </summary>
internal sealed record class EvidencePack
{
    /// <summary>The original query that produced this evidence.</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Assembled evidence Sections in deterministic order (by rank score, then RFC number, then section).
    /// </summary>
    public IReadOnlyList<EvidenceSection> Sections { get; init; } = [];

    /// <summary>Total character count of all included Section texts.</summary>
    public int TotalChars { get; init; }

    /// <summary>Estimated token count (TotalChars / 4), for downstream LLM budget awareness.</summary>
    public int EstimatedTokens { get; init; }

    /// <summary>The character budget that was enforced.</summary>
    public int BudgetChars { get; init; }

    /// <summary>True when evidence was truncated to fit the budget.</summary>
    public bool BudgetExceeded { get; init; }

    /// <summary>Warnings about budget, truncation, obsoletion, and enrichment issues.</summary>
    public IReadOnlyList<EvidenceWarning> Warnings { get; init; } = [];

    /// <summary>Relation notes for RFCs in the evidence (populated during enrichment).</summary>
    public IReadOnlyList<string> RelationNotes { get; init; } = [];
}
