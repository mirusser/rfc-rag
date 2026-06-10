# Collections

Choose collection types based on ownership and mutation:

* `List<T>` for internal mutable lists.
* `Dictionary<TKey,TValue>` for internal mutable maps.
* `IReadOnlyList<T>` / `IReadOnlyDictionary<TKey,TValue>` for read-only API surfaces.
* Immutable collections when callers need safe snapshots or functional updates.
* `FrozenSet<T>` / `FrozenDictionary<TKey,TValue>` for collections created rarely and read frequently over a long lifetime.

Use `FrozenSet<T>` and `FrozenDictionary<TKey,TValue>` for long-lived lookup tables such as:

* RFC 2119 normative keywords (MUST, MUST NOT, SHOULD, ...).
* MCP tool names.
* CLI command names.
* Header names.
* Known RFC source file extensions.
* Static routing or dispatch maps.

Do not use frozen collections for small, short-lived, or frequently rebuilt collections.

Always specify the intended comparer for string keys when casing matters:

```csharp
StringComparer.Ordinal
StringComparer.OrdinalIgnoreCase
```