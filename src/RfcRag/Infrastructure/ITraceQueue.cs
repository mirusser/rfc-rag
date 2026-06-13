namespace RfcRag.Infrastructure;

/// <summary>
/// Thread-safe queue for enqueueing query traces for asynchronous background writing.
/// Replaces fire-and-forget trace writes with a structured producer-consumer pattern.
/// </summary>
internal interface ITraceQueue
{
    /// <summary>
    /// Enqueues a trace to be written to the trace file by the background service.
    /// Returns immediately without waiting for the write to complete.
    /// </summary>
    /// <param name="trace">The query trace to persist.</param>
    void Enqueue(QueryTrace trace);
}
