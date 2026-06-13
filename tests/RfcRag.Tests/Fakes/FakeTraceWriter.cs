using RfcRag.Infrastructure;

namespace RfcRag.Tests.Fakes;

/// <summary>
/// Fake <see cref="ITraceQueue"/> that captures the most recently enqueued
/// <see cref="QueryTrace"/> in memory instead of writing via the background queue.
/// </summary>
internal sealed class FakeTraceWriter : ITraceQueue
{
    /// <summary>The last trace passed to <see cref="Enqueue"/>.</summary>
    public QueryTrace? LastTrace { get; private set; }

    public void Enqueue(QueryTrace trace)
    {
        LastTrace = trace;
    }
}
