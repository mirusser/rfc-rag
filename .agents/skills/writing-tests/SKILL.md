---
name: writing-tests
description: Apply when adding or modifying tests in rfc-rag. Covers test project structure, naming, InternalsVisibleTo for internal types, the no-mocks rule, and what to verify before and after.
---

# Writing Tests

## Test Project Structure

All tests live in a single project, `tests/RfcRag.Tests`, split by scope:

- `UnitTests/` — no network, no containers, no filesystem (or temp-path only).
- `IntegrationTests/` — may use Testcontainers (PostgreSQL; requires Docker) or in-process hosts.
- `Fakes/` — shared fakes and test doubles.
- `TestData/` — RFC fixtures and other static inputs.

Add new test files to the appropriate subdirectory.

## Assertion Surface

Prefer assertions on behavior contracts, not presentation text. When code returns a typed result, assert the stable fields first.

Do not assert user-facing prose unless the test is specifically about rendering, CLI/protocol output, or a documented wire contract. If parsing is unavoidable, parse by format (URL path, JSON field) rather than by label text.

Use convention constants for contract strings.

## Naming

Follow `Method_State_ExpectedResult`:

```csharp
Validate_MissingEndpoint_Fails()
SearchAsync_EmptyQuery_ReturnsNoResults()
GetDelay_RetryAfterHeader_HonorsServerValue()
```

## InternalsVisibleTo

Most types in this repo are `internal`. `src/RfcRag/RfcRag.csproj` already exposes internals to the test project:

```xml
<InternalsVisibleTo Include="RfcRag.Tests" />
```

If a new source project is added, follow the same pattern; without it, tests referencing internal types fail at compile time with `CS0122: inaccessible due to its protection level`.

## Time

Never sleep or read wall-clock time in tests. Production code injects `TimeProvider`; tests use `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) for retries, backoff, and expiry behavior.

## Mocking

Never use mocking libraries (Moq, NSubstitute, or similar). Use the fakes in `Fakes/`, or write integration tests with Testcontainers instead.

## Standards/Conventions

See skill: [code-standards](../code-standards/SKILL.md)

## Verification

After adding or changing tests, run the narrowest useful test command:

```bash
dotnet test tests/RfcRag.Tests/RfcRag.Tests.csproj

# Filter by test name
dotnet test tests/RfcRag.Tests/RfcRag.Tests.csproj --filter "FullyQualifiedName~SearchService"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

All pre-existing tests must continue to pass. New tests must pass too — do not commit failing tests.

## Common Anti-Patterns

| Anti-Pattern | Fix |
|---|---|
| Testing implementation details | Test behavior and outcomes |
| Shared mutable test state | Fresh instance per test (xUnit does this via constructors) |
| `Thread.Sleep` / real delays in async tests | `FakeTimeProvider` and deterministic time control |
| Asserting on `ToString()` output | Assert on typed properties |
| One giant assertion per test | One logical assertion per test |
| Test names describing implementation | Name by behavior: `Method_State_ExpectedResult` |
| Ignoring `CancellationToken` | Always pass and verify cancellation |
