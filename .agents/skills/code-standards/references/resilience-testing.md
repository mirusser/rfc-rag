# Resilience Testing

Use resilience policies deliberately. Do not add retries, timeouts, circuit breakers, or fallbacks without considering side effects and observability.

For resilience code, tests should cover:

* Success path.
* Timeout.
* Retryable failure.
* Non-retryable failure.
* Cancellation.
* Backoff and `Retry-After` behavior when applicable.
* Non-idempotent request behavior.

Use `FakeTimeProvider` so backoff and timeout tests run deterministically without real delays.
