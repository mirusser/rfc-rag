# Time, Timers, and Deterministic Tests

Do not call DateTime.Now, DateTime.UtcNow, DateTimeOffset.Now, or DateTimeOffset.UtcNow directly inside testable services.

Prefer injecting TimeProvider.

```csharp
internal sealed class RetryDeadline(TimeProvider timeProvider)
{
    public bool IsExpired(DateTimeOffset expiresAt)
        => timeProvider.GetUtcNow() >= expiresAt;
}
```

Use `TimeProvider.System` at composition boundaries.

Use fake time in tests when testing expiration, retries, timeouts, scheduling, delays, or timers.

Prefer TimeProvider.CreateTimer / ITimer over raw timers when timer behavior must be testable.