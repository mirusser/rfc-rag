using RfcRag.Answering;

namespace RfcRag.Tests.Fakes;

internal sealed class FakeAskService : IAskService
{
    public GeneratedAnswer? Result { get; set; }
    public int CallCount { get; private set; }

    public Task<GeneratedAnswer> AskAsync(
        string question,
        int? limit = null,
        string? normativeKeyword = null,
        bool includeObsolete = false,
        bool includeErrata = false,
        string? errataStatus = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(Result ?? new GeneratedAnswer
        {
            Answer = $"Fake answer to: {question}",
            Citations = [],
        });
    }
}
