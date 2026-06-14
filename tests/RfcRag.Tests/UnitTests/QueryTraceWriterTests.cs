using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RfcRag.Infrastructure;
using RfcRag.Settings;

namespace RfcRag.Tests.UnitTests;

public sealed class QueryTraceWriterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly RfcRagOptions OptionsWithTrace = new()
    {
        RfcMirrorPath = "/tmp/test-mirror",
        PostgresConnectionString = "Host=localhost;Database=test",
        TraceDirectory = "/tmp/test-traces",
    };

    private static readonly RfcRagOptions OptionsWithoutTrace = new()
    {
        RfcMirrorPath = "/tmp/test-mirror",
        PostgresConnectionString = "Host=localhost;Database=test",
        TraceDirectory = null,
    };

    [Fact]
    public async Task WriteAsync_TraceDirectorySet_WritesJsonlFile()
    {
        string tempDir = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var options = Options.Create(OptionsWithTrace with { TraceDirectory = tempDir });
            var writer = new QueryTraceWriter(options, NullLogger<QueryTraceWriter>.Instance, TimeProvider.System);

            var trace = new QueryTrace
            {
                TraceId = "test-1",
                Question = "How does HTTP work?",
                TimestampUtc = DateTime.UtcNow,
                Stages =
                [
                    new TraceStage { Name = "search", StartedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow },
                ],
                CandidateRfcNumbers = [9110, 7230],
                AnswerGenerated = true,
            };

            await writer.WriteAsync(trace, TestContext.Current.CancellationToken);

            string date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string filePath = Path.Join(tempDir, $"rfc-rag-trace-{date}.jsonl");
            Assert.True(File.Exists(filePath), "Trace file should exist");

            string content = await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken);
            Assert.NotEmpty(content);
            Assert.EndsWith(Environment.NewLine, content);

            var deserialized = JsonSerializer.Deserialize<QueryTrace>(content.TrimEnd(), JsonOptions);
            Assert.NotNull(deserialized);
            Assert.Equal("test-1", deserialized.TraceId);
            Assert.Equal("How does HTTP work?", deserialized.Question);
            Assert.Single(deserialized.Stages);
            Assert.Equal("search", deserialized.Stages[0].Name);
            Assert.Equal([9110, 7230], deserialized.CandidateRfcNumbers);
            Assert.True(deserialized.AnswerGenerated);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_TraceDirectoryNull_DoesNotCreateFile()
    {
        var options = Options.Create(OptionsWithoutTrace);
        var writer = new QueryTraceWriter(options, NullLogger<QueryTraceWriter>.Instance, TimeProvider.System);

        var trace = new QueryTrace
        {
            TraceId = "test-2",
            Question = "Test?",
            TimestampUtc = DateTime.UtcNow,
        };

        await writer.WriteAsync(trace, TestContext.Current.CancellationToken);

        Assert.Null(writer.TraceDirectory);
    }

    [Fact]
    public async Task WriteAsync_TraceDirectoryEmpty_DoesNotCreateFile()
    {
        var options = Options.Create(OptionsWithTrace with { TraceDirectory = "" });
        var writer = new QueryTraceWriter(options, NullLogger<QueryTraceWriter>.Instance, TimeProvider.System);

        var trace = new QueryTrace
        {
            TraceId = "test-3",
            Question = "Test?",
            TimestampUtc = DateTime.UtcNow,
        };

        await writer.WriteAsync(trace, TestContext.Current.CancellationToken);
        Assert.Null(writer.TraceDirectory);
    }

    [Fact]
    public async Task WriteAsync_InvalidPath_DoesNotThrow()
    {
        var options = Options.Create(OptionsWithTrace with
        {
            TraceDirectory = "/dev/null/nope",
        });
        var writer = new QueryTraceWriter(options, NullLogger<QueryTraceWriter>.Instance, TimeProvider.System);

        var trace = new QueryTrace
        {
            TraceId = "test-4",
            Question = "Test?",
            TimestampUtc = DateTime.UtcNow,
        };

        var ex = await Record.ExceptionAsync(() =>
            writer.WriteAsync(trace, TestContext.Current.CancellationToken));
        Assert.Null(ex);
    }

    [Fact]
    public async Task WriteAsync_AppendsToExistingFile()
    {
        string tempDir = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var options = Options.Create(OptionsWithTrace with { TraceDirectory = tempDir });
            var writer = new QueryTraceWriter(options, NullLogger<QueryTraceWriter>.Instance, TimeProvider.System);

            var trace1 = new QueryTrace
            {
                TraceId = "trace-a",
                Question = "Q1?",
                TimestampUtc = DateTime.UtcNow,
            };

            var trace2 = new QueryTrace
            {
                TraceId = "trace-b",
                Question = "Q2?",
                TimestampUtc = DateTime.UtcNow,
            };

            await writer.WriteAsync(trace1, TestContext.Current.CancellationToken);
            await writer.WriteAsync(trace2, TestContext.Current.CancellationToken);

            string date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string filePath = Path.Join(tempDir, $"rfc-rag-trace-{date}.jsonl");
            string content = await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken);

            string[] lines = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_UsesTimeProviderForDateRotation_FileNameReflectsFakeDate()
    {
        string tempDir = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var fakeTime = new FakeTimeProvider();
            fakeTime.SetUtcNow(new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero));

            var options = Options.Create(OptionsWithTrace with { TraceDirectory = tempDir });
            var writer = new QueryTraceWriter(options, NullLogger<QueryTraceWriter>.Instance, fakeTime);

            var trace = new QueryTrace
            {
                TraceId = "date-test",
                Question = "Does the filename use the injected date?",
                TimestampUtc = DateTime.UtcNow,
            };

            await writer.WriteAsync(trace, TestContext.Current.CancellationToken);

            string expectedDate = "2025-01-15";
            string expectedFile = Path.Join(tempDir, $"rfc-rag-trace-{expectedDate}.jsonl");
            Assert.True(File.Exists(expectedFile),
                $"Expected trace file for {expectedDate} but it was not created.");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
