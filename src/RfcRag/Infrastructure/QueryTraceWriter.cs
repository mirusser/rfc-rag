using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RfcRag.Settings;

namespace RfcRag.Infrastructure;

/// <summary>
/// Writes per-query <see cref="QueryTrace"/> records as JSONL to
/// daily-rotated files. No-op when <c>RfcRag__TraceDirectory</c> is not set.
/// Fail-open: warnings are logged but the writer never throws.
/// </summary>
internal sealed class QueryTraceWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly string? _traceDirectory;
    private readonly ILogger<QueryTraceWriter> _logger;
    private readonly TimeProvider _timeProvider;

    public QueryTraceWriter(IOptions<RfcRagOptions> options, ILogger<QueryTraceWriter> logger, TimeProvider timeProvider)
    {
        _traceDirectory = string.IsNullOrWhiteSpace(options.Value.TraceDirectory)
            ? null
            : options.Value.TraceDirectory;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>Trace directory configured, or <see langword="null"/> when tracing is disabled.</summary>
    internal string? TraceDirectory => _traceDirectory;

    /// <summary>
    /// Writes a <see cref="QueryTrace"/> as one JSONL line to the daily-rotated file.
    /// When <see cref="TraceDirectory"/> is not configured, this is a no-op.
    /// On I/O failure, a warning is logged and the exception is swallowed.
    /// </summary>
    public async Task WriteAsync(QueryTrace trace, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_traceDirectory))
            return;

        try
        {
            string filePath = GetFilePath();
            string dir = Path.GetDirectoryName(filePath)!;
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(trace, JsonOptions);
            await File.AppendAllTextAsync(filePath, json + Environment.NewLine, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to write query trace for trace ID {TraceId}", trace.TraceId);
        }
    }

    private string GetFilePath()
    {
        string date = _timeProvider.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Path.Join(_traceDirectory!, $"rfc-rag-trace-{date}.jsonl");
    }
}
