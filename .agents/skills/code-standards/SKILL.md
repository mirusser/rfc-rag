---
name: code-standards
description: Apply this repository's .NET coding standards when an AI coding assistant makes, reviews, or refactors code. Use for code edits, cleanup, reviews, convention work, and behavior-preserving modernization, especially to avoid repeated or unexplained magic strings and to keep changes small, testable, and consistent with the repository.
---

# Code Standards

## Overview

Apply these standards alongside [AGENTS.md](../../../AGENTS.md), [README.md](../../../README.md), [.editorconfig](../../../.editorconfig), analyzer configuration, and existing project conventions. AGENTS.md governs general working style (simplicity, surgical changes, verification); this skill adds the .NET-specific and repository-specific rules.

Each section states the core rule. Follow the `reference:` link for details and edge cases when the change touches that area.

### Precedence

When standards conflict, use this order:

1. Explicit user request.
2. Repository instructions such as AGENTS.md.
3. .editorconfig, analyzers, nullable settings, warning settings, and build configuration.
4. Existing local style in the file or project.
5. This document.
6. General .NET conventions (from new/official docs).

## Repository-Specific Standards

Rules established in this codebase that cannot be inferred from general .NET knowledge:

- MCP tools return `Task<CallToolResult>` (see `src/RfcRag/Tools/RfcRagTools.cs`), never raw strings or ad-hoc result DTOs.
- Options are validated by `IValidateOptions<T>` validators registered with `.ValidateOnStart()` (see `RfcRagOptionsValidator`). Add new option rules there, not as scattered runtime checks.
- Transient-failure retries follow the `EmbeddingRetryPolicy` pattern: a small, tested, hand-rolled policy with bounded exponential backoff, full jitter, `Retry-After` support, and an injected `TimeProvider`. Do not add a resilience library for this without discussion.
- All projects are CLI/MCP-server code with no UI `SynchronizationContext` — apply `ConfigureAwait(false)` throughout.
- Tests use xUnit v3 with no mocking libraries (fakes and Testcontainers instead) and `FakeTimeProvider` for time-dependent behavior — see the [writing-tests skill](../writing-tests/SKILL.md).

When a user correction establishes a new convention, record it in `.agents/lessons.md`; promote it into this section once it has fired repeatedly (see AGENTS.md).

## Contract Surfaces

Before editing, identify whether the change affects a public contract: serialization or persisted data, CLI behavior, HTTP routes, MCP tool names or schemas, configuration keys, environment variables, log event names, or metric names. Preserve these unless explicitly asked to change them, and call out behavior changes, contract changes, new dependencies, or migration risks when reporting.

After editing, check the change against the [review checklist](./references/review-checklist.md).

## Magic Strings

Avoid repeated or unexplained string literals. Extraction is a judgment call: name strings that are repeated, easy to mistype, or part of an external contract; leave one-off test data and user-facing sentences inline.

reference: [magic-strings.md](./references/magic-strings.md)

## Scope of Abstractions

Keep conventions local to the project unless the same contract is intentionally shared across projects. No grab-bag utility classes.

reference: [scope-of-abstractions.md](./references/scope-of-abstractions.md)

## Naming

Lower camel case for private fields, no `_` prefix: `private readonly JsonSerializerOptions jsonOptions;` Align fields you edit with this convention; do not churn unrelated fields.

Name booleans as questions: `IsReady`, `HasFailed`, `CanRetry`. Avoid negated names like `IsNotReady` or `NoCache`.

reference: [naming.md](./references/naming.md)

## Type Organization

One meaningful top-level type per file. No broad "grab bag" files (`Helpers.cs`, `Common.cs`). No `#region` — if a file needs them, split it instead.

reference: [type-organization.md](./references/type-organization.md)

## Formatting

No column-aligned spacing. File-scoped namespaces in all new files. `global using` directives go in a single `GlobalUsings.cs` per project.

reference: [formatting.md](./references/formatting.md)

## var

