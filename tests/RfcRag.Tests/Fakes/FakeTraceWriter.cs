using RfcRag.Infrastructure;

namespace RfcRag.Tests.Fakes;

/// <summary>
/// Fake <see cref="QueryTraceWriter"/> that captures the most recently written
/// <see cref="QueryTrace"/> in memory instead of writing to disk.
/// </summary>
internal sealed class FakeTraceWriter : QueryTraceWriter
{
    /// <summary>The last trace passed to <see cref="WriteAsync"/>.</summary>
    public QueryTrace? LastTrace { get; private set; }

    public FakeTraceWriter()
        : base(
            Microsoft.Extensions.Options.Options.Create(new RfcRag.Settings.RfcRagOptions
            {
                RfcMirrorPath = "/tmp/test-mirror",
                PostgresConnectionString = "Host=localhost;Database=test",
                TraceDirectory = "/tmp/test-traces",
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryTraceWriter>.Instance)
    {
    }

    public override async Task WriteAsync(QueryTrace trace, CancellationToken cancellationToken = default)
    {
        LastTrace = trace;
        // Skip the base implementation to avoid disk I/O in tests.
        await Task.CompletedTask;
    }
}
