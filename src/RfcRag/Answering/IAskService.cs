namespace RfcRag.Answering;

/// <summary>Service that orchestrates the full ask-RFC pipeline: search → assemble → generate.</summary>
public interface IAskService
{
    /// <summary>
    /// Asks a question against the RFC corpus and returns a cited answer.
    /// </summary>
    /// <param name="question">The user's question about RFCs.</param>
    /// <param name="limit">Maximum number of search results to retrieve (optional).</param>
    /// <param name="normativeKeyword">Optional normative keyword to filter search results by (e.g., "MUST NOT", "SHOULD").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured answer with inline citations.</returns>
    Task<GeneratedAnswer> AskAsync(string question, int? limit = null, string? normativeKeyword = null, CancellationToken cancellationToken = default);
}
