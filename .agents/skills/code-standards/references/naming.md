# Naming

Use clear names that describe behavior, role, or contract.

## General naming

- PascalCase for types, methods, properties, events, and public constants.
- lower camel case for parameters, local variables, and private fields.
- Private fields use no `_` prefix.

```csharp
private readonly JsonSerializerOptions jsonOptions;
```

Avoid abbreviations unless they are established domain terms in the repository.

## Boolean members

Boolean members should read as questions or capabilities:

```csharp
IsReady
HasFailed
CanRetry
ShouldRetry
SupportsStreaming
```

Avoid negated boolean names:

```csharp
// Avoid
IsNotReady
NoCache
DisableValidation
```

Use positive names where possible:

```csharp
IsReady
UseCache
ValidateInput
```

## Async methods

Async methods end in `Async`.

```csharp
Task SaveAsync(CancellationToken cancellationToken)
```
