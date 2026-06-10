# Formatting

Match the surrounding code exactly.

Do not introduce column-aligned spacing.

Use file-scoped namespaces in new files when the project uses them.

```csharp
namespace Company.Project.Feature;
```

Respect existing using organization.

If the project uses GlobalUsings.cs, place new global usings there. Do not create multiple global-using files unless the project already does.

Do not add #region. If a file needs regions to stay understandable, split it.

When in doubt, just use:

```bash
dotnet format
```
