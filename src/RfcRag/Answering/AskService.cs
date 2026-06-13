using RfcRag.Infrastructure;

namespace RfcRag.Answering;

/// <summary>
/// Orchestrates the full ask-RFC pipeline: hybrid search → evidence assembly → answer generation.
/// Each method call runs a complete search + assemble + generate cycle, capturing per-query
/// timing traces when tracing is enabled.
/// </summary>
internal sealed class AskService(
    ISearchService searchService,
    ContextAssembler contextAssembler,
    AnswerGenerator answerGenerator,
    IOptions<RfcRagOptions> options,
    ITraceQueue traceQueue) : IAskService
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
        bool includeErrata = false,
        string? errataStatus = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var traceId = Guid.NewGuid().ToString("D");

        var opts = options.Value;
        int effectiveLimit = limit ?? DefaultSearchLimit;
        QueryPlan? queryPlan = opts.QueryPlannerEnabled ? QueryPlanner.Plan(question) : null;
        string? effectiveNormativeKeyword = string.IsNullOrWhiteSpace(normativeKeyword)
            ? queryPlan?.SuggestedNormativeKeyword
            : normativeKeyword;

        // Phase 1: Hybrid search
        DateTime searchStart = DateTime.UtcNow;
        IReadOnlyList<SearchResult> results = await searchService.SearchAsync(
            question, effectiveLimit, effectiveNormativeKeyword, includeObsolete, cancellationToken)
            .ConfigureAwait(false);

        // Phase 2: Evidence assembly
        DateTime assembleStart = DateTime.UtcNow;
        EvidencePack pack = await contextAssembler.AssembleAsync(
            question,
            results,
            opts.EvidenceBudgetChars,
            includeObsolete,
            includeErrata: includeErrata,
            errataStatus: errataStatus,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Phase 3: Answer generation
        DateTime generateStart = DateTime.UtcNow;
        GeneratedAnswer answer = await answerGenerator.GenerateAsync(
            pack, question, cancellationToken)
            .ConfigureAwait(false);
        DateTime generateEnd = DateTime.UtcNow;

        // Phase 4: Citation verification
        ClaimVerificationResult verification = CitationVerifier.Verify(answer, pack);
        answer = answer with
        {
            Verification = verification,
            Warnings = [.. answer.Warnings, .. verification.VerificationWarnings],
        };

        var trace = new QueryTrace
        {
            TraceId = traceId,
            Question = question,
            TimestampUtc = DateTime.UtcNow,
            Stages =
            [
                new TraceStage { Name = "search", StartedAtUtc = searchStart, CompletedAtUtc = assembleStart },
                new TraceStage { Name = "assemble", StartedAtUtc = assembleStart, CompletedAtUtc = generateStart },
                new TraceStage { Name = "generate", StartedAtUtc = generateStart, CompletedAtUtc = generateEnd },
            ],
            CandidateRfcNumbers = results.Select(r => r.RfcNumber).Distinct().ToArray(),
            AnswerGenerated = true,
            WarningCount = answer.Warnings.Count,
        };

        traceQueue.Enqueue(trace);

        return answer with
        {
            Retrieval = CreateRetrievalInfo(
                opts.QueryPlannerEnabled,
                queryPlan,
                effectiveNormativeKeyword,
                includeErrata,
                errataStatus),
        };
    }

    private static RetrievalInfo CreateRetrievalInfo(
        bool queryPlannerEnabled,
        QueryPlan? queryPlan,
        string? normativeKeyword,
        bool includeErrata,
        string? errataStatus) =>
        new()
        {
            Strategy = queryPlannerEnabled ? QueryPlannerStrategy : HybridSearchStrategy,
            Filters = new RetrievalFilters
            {
                NormativeKeyword = normativeKeyword,
                IncludeErrata = includeErrata,
                ErrataStatus = errataStatus,
            },
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
