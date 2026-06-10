# Metrics and Observability

Use `System.Diagnostics.Metrics` for new application or library metrics.

Prefer OpenTelemetry-compatible instrumentation.

Keep metric names, units, and tag names stable.

Use constants for metric names and tag names.

Choose low-cardinality tags. Do not use unbounded values such as raw IDs, full URLs, full query text, exception messages, or user input as metric tags.

Prefer metrics for aggregate behavior and logs for individual events.

For new instrumentation, prefer modern metrics APIs over `EventCounters` unless maintaining existing EventCounter-based integrations.