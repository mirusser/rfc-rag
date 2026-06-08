# Contributing

## Development

```bash
# Build
dotnet build RfcRag.slnx

# Run unit tests
dotnet test --filter "Category!=Integration"

# Run integration tests (requires Docker)
dotnet test --filter "Category=Integration"
```

## Code Style

This project uses [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer) with all warnings enabled
and treated as errors. Run `dotnet format RfcRag.slnx --verify-no-changes` before submitting.

## Pull Requests

1. Ensure `dotnet build` and `dotnet test --filter "Category!=Integration"` pass
2. Keep changes focused and minimal
3. Follow existing code patterns
