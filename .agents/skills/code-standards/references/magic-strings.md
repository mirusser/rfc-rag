# Magic Strings

Avoid introducing repeated or unexplained string literals in code.

Prefer named conventions when a string is:

- Repeated.
- Easy to mistype.
- Used for both declaration and invocation.
- Part of an external contract.
- Used in attributes.
- Used in serialization, persistence, routing, configuration, logging, metrics, or integration code.

Examples of external-contract strings:

- MCP tool names.
- JSON property names.
- Environment variable names.
- Configuration keys.
- HTTP paths.
- Header names.
- Vector store collection, index, and payload field names.
- CLI command names and option names.
- Audit event names.
- Metric names and tag names.
- Logger event names or IDs.
- Dependency injection keyed-service keys.
- Named HttpClient names.
- Feature flag names.
- Queue, topic, stream, or table names.

Choose the smallest suitable shape:

- Use const string for compile-time values, especially values used in attributes.
- Use static readonly only when the value cannot be a compile-time constant.
- Use nested static classes for related names when the project already groups conventions that way.
- Use small local convention classes for project-local contracts.
- Use shared convention classes only when the same contract is intentionally shared across projects.
- Use enums only when serialization, persistence, and display text are either not involved or explicitly handled.
- Use strongly typed IDs or value objects only when they clarify behavior, validation, or domain meaning.
- Keep one-off user-facing sentences inline unless extraction improves clarity.

Do not extract literals only to satisfy a rule mechanically. Test data, examples, one-off exception messages, and simple user-facing text can stay inline when extraction would reduce readability.

When replacing existing literals, preserve spelling, casing, wire names, route names, JSON names, environment variable names, metric names, and other public contracts.
