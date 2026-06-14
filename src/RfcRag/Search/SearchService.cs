namespace RfcRag.Search;

internal sealed class SearchService(
    SearchRepository searchRepository,
    MetadataRepository metadataRepository,
    EmbeddingService embeddingService,
    IOptions<RfcRagOptions> options) : ISearchService
{

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int limit,
        string? normativeKeyword,
        bool includeObsolete,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        // Normalize whitespace-only keyword to null so the SQL predicate is not applied
        QueryPlan? queryPlan = options.Value.QueryPlannerEnabled ? QueryPlanner.Plan(query) : null;
        string? keyword = string.IsNullOrWhiteSpace(normativeKeyword)
            ? queryPlan?.SuggestedNormativeKeyword
            : normativeKeyword;

        // Explicit includeObsolete parameter overrides the plan's historical-intent detection
        bool effectiveIncludeObsolete = includeObsolete || (queryPlan?.IncludeObsolete ?? false);

        IReadOnlyList<float[]> embeddings = await embeddingService.GenerateEmbeddingsAsync(
            [query],
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<SearchResult> results;
        IReadOnlyDictionary<int, RfcRelationsBatch> rfcStatuses;

        if (options.Value.RerankerEnabled)
        {
            IReadOnlyList<HybridCandidate> candidates = await searchRepository.SearchHybridWideCandidatesAsync(
                query,
                embeddings[0],
                limit,
                keyword,
                cancellationToken).ConfigureAwait(false);

            IReadOnlyList<int> candidateRfcNumbers = candidates.Select(c => c.RfcNumber).Distinct().ToList();
            rfcStatuses = candidateRfcNumbers.Count > 0
                ? await metadataRepository.GetRelationsBatchAsync(candidateRfcNumbers, cancellationToken).ConfigureAwait(false)
                : new Dictionary<int, RfcRelationsBatch>();

            // Pass effectiveIncludeObsolete so the reranker suppresses the penalty when requested
            QueryPlan? planForReranker = effectiveIncludeObsolete && queryPlan is not null
                ? queryPlan with { IncludeObsolete = true }
                : queryPlan;

            results = DeterministicReranker.Rerank(query, candidates, planForReranker, rfcStatuses, limit);
        }
        else
        {
            results = await searchRepository.SearchHybridAsync(
                query,
                embeddings[0],
                limit,
                keyword,
                cancellationToken).ConfigureAwait(false);

            IReadOnlyList<int> resultRfcNumbers = results.Select(r => r.RfcNumber).Distinct().ToList();
            rfcStatuses = resultRfcNumbers.Count > 0
                ? await metadataRepository.GetRelationsBatchAsync(resultRfcNumbers, cancellationToken).ConfigureAwait(false)
                : new Dictionary<int, RfcRelationsBatch>();
        }

        results = EnrichWithStatus(results, rfcStatuses);

        if (queryPlan is null || queryPlan.SectionReferences.Count == 0)
            return results;

        return await MergeReferencedSectionsAsync(
            queryPlan.SectionReferences,
            results,
            rfcStatuses,
            limit,
            cancellationToken).ConfigureAwait(false);
    }

    internal static IReadOnlyList<SearchResult> EnrichWithStatus(
        IReadOnlyList<SearchResult> results,
        IReadOnlyDictionary<int, RfcRelationsBatch> rfcStatuses)
    {
        if (rfcStatuses.Count == 0)
            return results;

        return results
            .Select(r => rfcStatuses.TryGetValue(r.RfcNumber, out var rel)
                ? r with { Status = RfcStatusBlock.From(rel) }
                : r)
            .ToArray();
    }

    private async Task<IReadOnlyList<SearchResult>> MergeReferencedSectionsAsync(
        IReadOnlyList<QuerySectionReference> sectionReferences,
        IReadOnlyList<SearchResult> searchResults,
        IReadOnlyDictionary<int, RfcRelationsBatch> rfcStatuses,
        int limit,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<(int RfcNumber, string Section)>();
        var mergedResults = new List<SearchResult>(sectionReferences.Count + searchResults.Count);

        foreach (QuerySectionReference sectionReference in sectionReferences.Where(sectionReference =>
                     seen.Add((sectionReference.RfcNumber, sectionReference.Section))))
        {
            RfcSection? section = await searchRepository
                .GetSectionAsync(sectionReference.RfcNumber, sectionReference.Section, cancellationToken)
                .ConfigureAwait(false);

            if (section is null)
                continue;

            RfcStatusBlock? status = rfcStatuses.TryGetValue(sectionReference.RfcNumber, out var rel)
                ? RfcStatusBlock.From(rel)
                : null;

            mergedResults.Add(new SearchResult(
                section.Id,
                section.RfcNumber,
                section.Title,
                section.Section,
                section.Heading,
                section.Text,
                section.SourcePath,
                section.Url,
                Score: 1.0)
            {
                Status = status,
            });
        }

        mergedResults.AddRange(searchResults.Where(searchResult => seen.Add((searchResult.RfcNumber, searchResult.Section))));

        int effectiveLimit = limit > 0 ? limit : searchResults.Count;
        return effectiveLimit > 0
            ? mergedResults.Take(effectiveLimit).ToArray()
            : mergedResults;
    }

    public Task<RfcSection?> GetSectionAsync(int rfcNumber, string section, CancellationToken cancellationToken) =>
        searchRepository.GetSectionAsync(rfcNumber, section, cancellationToken);

    public Task<IReadOnlyList<RfcSection>> GetRfcAsync(int rfcNumber, CancellationToken cancellationToken) =>
        searchRepository.GetRfcAsync(rfcNumber, cancellationToken);

    public Task<IReadOnlyList<SearchResult>> SearchNormativeAsync(
        string keyword,
        int[]? rfcNumbers,
        int limit,
        CancellationToken cancellationToken) =>
        searchRepository.SearchNormativeAsync(keyword, rfcNumbers, limit, cancellationToken);

    public Task<IReadOnlyList<SearchResult>> SearchAbnfAsync(
        string query,
        int[]? rfcNumbers,
        int limit,
        CancellationToken cancellationToken) =>
        searchRepository.SearchAbnfAsync(query, rfcNumbers, limit, cancellationToken);

    public Task<RfcMetadata?> GetRfcMetadataAsync(int rfcNumber, CancellationToken cancellationToken) =>
        metadataRepository.GetIndexedRfcMetadataAsync(rfcNumber, cancellationToken);

    public Task<IReadOnlyList<RfcMetadata>> FindBackReferencesAsync(int rfcNumber, CancellationToken cancellationToken) =>
        metadataRepository.FindBackReferencesAsync(rfcNumber, cancellationToken);

    public Task<IReadOnlyList<RfcMetadata>> ListIndexedAsync(int limit, int offset, CancellationToken cancellationToken) =>
        metadataRepository.ListIndexedAsync(limit, offset, cancellationToken);

    public Task<string> GetStatsAsync(CancellationToken cancellationToken) =>
        metadataRepository.GetStatsAsync(cancellationToken);

    public Task<IReadOnlyDictionary<string, string?>> GetTocAsync(
        int rfcNumber,
        CancellationToken cancellationToken) =>
        searchRepository.GetTocAsync(rfcNumber, cancellationToken);

    public Task<(RfcSection Parent, IReadOnlyList<RfcSection> Children)> GetSectionWithChildrenAsync(
        int rfcNumber,
        string section,
        int depth,
        CancellationToken cancellationToken) =>
        searchRepository.GetSectionWithChildrenAsync(rfcNumber, section, depth, cancellationToken);

    public async Task<IReadOnlyDictionary<string, RfcSection>> GetSectionWithExpandedTypesAsync(
        int rfcNumber,
        string section,
        CancellationToken cancellationToken)
    {
        RfcSection? target = await searchRepository.GetSectionAsync(rfcNumber, section, cancellationToken).ConfigureAwait(false);
        if (target is null)
            return new Dictionary<string, RfcSection>(StringComparer.Ordinal);

        var typeNames = ExtractPascalCaseTypeNames(target.Text, target.Heading);
        if (typeNames.Count == 0)
            return new Dictionary<string, RfcSection>(StringComparer.Ordinal);

        IReadOnlyList<RfcSection> rfcSections = await searchRepository.GetRfcAsync(rfcNumber, cancellationToken).ConfigureAwait(false);
        var knownHeadings = rfcSections
            .Where(rfcSection => rfcSection.Heading is not null)
            .Select(rfcSection => rfcSection.Heading!)
            .ToHashSet(StringComparer.Ordinal);

        string[] matchingTypeNames = typeNames
            .Where(knownHeadings.Contains)
            .ToArray();

        return await searchRepository.FindSectionsByHeadingsAsync(rfcNumber, matchingTypeNames, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyDictionary<int, RfcRelationsBatch>> GetRelationsBatchAsync(
        IReadOnlyList<int> rfcNumbers, CancellationToken cancellationToken) =>
        metadataRepository.GetRelationsBatchAsync(rfcNumbers, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<NormativeOccurrenceData>>> GetNormativeOccurrencesBatchAsync(
        IReadOnlyList<Guid> sectionIds, CancellationToken cancellationToken) =>
        searchRepository.GetNormativeOccurrencesBatchAsync(sectionIds, cancellationToken);


    public Task<IReadOnlyDictionary<string, IReadOnlyList<RfcErratum>>> GetErrataBatchAsync(
        IReadOnlyList<int> rfcNumbers,
        IReadOnlyCollection<string> statuses,
        CancellationToken cancellationToken) =>
        searchRepository.GetErrataBatchAsync(rfcNumbers, statuses, cancellationToken);

    internal static List<string> ExtractPascalCaseTypeNames(string sectionText, string? sectionHeading)
    {
        var typeNames = new HashSet<string>(StringComparer.Ordinal);
        var words = sectionText.Split(new[] { ' ', '\n', '\r', '\t', '(', ')', '{', '}', ',', '.', ':', ';', '[', ']' },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string word in words)
        {
            if (word.Length >= 2 && char.IsUpper(word[0]) && word.All(c => char.IsLetterOrDigit(c) || c == '_'))
            {
                typeNames.Add(word);
            }
        }

        if (sectionHeading is not null)
            typeNames.Remove(sectionHeading);

        return typeNames.ToList();
    }
}
