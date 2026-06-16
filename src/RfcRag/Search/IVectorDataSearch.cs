namespace RfcRag.Search;

internal interface IVectorDataSearch
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit, CancellationToken cancellationToken);
}
