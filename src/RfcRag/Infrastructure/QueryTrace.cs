using System.Text.Json.Serialization;

namespace RfcRag.Infrastructure;

/// <summary>
/// Trace record for a single query through the ask-RFC pipeline.
/// Written as JSONL when <c>TraceDirectory</c> is configured.
/// </summary>
public sealed record class QueryTrace
{
    /// <summary>Unique trace identifier for correlating log lines.</summary>
    public required string TraceId { get; init; }

    /// <summary>The user's question.</summary>
    public required string Question { get; init; }

    /// <summary>When the trace was created (UTC).</summary>
    public required DateTime TimestampUtc { get; init; }

    /// <summary>Timed stages in the pipeline.</summary>
    public IReadOnlyList<TraceStage> Stages { get; init; } = [];

    /// <summary>RFC numbers of search candidates retrieved.</summary>
    public IReadOnlyList<int> CandidateRfcNumbers { get; init; } = [];

    /// <summary>Retrieval strategy metadata.</summary>
    public RetrievalInfo? Retrieval { get; init; }

    /// <summary>Whether an answer was generated.</summary>
    public bool AnswerGenerated { get; init; }

    /// <summary>Number of warnings produced in the answer.</summary>
    public int WarningCount { get; init; }
}

/// <summary>A named, timed stage in the query pipeline.</summary>
public sealed record class TraceStage
{
    /// <summary>Stage name (e.g., "search", "assemble", "generate").</summary>
    public required string Name { get; init; }

    /// <summary>Wall-clock start time (UTC).</summary>
    public required DateTime StartedAtUtc { get; init; }

    /// <summary>Wall-clock completion time (UTC), or <see langword="null"/> if the stage did not complete.</summary>
    public DateTime? CompletedAtUtc { get; init; }

    /// <summary>
    /// Duration computed from <see cref="CompletedAtUtc"/> - <see cref="StartedAtUtc"/>.
    /// <see langword="null"/> when the stage has not completed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TimeSpan? Duration => CompletedAtUtc is not null
        ? CompletedAtUtc.Value - StartedAtUtc
        : null;
}
