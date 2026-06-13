namespace RfcRag.Answering;

/// <summary>
/// A structured warning produced during evidence assembly or enrichment.
/// Warnings are informational — they never cause assembly to fail.
/// </summary>
internal sealed record class EvidenceWarning
{
    /// <summary>Evidence pack was truncated to fit the character budget.</summary>
    public const string BudgetExceeded = "budget_exceeded";

    /// <summary>Ancestor section omitted in favor of a more specific child subsection.</summary>
    public const string OverlapCollapsed = "overlap_collapsed";

    /// <summary>Per-RFC section cap reached; lower-scoring sections omitted.</summary>
    public const string OmittedSection = "omitted_section";

    /// <summary>Section belongs to an obsoleted RFC.</summary>
    public const string ObsoletedRfc = "obsoleted_rfc";

    /// <summary>Section has one or more verified errata attached.</summary>
    public const string VerifiedErratum = "verified_erratum";

    /// <summary>Warning category. Stable contract values correspond to the const fields on this type.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Human-readable description of the warning.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Optional section context (evidence id like "9110#9.3.1") the warning relates to.</summary>
    public string? EvidenceId { get; init; }
}
