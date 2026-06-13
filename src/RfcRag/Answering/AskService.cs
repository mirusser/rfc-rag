namespace RfcRag.Answering;

/// <summary>
/// Orchestrates the full ask-RFC pipeline: hybrid search → evidence assembly → answer generation.
/// Each method call runs a complete search + assemble + generate cycle.
/// </summary>
internal sealed class AskService(
    ISearchService searchService,
    ContextAssembler contextAssembler,
    AnswerGenerator answerGenerator,
    IOptions<RfcRagOptions> options) : IAskService
{
    /// <summary>Default number of search results to retrieve from hybrid search.</summary>
    private const int DefaultSearchLimit = 20;
    private const string HybridSearchStrategy = "hybrid-search";
    private const string QueryPlannerStrategy = "query-planner";

    /// <inheritdoc/>
    public async Task<GeneratedAnswer> AskAsync(
        string question,
        int? limit = null,
        string? normativeKeyword = null,
        bool includeObsolete = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var opts = options.Value;
        int effectiveLimit = limit ?? DefaultSearchLimit;
        QueryPlan? queryPlan = opts.QueryPlannerEnabled ? QueryPlanner.Plan(question) : null;
        string? effectiveNormativeKeyword = string.IsNullOrWhiteSpace(normativeKeyword)
            ? queryPlan?.SuggestedNormativeKeyword
            : normativeKeyword;

        // Phase 1: Hybrid search
        IReadOnlyList<SearchResult> results = await searchService.SearchAsync(
            question, effectiveLimit, effectiveNormativeKeyword, includeObsolete, cancellationToken)
            .ConfigureAwait(false);

        // Phase 2: Evidence assembly
        EvidencePack pack = await contextAssembler.AssembleAsync(
            question, results, opts.EvidenceBudgetChars, includeObsolete, cancellationToken)
            .ConfigureAwait(false);

        // Phase 3: Answer generation
        GeneratedAnswer answer = await answerGenerator.GenerateAsync(
            pack, question, cancellationToken)
            .ConfigureAwait(false);

        return answer with
        {
            Retrieval = CreateRetrievalInfo(opts.QueryPlannerEnabled, queryPlan, effectiveNormativeKeyword),
        };
    }

    private static RetrievalInfo CreateRetrievalInfo(
        bool queryPlannerEnabled,
        QueryPlan? queryPlan,
        string? normativeKeyword) =>
        new()
        {
            Strategy = queryPlannerEnabled ? QueryPlannerStrategy : HybridSearchStrategy,
            Filters = new RetrievalFilters { NormativeKeyword = normativeKeyword },
            Plan = queryPlan is null ? null : CreateRetrievalPlanInfo(queryPlan),
        };

    private static RetrievalPlanInfo CreateRetrievalPlanInfo(QueryPlan queryPlan) =>
        new()
        {
            RfcNumbers = queryPlan.RfcNumbers,
            SectionReferences = queryPlan.SectionReferences
                .Select(sectionReference => new RetrievalSectionReference
                {
                    RfcNumber = sectionReference.RfcNumber,
                    Section = sectionReference.Section,
                })
                .ToArray(),
            ProtocolRfcNumbers = queryPlan.ProtocolRfcNumbers,
            SuggestedNormativeKeyword = queryPlan.SuggestedNormativeKeyword,
            HasAbnfIntent = queryPlan.HasAbnfIntent,
            IncludeObsolete = queryPlan.IncludeObsolete,
            NeedsCurrentSpec = queryPlan.NeedsCurrentSpec,
            Rationale = queryPlan.Rationale,
        };
}
