---
name: code-standards
description: Apply this repository's .NET coding standards when an AI coding assistant makes, reviews, or refactors code. Use for code edits, cleanup, reviews, convention work, and behavior-preserving modernization, especially to avoid repeated or unexplained magic strings and to keep changes small, testable, and consistent with the repository.
---

# Code Standards

## Overview

Apply these standards alongside the repository's own instructions: [AGENTS.md](../../../AGENTS.md), [README.md](../../../README.md), [.editorconfig](../../../.editorconfig), analyzer configuration, and existing project conventions.

Prefer the existing repository conventions.

Each section below states the core rule. Follow the `reference:` link for details, examples, and edge cases when the change touches that area.

### Precedence

When standards conflict, use this order:

1. Explicit user request.
2. Repository instructions such as AGENTS.md.
3. .editorconfig, analyzers, nullable settings, warning settings, and build configuration.
4. Existing local style in the file or project.
5. This document.
6. General .NET conventions (from new/official docs).

## Agent Workflow

### Before editing

Inspect nearby code and project conventions.
Identify whether the change affects public contracts, serialization, persistence, CLI behavior, HTTP routes, MCP tool names or schemas, configuration keys, environment variables, logs, metrics, or external integrations.
Prefer the smallest change that solves the problem.
Avoid speculative abstractions.

### While editing

Preserve behavior and public contracts unless explicitly asked to change them.
Pass CancellationToken through async I/O and external calls.
Keep visibility as narrow as possible.
Add or update tests for changed behavior.
Do not silence analyzers instead of fixing the issue.

### After editing

Run the narrowest useful tests or build command when available.
Check the change against the [review checklist](./references/review-checklist.md).
Report what was changed, what was verified, and anything not verified.
Call out behavior changes, contract changes, new dependencies, or migration risks.

## Magic Strings

Avoid repeated or unexplained string literals.

reference: [magic-strings.md](./references/magic-strings.md)

## Scope of Abstractions

Keep conventions local to the project unless there is already a shared project or the same contract is intentionally shared across projects. Avoid creating shared abstractions only to remove a small amount of duplication.

reference: [scope-of-abstractions.md](./references/scope-of-abstractions.md)

## Naming

Lower camel case for private fields, no `_` prefix: `private readonly JsonSerializerOptions jsonOptions;` Align fields you edit with this convention; do not churn unrelated fields.

Name booleans as questions: `IsReady`, `HasFailed`, `CanRetry`. Avoid negated names like `IsNotReady` or `NoCache`.

reference: [naming.md](./references/naming.md)

## Type Organization

One meaningful top-level type per file. Split files when types are not tightly coupled. Multiple types in one file are acceptable only for tiny implementation details bound to the primary type. No broad "grab bag" files. No `#region` — if a file needs them, split it instead.

reference: [type-organization.md](./references/type-organization.md)

## Formatting

Match surrounding code exactly. No column-aligned spacing. File-scoped namespaces (`namespace Foo.Bar;`) in all new files. `global using` directives go in a single `GlobalUsings.cs` per project.

reference: [formatting.md](./references/formatting.md)

## Async and Cancellation

Async methods end in `Async`. Pass `CancellationToken` through all async I/O and external calls. Call `ConfigureAwait(false)` on awaited tasks in library/tool code. No `async void` outside event handlers. Do not block on async code.

reference: [async-and-cancellation.md](./references/async-and-cancellation.md)

## Dependency Injection

Constructor injection for dependencies; options records for configuration. No service locator or direct `IServiceProvider` use outside composition roots, factories, or framework integration code.

reference: [dependency-injection.md](./references/dependency-injection.md)

## Collections

Prefer `IReadOnlyList<T>` / `IReadOnlyDictionary<K,V>` on public and internal API surfaces. Use frozen collections for long-lived, read-heavy lookup tables. Always specify a `StringComparer` when string-key casing matters.

reference: [collections.md](./references/collections.md)

## Primary Constructors

Use primary constructors where they reduce boilerplate. Prefer explicit constructors when validation, normalization, or multiple construction paths are involved.

