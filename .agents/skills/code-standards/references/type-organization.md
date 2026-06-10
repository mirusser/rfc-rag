# Type Organization

Use one meaningful top-level type per file.

Multiple types in one file are acceptable only when they are tiny implementation details tightly bound to the primary type, such as a private nested-like helper, tiny result type, or test-only fixture.

Avoid broad files such as:

- Helpers.cs
- Constants.cs
- Common.cs
- Extensions.cs

Prefer feature-specific names:

- McpToolNames.cs
- HttpHeaderNames.cs
- NormativeKeywords.cs
- RfcIndexer.cs

Keep DTOs and contracts separate from behavior-heavy services when they grow.