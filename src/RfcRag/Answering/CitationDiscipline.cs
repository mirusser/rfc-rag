namespace RfcRag.Answering;

/// <summary>
/// Enforces citation integrity: verifies that cited text appears verbatim
/// in the evidence and demotes unsupported answers.
/// </summary>
internal static class CitationDiscipline
{
    /// <summary>
    /// Verifies that each citation's <see cref="Citation.RelevantText"/> appears verbatim
    /// in the corresponding evidence section. Citations where the text does not match
    /// (or where <see cref="Citation.RelevantText"/> is null/empty) are excluded.
    /// </summary>
    public static IReadOnlyList<Citation> VerifyCitations(
        IReadOnlyList<Citation> citations,
        EvidencePack pack)
    {
        List<Citation> verified = [];
        foreach (Citation citation in citations)
        {
            if (string.IsNullOrWhiteSpace(citation.RelevantText))
            {
                continue;
            }

            EvidenceSection? section = pack.Sections.FirstOrDefault(s =>
                string.Equals(s.EvidenceId, citation.EvidenceId, StringComparison.Ordinal));

            if (section is null)
            {
                continue;
            }

            if (section.Text.Contains(citation.RelevantText, StringComparison.Ordinal))
            {
                verified.Add(citation);
            }
        }

        return verified;
    }

    /// <summary>
    /// When the answer has no citations and is not already a no-answer, demotes it to
    /// a no-answer with a message indicating verification failure.
    /// </summary>
    public static GeneratedAnswer DemoteOnNoCitations(GeneratedAnswer answer)
    {
        if (answer.Citations.Count > 0 || answer.NoAnswer)
        {
            return answer;
        }

        return new GeneratedAnswer
        {
            Answer = "The generated answer could not be verified against the evidence.",
            Citations = [],
            Model = answer.Model,
            NoAnswer = true,
        };
    }
}
