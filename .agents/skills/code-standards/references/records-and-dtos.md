# Records and DTOs

Use record or record struct for immutable value objects, DTOs, messages, options snapshots, and simple data carriers.

Prefer positional records for small types.

```csharp
internal sealed record RfcChunkId(int RfcNumber, string Section, int Index);
```

Use nominal records with properties when the type has many fields, optional fields, defaults, or serialization attributes.

Avoid adding behavior-heavy methods to records. If a type owns complex behavior, use a class.

Be careful with records used for serialization or persistence. Preserve wire names and constructor binding behavior.