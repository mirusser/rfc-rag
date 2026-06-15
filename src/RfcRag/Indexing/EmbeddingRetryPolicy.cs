using System.ClientModel;
using System.Security.Cryptography;

namespace RfcRag.Indexing;

internal sealed class EmbeddingRetryPolicy(TimeProvider timeProvider)
{
    internal const int MaxAttempts = 3;
    private const double BaseDelaySeconds = 1.0;
    private const double MaxDelaySeconds = 30.0;
    private const string RetryAfterHeader = "Retry-After";

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Action<int, Exception, TimeSpan>? onRetrying,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            // TaskCanceledException inherits from OperationCanceledException.
            // User-initiated cancellation (IsCancellationRequested=true) rethrows here;
            // transport-level TCEs (timeout, not user-requested) fall through to IsRetryable below.
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ClientResultException ex) when (!IsRetryableStatus(ex.Status))
            {
                throw;
            }
            catch (Exception ex) when (IsRetryable(ex))
            {
                if (attempt >= MaxAttempts - 1)
                    throw;

                double delaySeconds = GetDelay(ex, attempt);
                var delay = TimeSpan.FromSeconds(delaySeconds);
                onRetrying?.Invoke(attempt + 1, ex, delay);
                await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException($"Operation failed after {MaxAttempts} retry attempts.");
    }

    internal static bool IsRetryable(Exception ex) => ex switch
    {
        ClientResultException cre => IsRetryableStatus(cre.Status),
        HttpRequestException => true,
        IOException => true,
        TaskCanceledException => true,
        _ => false
    };

    internal static bool IsRetryableStatus(int status) =>
        status == 429 || status == 408 || (status is >= 500 and <= 599);

    private double GetDelay(Exception ex, int attempt)
    {
        if (ex is ClientResultException { Status: 429 } cre)
        {
            double? retryAfter = TryGetRetryAfterSeconds(cre);
            if (retryAfter is double retryAfterSeconds)
                return Math.Min(retryAfterSeconds, MaxDelaySeconds);
        }

        double cap = Math.Min(MaxDelaySeconds, BaseDelaySeconds * Math.Pow(2, attempt));
        // Non-security jitter: full jitter for backoff spread, not for cryptographic use
        return cap * RandomNumberGenerator.GetInt32(0, 10001) / 10000.0;
    }

    private double? TryGetRetryAfterSeconds(ClientResultException ex)
    {
        var response = ex.GetRawResponse();
        if (response is null) return null;

        if (response.Headers.TryGetValue(RetryAfterHeader, out string? value) && value is not null)
        {
            if (double.TryParse(value, out double seconds))
                return seconds;

            if (DateTimeOffset.TryParse(value, out DateTimeOffset retryDate))
            {
                double delay = (retryDate - timeProvider.GetUtcNow()).TotalSeconds;
                return delay > 0 ? delay : 0;
            }
        }

        return null;
    }
}
