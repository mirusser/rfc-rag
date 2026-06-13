using System.Collections.Immutable;

namespace RfcRag.Models;

/// <summary>A single RFC Editor erratum loaded from a local errata snapshot.</summary>
public sealed record class RfcErratum
{
    /// <summary>The stable verified status value recognized by the RFC Editor.</summary>
    public const string VerifiedStatus = "verified";

    /// <summary>The stable held-for-document-update status value.</summary>
    public const string HeldForDocumentUpdateStatus = "held_for_document_update";

    /// <summary>The stable reported status value.</summary>
    public const string ReportedStatus = "reported";

    /// <summary>The three valid errata statuses recognized by the RFC Editor.</summary>
    public static readonly ImmutableArray<string> ValidStatuses = [VerifiedStatus, HeldForDocumentUpdateStatus, ReportedStatus];

    /// <summary>
    /// Normalizes an errata status string: trims whitespace, replaces spaces/hyphens with underscores,
    /// and returns the canonical lowercase form for known statuses. Unknown statuses are returned
    /// as-is (trimmed, space-replaced, lower-cased). Null or whitespace returns null.
    /// </summary>
    public static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return null;

        string trimmed = status.Trim();
        string normalized = trimmed.Replace(' ', '_').Replace('-', '_');

        if (string.Equals(normalized, VerifiedStatus, StringComparison.OrdinalIgnoreCase))
            return VerifiedStatus;

        if (string.Equals(normalized, HeldForDocumentUpdateStatus, StringComparison.OrdinalIgnoreCase))
            return HeldForDocumentUpdateStatus;

        if (string.Equals(normalized, ReportedStatus, StringComparison.OrdinalIgnoreCase))
            return ReportedStatus;

        return normalized;
    }
    /// <summary>Stable RFC Editor erratum identifier.</summary>
    public int ErrataId { get; init; }

    /// <summary>RFC number the erratum applies to.</summary>
    public int RfcNumber { get; init; }

    /// <summary>Section identifier reported by RFC Editor, when available.</summary>
    public string? Section { get; init; }

    /// <summary>Normalized status: verified, held_for_document_update, or reported.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Original text from the erratum entry.</summary>
    public string? OriginalText { get; init; }

    /// <summary>Corrected text from the erratum entry.</summary>
    public string? CorrectedText { get; init; }

    /// <summary>Date the erratum was reported.</summary>
    public DateOnly? ReportedDate { get; init; }
}
