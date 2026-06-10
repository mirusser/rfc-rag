# Logging

Use `ILogger<T>`.

Use structured logging message templates. Never use string interpolation in log calls.

```csharp
// Correct
logger.LogInformation(
    "Indexed RFC {RfcNumber} into {ChunkCount} chunks",
    rfc.Number,
    chunks.Count);

// Avoid
logger.LogInformation($"Indexed RFC {rfc.Number} into {chunks.Count} chunks");
```

Use stable property names in log templates.

Do not log secrets, tokens, passwords, connection strings, API keys, private keys, authorization headers, or sensitive resource data.

For high-frequency logging paths, use the `[LoggerMessage]` source generator.

Keep event IDs and event names stable when they are consumed by dashboards, alerts, or tests.
