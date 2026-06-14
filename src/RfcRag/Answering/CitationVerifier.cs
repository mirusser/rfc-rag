using System.Text.RegularExpressions;

namespace RfcRag.Answering;

/// <summary>
/// Verifies that claims in a generated answer are supported by inline citations
/// that actually match the evidence text. Produces per-claim verification status
/// and a support rate for the overall answer.
/// </summary>
internal static class CitationVerifier
{
    private const string StatusUncited = "uncited";
    private const string StatusSupported = "supported";
    private const string WarningType = "verification_warning";
    // Sentence-ending punctuation (. ! ?) followed by whitespace and an uppercase letter,
    // but NOT after known abbreviations that commonly appear mid-sentence.
    // Explicit timeout prevents ReDoS from pathological input.
    private static readonly Regex SentenceSplitter = new(
        @"(?<!\b(?:e\.g|i\.e|etc|al|vs|dr|mr|mrs|ms|st|dept|approx))[.!?]\s+(?=[A-Z""'(])",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(2));

    // Inline citation markers in "[evidenceId]" format, e.g. "[9110#9.3.1]"
    // Explicit timeout prevents ReDoS from pathological input.
    private static readonly Regex CitationMarker = new(
        @"\[(\d+#[\d.]+)\]",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    /// <summary>
    /// Verifies each claim in <paramref name="answer"/> against <paramref name="pack"/>.
    /// Returns a <see cref="ClaimVerificationResult"/> with per-claim status,
    /// an overall support rate, and derived warning objects.
    /// </summary>
    public static ClaimVerificationResult Verify(GeneratedAnswer answer, EvidencePack pack)
    {
        if (string.IsNullOrWhiteSpace(answer.Answer))
        {
            return new ClaimVerificationResult();
        }

        string[] rawClaims = SentenceSplitter.Split(answer.Answer);
        List<ClaimVerification> claims = [];
        List<AnswerWarning> warnings = [];

        foreach (string claim in rawClaims.Select(rawClaim => rawClaim.Trim()))
        {
            if (claim.Length == 0)
                continue;

            // Extract citation markers from the claim
            MatchCollection citationMatches = CitationMarker.Matches(claim);
            List<string> claimEvidenceIds = [];
            foreach (Match match in citationMatches)
            {
                claimEvidenceIds.Add(match.Groups[1].Value);
            }

            string status;
            if (claimEvidenceIds.Count == 0)
            {
                status = StatusUncited;
                warnings.Add(new AnswerWarning
                {
                    Type = WarningType,
                    Message = $"Claim contains no inline citations: \"{Truncate(claim, 120)}\"",
                });
            }
            else
            {
                // Check whether any cited citation passes the support check
                bool anySupported = false;
                foreach (string evidenceId in claimEvidenceIds)
                {
                    Citation? citation = answer.Citations.FirstOrDefault(c =>
                        string.Equals(c.EvidenceId, evidenceId, StringComparison.Ordinal));

                    if (citation is null || string.IsNullOrWhiteSpace(citation.RelevantText))
                        continue;

                    EvidenceSection? section = pack.Sections.FirstOrDefault(s =>
                        string.Equals(s.EvidenceId, evidenceId, StringComparison.Ordinal));

                    if (section is not null &&
                        section.Text.Contains(
                            citation.RelevantText.TrimEnd('.', '!', '?', ' ', '\t'),
                            StringComparison.Ordinal))
                    {
                        anySupported = true;
                        break;
                    }
                }

                status = anySupported ? StatusSupported : "unsupported";

                if (!anySupported)
                {
                    warnings.Add(new AnswerWarning
                    {
                        Type = WarningType,
                        Message = $"Claim citations could not be verified against evidence: \"{Truncate(claim, 120)}\"",
                        EvidenceId = claimEvidenceIds[0],
                    });
                }
            }

            claims.Add(new ClaimVerification
            {
                Claim = claim,
                Status = status,
                CitationEvidenceIds = claimEvidenceIds.Count > 0 ? [.. claimEvidenceIds] : null,
            });
        }

        double supportRate = claims.Count > 0
            ? (double)claims.Count(c => string.Equals(c.Status, StatusSupported, StringComparison.Ordinal)) / claims.Count
            : 0.0;

        return new ClaimVerificationResult
        {
            Claims = [.. claims],
            ClaimSupportRate = supportRate,
            VerificationWarnings = [.. warnings],
        };
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}
