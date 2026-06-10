# Primary Constructors

Use primary constructors when they reduce boilerplate and keep the type easy to read.

Good candidates:

- Small services with constructor-injected dependencies.
- Simple immutable types.
- Small test fixtures.
- Records and record structs.

For classes, remember that primary constructor parameters are parameters in scope throughout the type body. They are not automatically properties.

```csharp
internal sealed class RfcReader(IRfcSource source, ILogger<RfcReader> logger)
{
    public Task<IReadOnlyList<RfcDocument>> ReadAsync(CancellationToken cancellationToken)
        => source.ReadDocumentsAsync(cancellationToken);
}
```

For records, primary constructor parameters become positional properties.

```csharp
internal sealed record ToolName(string Value);
```

Avoid primary constructors when:

- Validation or normalization would be clearer in an explicit constructor.
- The type has multiple construction paths.
- Captured parameters make state unclear.
- The surrounding project has not adopted the style.