Use `var` when the type is obvious from the right-hand side (e.g. `new`, casts, named factories). Use explicit types when the initializer doesn't make the type clear. Never use `var` for primitives (`int`, `bool`, `string`).

## Async and Cancellation

Pass `CancellationToken` through all async I/O and external calls — including embedding and vector store calls. `ConfigureAwait(false)` on awaited tasks (see Repository-Specific Standards).

reference: [async-and-cancellation.md](./references/async-and-cancellation.md)

## Dependency Injection

Options records for configuration. Keyed-service keys live in constants, never repeated as raw strings across registration and resolution. No service locator outside composition roots.

reference: [dependency-injection.md](./references/dependency-injection.md)

## Collections

`IReadOnlyList<T>` / `IReadOnlyDictionary<K,V>` on public and internal API surfaces. `FrozenSet<T>` / `FrozenDictionary<K,V>` for long-lived lookup tables (normative keywords, tool names). Always specify a `StringComparer` when string-key casing matters.

reference: [collections.md](./references/collections.md)

## Primary Constructors

Use primary constructors where they reduce boilerplate; switch to explicit constructors when validation, normalization, or multiple construction paths appear.

reference: [primary-constructors.md](./references/primary-constructors.md)

## Records and DTOs

Records stay behavior-free — if a type needs methods beyond simple accessors, use a `class`. Positional records for small types. Keep DTOs/contracts separate from behavior-heavy services when they grow.

reference: [records-and-dtos.md](./references/records-and-dtos.md)

## JSON and Serialization

Reuse `JsonSerializerOptions` instances — never create them per call. Keep JSON property names, discriminators, and wire contracts stable.

reference: [json-and-serialization.md](./references/json-and-serialization.md)

## HTTP Clients and Resilience

Outbound HTTP goes through `IHttpClientFactory`-managed clients with names in constants. Retries follow the repository retry pattern (see Repository-Specific Standards); be careful retrying non-idempotent operations.

reference: [http-clients-and-resilience.md](./references/http-clients-and-resilience.md)

## Structured Logging

Message templates, never string interpolation, in log calls. `[LoggerMessage]` source generator on high-frequency paths. Never log secrets.

reference: [logging.md](./references/logging.md)

## Metrics and Observability

Metric names, units, and tag names are stable contracts kept in constants. Low-cardinality tags only.

reference: [metrics-and-observability.md](./references/metrics-and-observability.md)

## Time and Timers

No direct `DateTime.Now`/`UtcNow` (or `DateTimeOffset` equivalents) in testable services — inject `TimeProvider`; test with `FakeTimeProvider`.

reference: [time-timers-deterministic-tests.md](./references/time-timers-deterministic-tests.md)

## Resilience Testing

Retry/timeout code is tested across success, timeout, retryable failure, non-retryable failure, and cancellation.

reference: [resilience-testing.md](./references/resilience-testing.md)

## Exception Handling

Catch specific exceptions, not `Exception`, except at top-level boundaries; never swallow silently. Use the static guard helpers (`ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrEmpty`, `ArgumentOutOfRangeException.ThrowIfNegativeOrZero`) instead of hand-written guard blocks.

## Other .NET Norms

- Keep public surface minimal; prefer `internal` unless cross-project use is intentional.
- Use nullable reference types honestly; avoid `!` without a clear invariant.
- Use `using var` declarations unless early disposal is needed.
- Mark classes `sealed` by default; only leave them open when subclassing is intentional.
- Do not introduce preview or experimental APIs without an explicit requirement.

## Analyzer and Build Hygiene

Do not silence analyzer warnings; fix them. No broad `NoWarn`, disabled nullable contexts, or project-wide analyzer changes as part of a local code edit.

## Tests

One test class per production class, named `{TypeUnderTest}Tests`. Name tests `Method_State_ExpectedResult`. Use `[Theory]` with `[InlineData]` or `[MemberData]` over duplicated `[Fact]` tests. Assert on observable outputs, not implementation details.

For project structure, `InternalsVisibleTo`, fakes, and verification, see the [writing-tests skill](../writing-tests/SKILL.md).
