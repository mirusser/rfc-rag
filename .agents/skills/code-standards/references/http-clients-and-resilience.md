# HTTP Clients and Resilience

Use `IHttpClientFactory` for outbound HTTP clients.

Prefer typed clients for domain-specific dependencies.

Use named clients when the name is itself part of a convention. Keep names in constants.

Use `ConfigureHttpClientDefaults` for shared client defaults when the behavior should apply broadly.

Use resilience pipelines for transient-fault handling instead of hand-written retry loops.

Standard resilience concerns include:

- Retry.
- Timeout.
- Circuit breaker.
- Rate limiting.
- Hedging, when appropriate.
- Fallback, when appropriate.

Be careful retrying non-idempotent operations such as POST, PATCH, and some DELETE calls. Automatic retries can duplicate side effects.

Keep retry, timeout, and circuit-breaker settings explicit and observable.

Do not hide failures with broad fallback behavior unless the fallback is a deliberate product decision.