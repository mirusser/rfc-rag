using RfcRag.Search;

namespace RfcRag.Tests.Fakes;

internal sealed class FakeVectorDataSearch : IVectorDataSearch
{
    public IReadOnlyList<SearchResult> Results { get; set; } = [];

    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        return Task.FromResult((IReadOnlyList<SearchResult>)Results.Take(limit).ToArray());
    }
}
