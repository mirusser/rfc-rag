# Resilience and Chaos Testing

Use resilience policies deliberately. Do not add retries, timeouts, circuit breakers, or fallbacks without considering side effects and observability.

For resilience code, tests should cover:

* Success path.
* Timeout.
* Retryable failure.
* Non-retryable failure.
* Cancellation.
* Circuit-breaker or fallback behavior when applicable.
* Non-idempotent request behavior.

Use chaos testing only in controlled environments or behind explicit opt-in configuration.

Chaos experiments must have:

* Clear scope.
* Bounded blast radius.
* Observability.
* Fast rollback.
* Explicit enablement.
* No accidental production activation.