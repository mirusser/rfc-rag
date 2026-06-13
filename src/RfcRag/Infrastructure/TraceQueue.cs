using System.Threading.Channels;

namespace RfcRag.Infrastructure;

/// <summary>
/// Bounded <see cref="Channel{T}"/>-backed implementation of <see cref="ITraceQueue"/>.
/// Dropping mode: if the channel is full, traces are silently dropped rather than
/// blocking the caller or unboundedly growing memory.
/// </summary>
internal sealed class TraceQueue : ITraceQueue, IDisposable
{
    private readonly Channel<QueryTrace> _channel;

    /// <summary>
    /// Creates a trace queue with a bounded capacity of <paramref name="capacity"/>
    /// items. When the channel is full, new traces are silently dropped.
    /// </summary>
    public TraceQueue(int capacity = 256)
    {
        _channel = Channel.CreateBounded<QueryTrace>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });
    }

    /// <summary>
    /// Gets the reader for the background service to consume traces.
    /// </summary>
    internal ChannelReader<QueryTrace> Reader => _channel.Reader;

    /// <inheritdoc/>
    public void Enqueue(QueryTrace trace)
    {
        // TryWrite is non-blocking; drops the trace if the channel is full.
        _channel.Writer.TryWrite(trace);
    }

    /// <summary>
    /// Marks the channel as complete, signaling to the reader that no more
    /// traces will be enqueued.
    /// </summary>
    public void Dispose()
    {
        _channel.Writer.TryComplete();
    }
}
