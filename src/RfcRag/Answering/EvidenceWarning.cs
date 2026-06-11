namespace RfcRag.Answering;

/// <summary>
/// A structured warning produced during evidence assembly or enrichment.
/// Warnings are informational — they never cause assembly to fail.
/// </summary>
internal sealed record class EvidenceWarning
{
    /// <summary>
    /// Warning category. Stable contract values:
    /// "budget_exceeded", "omitted_section", "obsoleted_rfc", "overlap_collapsed".
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Human-readable description of the warning.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Optional section context (evidence id like "9110#9.3.1") the warning relates to.</summary>
    public string? EvidenceId { get; init; }
}
