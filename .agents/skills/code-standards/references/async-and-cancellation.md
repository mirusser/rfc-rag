# Async and Cancellation

Use async all the way.

Do not block on async code with .Result, .Wait(), or .GetAwaiter().GetResult() except at true process boundaries where no async alternative exists.

Async methods end in Async.

Accept and pass CancellationToken through all async I/O, external calls, waits, HTTP calls, embedding and vector store calls, file I/O, database calls, and long-running operations.

```csharp
public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
{
    return await repository.SearchAsync(query, cancellationToken).ConfigureAwait(false);
}
```

In reusable library or tool code, call ConfigureAwait(false) unless the project convention says otherwise.

Do not use async void except for event handlers.

Use ValueTask only when there is a measured reason or an API requires it. Prefer Task by default.