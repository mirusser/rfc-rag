namespace RfcRag.Answering;

/// <summary>
/// Assembles ranked SearchResults into a deduplicated, budget-enforced Evidence Pack.
/// This is the deep module of the evidence assembly pipeline — callers get a ready-to-use pack
/// and know nothing about deduplication, overlap collapse, heading-chain construction, or budget enforcement.
/// </summary>
internal sealed class ContextAssembler(ISearchService searchService)
{
    /// <summary>Approximate token estimate: 1 token ≈ 4 characters (AD10).</summary>
    private const int CharsPerToken = 4;
    private const int MaxSectionsPerRfc = 5;

    // Warning type constants — delegated to EvidenceWarning for shared access.


    /// <summary>Assembles an Evidence Pack from ranked search results.</summary>
    /// <param name="query">The original search query.</param>
    /// <param name="results">Ranked search results (highest score first).</param>
    /// <param name="budgetChars">Maximum total characters for included Section texts.</param>
    /// <param name="includeObsolete">When true, suppresses obsoleted-RFC warnings (penalties were already suppressed upstream).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An assembled Evidence Pack with deduplicated, ordered Sections and warnings.</returns>
    public async Task<EvidencePack> AssembleAsync(
        string query,
        IReadOnlyList<SearchResult> results,
        int budgetChars,
        bool includeObsolete,
        bool includeErrata = false,
        string? errataStatus = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (results.Count == 0)
        {
            return new EvidencePack
            {
                Query = query,
                BudgetChars = budgetChars,
            };
        }

        var warnings = new List<EvidenceWarning>();

        // Phase 1: Deduplicate and collapse overlaps
        var deduplicated = DeduplicateAndCollapse(results, warnings);

        // Phase 2: Enforce per-RFC cap
        var capped = EnforcePerRfcCap(deduplicated, warnings);

        // Phase 3: Fetch full section text and build evidence sections
        var evidenceSections = new List<EvidenceSection>();
        var sectionIds = new List<Guid>();
        int totalChars = 0;
        bool budgetExceeded = false;
        bool hadAtLeastOne = false;

        foreach (var result in capped)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RfcSection? section = await searchService.GetSectionAsync(
                result.RfcNumber, result.Section, cancellationToken).ConfigureAwait(false);

            if (section is null)
                continue;

            // Build parent-heading chain
            IReadOnlyDictionary<string, string?> toc = await searchService.GetTocAsync(
                result.RfcNumber, cancellationToken).ConfigureAwait(false);
            var parentHeadings = BuildParentHeadingChain(result.Section, toc);

            int sectionChars = section.Text.Length;

            // Budget enforcement: always include at least the first section
            if (!hadAtLeastOne)
            {
                // First section always included, even if oversized
                hadAtLeastOne = true;
                if (sectionChars > budgetChars)
                {
                    budgetExceeded = true;
                }
            }
            else if (totalChars + sectionChars > budgetChars)
            {
                budgetExceeded = true;
                break;
            }

            totalChars += sectionChars;

            evidenceSections.Add(new EvidenceSection
            {
                RfcNumber = result.RfcNumber,
                Section = result.Section,
                Heading = section.Heading,
                ParentHeadings = parentHeadings,
                Text = section.Text,
                Score = result.Score,
                EvidenceId = EvidenceSection.CreateEvidenceId(result.RfcNumber, result.Section),
                Status = result.Status,
            });

            sectionIds.Add(result.Id);
        }

