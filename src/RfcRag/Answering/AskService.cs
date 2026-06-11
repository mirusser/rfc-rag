using Microsoft.Extensions.Options;
using RfcRag.Search;
using RfcRag.Settings;

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

    /// <inheritdoc/>
    public async Task<GeneratedAnswer> AskAsync(
        string question,
        int? limit = null,
        string? normativeKeyword = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var opts = options.Value;
        int effectiveLimit = limit ?? DefaultSearchLimit;

        // Phase 1: Hybrid search
        IReadOnlyList<SearchResult> results = await searchService.SearchAsync(
            question, effectiveLimit, normativeKeyword, cancellationToken)
            .ConfigureAwait(false);

        // Phase 2: Evidence assembly
        EvidencePack pack = await contextAssembler.AssembleAsync(
            question, results, opts.EvidenceBudgetChars, cancellationToken)
            .ConfigureAwait(false);

        // Phase 3: Answer generation
        GeneratedAnswer answer = await answerGenerator.GenerateAsync(
            pack, question, cancellationToken)
            .ConfigureAwait(false);

        return answer;
    }
}
