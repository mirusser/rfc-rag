using Microsoft.Extensions.Time.Testing;
using RfcRag.Indexing;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace RfcRag.Tests.UnitTests;

public sealed class EmbeddingRetryPolicyTests
{
    [Theory]
    [InlineData(429, true)]
    [InlineData(408, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(599, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    [InlineData(200, false)]
    public void IsRetryableStatus_VariousStatusCodes_CorrectlyClassified(int status, bool expected)
    {
        Assert.Equal(expected, EmbeddingRetryPolicy.IsRetryableStatus(status));
    }

    [Fact]
    public void IsRetryable_HttpRequestException_ReturnsTrue()
    {
        Assert.True(EmbeddingRetryPolicy.IsRetryable(new HttpRequestException()));
    }

    [Fact]
    public void IsRetryable_IOException_ReturnsTrue()
    {
        Assert.True(EmbeddingRetryPolicy.IsRetryable(new IOException()));
    }

    [Fact]
    public void IsRetryable_TaskCanceledException_ReturnsTrue()
    {
        Assert.True(EmbeddingRetryPolicy.IsRetryable(new TaskCanceledException()));
    }

    [Fact]
    public void IsRetryable_ArgumentException_ReturnsFalse()
    {
        Assert.False(EmbeddingRetryPolicy.IsRetryable(new ArgumentException()));
    }

    [Fact]
    public async Task ExecuteAsync_RateLimitedWithRetryAfter_DelaysRequestedInterval()
    {
        var fakeTime = new FakeTimeProvider();
        var policy = new EmbeddingRetryPolicy(fakeTime);

        int callCount = 0;
        TimeSpan? observedDelay = null;
        var rateLimitResponse = new FakePipelineResponse(429, "Retry-After: 7");

        var task = policy.ExecuteAsync(
            async ct =>
            {
                callCount++;
                await Task.CompletedTask;
                if (callCount == 1)
                    throw new ClientResultException("rate limited", rateLimitResponse);
                return "ok";
            },
            (attempt, ex, delay) => observedDelay = delay,
            CancellationToken.None);

        // Advance time past the Retry-After delay to unblock Task.Delay
        fakeTime.Advance(TimeSpan.FromSeconds(10));

        string result = await task;

        Assert.Equal("ok", result);
        Assert.Equal(2, callCount);
        Assert.NotNull(observedDelay);
        Assert.Equal(7.0, observedDelay!.Value.TotalSeconds, tolerance: 0.01);
    }

    [Fact]
    public async Task ExecuteAsync_FatalStatus_DoesNotRetry()
    {
        var policy = new EmbeddingRetryPolicy(TimeProvider.System);
        int callCount = 0;
        var badRequestResponse = new FakePipelineResponse(400, string.Empty);

        await Assert.ThrowsAsync<ClientResultException>(async () =>
        {
            await policy.ExecuteAsync(
                async ct =>
                {
                    callCount++;
                    await Task.CompletedTask;
                    throw new ClientResultException("bad request", badRequestResponse);
#pragma warning disable CS0162
                    return "unreachable";
#pragma warning restore CS0162
                },
                null,
                CancellationToken.None);
        });

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_PropagatesImmediately()
    {
        var policy = new EmbeddingRetryPolicy(TimeProvider.System);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await policy.ExecuteAsync(
                ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult("unreachable");
                },
                null,
                cts.Token);
        });
    }

    [Fact]
    public async Task ExecuteAsync_TransientError_RetriesAndSucceeds()
    {
        var fakeTime = new FakeTimeProvider();
        var policy = new EmbeddingRetryPolicy(fakeTime);
        int callCount = 0;

        var task = policy.ExecuteAsync(
            async ct =>
            {
                callCount++;
                await Task.CompletedTask;
                if (callCount < 3)
                    throw new HttpRequestException("transient");
                return "ok";
            },
            null,
            CancellationToken.None);

        // Advance time to unblock the first backoff delay
        fakeTime.Advance(TimeSpan.FromSeconds(60));
        // First retry fired — the operation threw again, registering a second delay.
        // Advance again to unblock the second backoff delay.
        fakeTime.Advance(TimeSpan.FromSeconds(60));

        string result = await task;

        Assert.Equal("ok", result);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_RetriesExhausted_ThrowsException()
    {
        var fakeTime = new FakeTimeProvider();
        var policy = new EmbeddingRetryPolicy(fakeTime);
        int callCount = 0;

        var task = policy.ExecuteAsync<string>(
            async ct =>
            {
                callCount++;
                await Task.CompletedTask;
                throw new HttpRequestException("always fails");
            },
            null,
            CancellationToken.None);

        fakeTime.Advance(TimeSpan.FromSeconds(60));
        fakeTime.Advance(TimeSpan.FromSeconds(60));

        await Assert.ThrowsAsync<HttpRequestException>(async () => await task);

        Assert.Equal(3, callCount);
    }
}

file sealed class FakePipelineResponse : PipelineResponse
{
    private readonly Dictionary<string, string> headers;

    public FakePipelineResponse(int status, string retryAfterHeader)
    {
        Status = status;
        headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(retryAfterHeader))
        {
            int colon = retryAfterHeader.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
                headers[retryAfterHeader[..colon].Trim()] = retryAfterHeader[(colon + 1)..].Trim();
        }
    }

    public override int Status { get; }
    public override string ReasonPhrase => string.Empty;
    public override Stream? ContentStream { get; set; }
    public override BinaryData Content => BinaryData.Empty;
    protected override PipelineResponseHeaders HeadersCore => new FakePipelineResponseHeaders(headers);
    public override void Dispose() { }
    public override BinaryData BufferContent(CancellationToken cancellationToken = default) => BinaryData.Empty;
    public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => new(BinaryData.Empty);
}

file sealed class FakePipelineResponseHeaders(Dictionary<string, string> headers) : PipelineResponseHeaders
{
    public override bool TryGetValue(string name, out string? value) =>
        headers.TryGetValue(name, out value);

    public override bool TryGetValues(string name, out IEnumerable<string>? values)
    {
        if (headers.TryGetValue(name, out string? v))
        {
            values = [v];
            return true;
        }

        values = null;
        return false;
    }

    public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
        headers.GetEnumerator();
}