        // Phase 4: Enrichment — batch-fetch relations and normative occurrences
        IReadOnlyList<string> relationNotes = [];
        if (evidenceSections.Count > 0)
        {
            relationNotes = await EnrichAsync(
                evidenceSections, sectionIds, warnings, includeObsolete, cancellationToken)
                .ConfigureAwait(false);

            if (includeErrata)
            {
                await AttachErrataAsync(evidenceSections, warnings, errataStatus, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (budgetExceeded)
        {
            warnings.Add(new EvidenceWarning
            {
                Type = EvidenceWarning.BudgetExceeded,
                Message = $"Evidence truncated to fit {budgetChars}-character budget. " +
                          $"{evidenceSections.Count} sections included.",
            });
        }

        int estimatedTokens = totalChars / CharsPerToken;

        return new EvidencePack
        {
            Query = query,
            Sections = evidenceSections,
            TotalChars = totalChars,
            EstimatedTokens = estimatedTokens,
            BudgetChars = budgetChars,
            BudgetExceeded = budgetExceeded,
            Warnings = warnings,
            RelationNotes = relationNotes,
        };
    }

    /// <summary>
    /// Deduplicates by (RfcNumber, Section), keeping the highest score.
    /// Then collapses ancestor/descendant overlaps — when both a parent section
    /// and its child subsection appear, keeps the more specific (child) and warns.
    /// </summary>
    private static List<SearchResult> DeduplicateAndCollapse(
        IReadOnlyList<SearchResult> results,
        List<EvidenceWarning> warnings)
    {
        // Pass 1: deduplicate exact matches, keeping highest score
        var seen = new Dictionary<(int RfcNumber, string Section), SearchResult>();
        foreach (var result in results)
        {
            var key = (result.RfcNumber, result.Section);
            if (!seen.TryGetValue(key, out var existing) || result.Score > existing.Score)
            {
                seen[key] = result;
            }
        }

        // Pass 2: collapse ancestor/descendant overlaps
        var collapsed = new List<SearchResult>();
        foreach (var result in seen.Values.OrderByDescending(r => r.Score))
        {
            // Check if this result is an ancestor of any already-included result
            bool isAncestor = false;
            string ancestorPrefix = result.Section + ".";

            foreach (var existing in collapsed.Where(existing =>
                         existing.RfcNumber == result.RfcNumber
                         && existing.Section.StartsWith(ancestorPrefix, StringComparison.Ordinal)))
            {
                // This result is an ancestor of an already-included child — skip it
                isAncestor = true;
                warnings.Add(new EvidenceWarning
                {
                    Type = EvidenceWarning.OverlapCollapsed,
                    Message = $"Section {result.RfcNumber}#{result.Section} omitted in favor of " +
                              $"more specific subsection {existing.RfcNumber}#{existing.Section}.",
                    EvidenceId = EvidenceSection.CreateEvidenceId(result.RfcNumber, result.Section),
                });
                break;
            }

            if (isAncestor)
                continue;

            // Check if any already-included result is an ancestor of this one
            for (int i = collapsed.Count - 1; i >= 0; i--)
            {
                var existing = collapsed[i];
                if (existing.RfcNumber == result.RfcNumber &&
                    result.Section.StartsWith(existing.Section + ".", StringComparison.Ordinal))
                {
                    // This is a child of an already-included parent — replace parent with child
                    collapsed.RemoveAt(i);
                    warnings.Add(new EvidenceWarning
                    {
                        Type = EvidenceWarning.OverlapCollapsed,
                        Message = $"Section {existing.RfcNumber}#{existing.Section} omitted in favor of " +
                                  $"more specific subsection {result.RfcNumber}#{result.Section}.",
                        EvidenceId = EvidenceSection.CreateEvidenceId(existing.RfcNumber, existing.Section),
                    });
                    break;
                }
            }

            collapsed.Add(result);
        }

        return collapsed;
    }

    /// <summary>Enforces the per-RFC section cap by keeping only the best-scoring N sections per RFC.</summary>
    private static List<SearchResult> EnforcePerRfcCap(
        List<SearchResult> results,
        List<EvidenceWarning> warnings)
    {
        var perRfc = new Dictionary<int, List<SearchResult>>();
        foreach (var result in results)
        {
            if (!perRfc.ContainsKey(result.RfcNumber))
                perRfc[result.RfcNumber] = [];
            perRfc[result.RfcNumber].Add(result);
        }

        var capped = new List<SearchResult>();
        foreach (var (rfcNumber, rfcResults) in perRfc)
        {
            var topN = rfcResults
                .OrderByDescending(r => r.Score)
                .Take(MaxSectionsPerRfc)
                .ToList();

            if (rfcResults.Count > MaxSectionsPerRfc)
            {
                warnings.Add(new EvidenceWarning
                {
                    Type = EvidenceWarning.OmittedSection,
                    Message = $"RFC {rfcNumber}: capped at {MaxSectionsPerRfc} sections " +
                              $"({rfcResults.Count - MaxSectionsPerRfc} omitted).",
                    EvidenceId = EvidenceSection.CreateEvidenceId(rfcNumber, EvidenceSection.RfcWildcard),
                });
            }

            capped.AddRange(topN);
        }

        // Re-sort by score descending, then by RFC number, then by section for deterministic tie-breaking
        return capped
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.RfcNumber)
            .ThenBy(r => r.Section, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Builds the chain of ancestor headings for a section, ordered outermost-first.
    /// Uses the RFC's table of contents for heading lookup.
    /// </summary>
    private static List<string> BuildParentHeadingChain(
        string section,
        IReadOnlyDictionary<string, string?> toc)
    {
        var chain = new List<string>();
        var parts = section.Split('.');

        // Walk up from the immediate parent to the outermost ancestor
        for (int i = parts.Length - 2; i >= 0; i--)
        {
            string ancestorId = string.Join(".", parts.Take(i + 1));
            if (toc.TryGetValue(ancestorId, out var heading) && heading is not null)
            {
                chain.Insert(0, heading);
            }
        }

        return chain;
    }

    private async Task<IReadOnlyList<string>> EnrichAsync(
        List<EvidenceSection> sections,
        List<Guid> sectionIds,
        List<EvidenceWarning> warnings,
        bool includeObsolete,
        CancellationToken cancellationToken)
    {
        // Status is already populated on each section from SearchResult.Status (set by SearchService).
        // No second GetRelationsBatchAsync call needed.
        var relationNotes = new List<string>();
        for (int i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            if (section.Status is null || section.Status.ObsoletedBy.Count == 0 || includeObsolete)
                continue;

            string obsMessage = $"RFC {section.RfcNumber} is obsoleted by RFC {string.Join(", ", section.Status.ObsoletedBy)}.";

            if (!relationNotes.Contains(obsMessage))
            {
                relationNotes.Add(obsMessage);
                warnings.Add(new EvidenceWarning
                {
                    Type = EvidenceWarning.ObsoletedRfc,
                    Message = obsMessage,
                    EvidenceId = EvidenceSection.CreateEvidenceId(section.RfcNumber, EvidenceSection.RfcWildcard),
                });
            }

            sections[i] = section with { RelationNote = obsMessage };
        }

        // Batch-fetch normative occurrences for all sections
        var normativeOccurrences = await searchService.GetNormativeOccurrencesBatchAsync(
            sectionIds, cancellationToken).ConfigureAwait(false);

        for (int i = 0; i < sections.Count; i++)
        {
            if (i < sectionIds.Count && normativeOccurrences.TryGetValue(sectionIds[i], out var occurrences))
            {
                sections[i] = sections[i] with { NormativeOccurrences = occurrences };
            }
        }

        return relationNotes;
    }


    private async Task AttachErrataAsync(
        List<EvidenceSection> sections,
        List<EvidenceWarning> warnings,
        string? errataStatus,
        CancellationToken cancellationToken)
    {
        string[] statuses = NormalizeErrataStatuses(errataStatus);
        int[] rfcNumbers = sections.Select(section => section.RfcNumber).Distinct().ToArray();
        IReadOnlyDictionary<string, IReadOnlyList<RfcErratum>> errataByEvidenceId = await searchService
            .GetErrataBatchAsync(rfcNumbers, statuses, cancellationToken)
            .ConfigureAwait(false);

        for (int i = 0; i < sections.Count; i++)
        {
            EvidenceSection section = sections[i];
            if (!errataByEvidenceId.TryGetValue(section.EvidenceId, out IReadOnlyList<RfcErratum>? errata)
                || errata.Count == 0)
            {
                continue;
            }

            RfcErratum[] matchingErrata = errata
                .Where(erratum => statuses.Contains(erratum.Status, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (matchingErrata.Length == 0)
            {
                continue;
            }

            sections[i] = section with { Errata = matchingErrata };

            foreach (RfcErratum erratum in matchingErrata.Where(erratum =>
                string.Equals(erratum.Status, RfcErratum.VerifiedStatus, StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add(new EvidenceWarning
                {
                    Type = EvidenceWarning.VerifiedErratum,
                    EvidenceId = section.EvidenceId,
                    Message = CreateVerifiedErratumWarning(section, erratum),
                });
            }
        }
    }

    private static string[] NormalizeErrataStatuses(string? errataStatus)
    {
        if (string.IsNullOrWhiteSpace(errataStatus))
        {
            return [RfcErratum.VerifiedStatus];
        }

        return errataStatus
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeErrataStatus)
            .Where(status => !string.IsNullOrWhiteSpace(status))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeErrataStatus(string status) =>
        RfcErratum.NormalizeStatus(status) ?? status;

    private static string CreateVerifiedErratumWarning(EvidenceSection section, RfcErratum erratum)
    {
        string message = $"RFC {section.RfcNumber} section {section.Section} has verified erratum {erratum.ErrataId}.";
        if (!string.IsNullOrWhiteSpace(erratum.CorrectedText))
        {
            message += $" Corrected text: {erratum.CorrectedText}";
        }

        return message;
    }
}
