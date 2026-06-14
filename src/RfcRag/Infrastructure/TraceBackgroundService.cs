using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RfcRag.Infrastructure;

/// <summary>
/// Background service that drains <see cref="TraceQueue"/> and writes each
/// trace via <see cref="QueryTraceWriter"/>.
/// </summary>
internal sealed class TraceBackgroundService : BackgroundService
{
    private readonly TraceQueue _queue;
    private readonly QueryTraceWriter _writer;
    private readonly ILogger<TraceBackgroundService> _logger;

    public TraceBackgroundService(
        TraceQueue queue,
        QueryTraceWriter writer,
        ILogger<TraceBackgroundService> logger)
    {
        _queue = queue;
        _writer = writer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _queue.Reader;

        await foreach (QueryTrace trace in reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await _writer.WriteAsync(trace, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or System.Text.Json.JsonException
                or NotSupportedException
                or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Failed to write query trace {TraceId}", trace.TraceId);
            }
        }
    }
}