reference: [primary-constructors.md](./references/primary-constructors.md)

## Records and DTOs

Use `record` or `record struct` for immutable value objects, DTOs, and configuration snapshots. Prefer positional records for small types. Avoid adding behavior beyond simple accessors — if a type needs methods, use a `class`. Keep DTOs/contracts separate from behavior-heavy services when they grow.

reference: [records-and-dtos.md](./references/records-and-dtos.md)

## JSON and Serialization

Prefer `System.Text.Json`. Reuse `JsonSerializerOptions` instances. Keep JSON property names, discriminators, and wire contracts stable.

reference: [json-and-serialization.md](./references/json-and-serialization.md)

## HTTP Clients and Resilience

Use `IHttpClientFactory`; prefer typed clients. Use resilience pipelines instead of hand-written retry loops, and be careful retrying non-idempotent operations.

reference: [http-clients-and-resilience.md](./references/http-clients-and-resilience.md)

## Structured Logging

Use `ILogger<T>` with message templates. Never use string interpolation in log calls. For high-frequency paths, use the `[LoggerMessage]` source generator. Never log secrets.

reference: [logging.md](./references/logging.md)

## Metrics and Observability

Use `System.Diagnostics.Metrics` with OpenTelemetry-compatible instrumentation. Keep metric names, units, and tag names stable and in constants. Choose low-cardinality tags.

reference: [metrics-and-observability.md](./references/metrics-and-observability.md)

## Time and Timers

Do not call `DateTime.Now`/`DateTime.UtcNow` (or the `DateTimeOffset` equivalents) directly in testable services. Inject `TimeProvider` and use fake time in tests.

reference: [time-timers-deterministic-tests.md](./references/time-timers-deterministic-tests.md)

## Resilience and Chaos Testing

Add retries, timeouts, circuit breakers, and fallbacks deliberately, with tests covering failure paths. Chaos testing only in controlled environments behind explicit opt-in.

reference: [resilience-chaos-testing.md](./references/resilience-chaos-testing.md)

## Experimental and Specialized APIs

Do not introduce experimental or preview APIs casually. When required, isolate them behind a small abstraction, document why, and add tests.

reference: [experimental-and-specialized-apis.md](./references/experimental-and-specialized-apis.md)

## Comments

Write comments that explain why, not what. Remove obsolete comments.

reference: [comments.md](./references/comments.md)

## var

Use `var` when the type is obvious from the right-hand side (e.g. `new`, casts, named factories). Use explicit types when the initializer doesn't make the type clear. Never use `var` for primitives (`int`, `bool`, `string`).

## Exception Handling

Catch specific exceptions, not `Exception`, except at top-level boundaries. Do not swallow exceptions silently. Use static guard helpers: `ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrEmpty`, `ArgumentOutOfRangeException.ThrowIfNegativeOrZero`. Do not use exceptions for control flow.

## Pattern Matching

Prefer `is` patterns over `as`-casts with null checks. Prefer `switch` expressions for exhaustive dispatch. Avoid hard `(T)obj` casts without a clear invariant.

## Other .NET Norms

- Keep public surface minimal; prefer `internal` unless cross-project use is intentional.
- Use nullable reference types honestly; avoid `!` without a clear invariant.
- Use `using var` declarations unless early disposal is needed.
- Mark classes `sealed` by default; only leave them open when subclassing is intentional.

## Analyzer and Build Hygiene

Respect existing `.editorconfig`, analyzer, nullable, and warning settings. Do not silence analyzer warnings.

Do not introduce broad `NoWarn`, disabled nullable contexts, or project-wide analyzer changes as part of a local code edit.

## Tests

One test class per production class, named `{TypeUnderTest}Tests`. Name tests `Method_State_ExpectedResult`. Use `[Theory]` with `[InlineData]` or `[MemberData]` over duplicated `[Fact]` tests. No shared mutable state between test cases. Assert on observable outputs, not implementation details.

For test project structure, `InternalsVisibleTo`, and the verification workflow, see the [writing-tests skill](../writing-tests/SKILL.md).
