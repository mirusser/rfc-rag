---
name: run-tests
description: Run all available tests in the rfc-rag repository, correctly accounting for prerequisites (Docker, API keys) and trait/category gating. Use when running, verifying, or troubleshooting any test tier.
---

# Run Tests

## Quick Reference

| Tier | What runs | Prerequisites | Command |
|------|-----------|---------------|---------|
| **1** | Unit tests | .NET 10 SDK | `dotnet test RfcRag.slnx --filter "Category!=Integration&Category!=LiveApi"` |
| **2** | Integration tests | + Docker | `dotnet test RfcRag.slnx --filter "Category=Integration"` |
| **3** | Live API indexing | + `OpenRouter__ApiKey` + network | `dotnet test RfcRag.slnx --filter "Category=LiveApi"` |
| **4** | Coverage (CI parity) | .NET 10 SDK | see Tier 4 below |

## Test Gating Mechanisms

### 1. Trait / category filter (`--filter`)

| Trait | Used by | Meaning |
|-------|---------|---------|
| `Category=Integration` | classes in `tests/RfcRag.Tests/IntegrationTests/` | Testcontainers starts PostgreSQL (`pgvector/pgvector:pg17`); requires Docker |
| `Category=LiveApi` | `LiveApiIndexingTests` | Hits the real OpenRouter embedding API |

Unit tests carry no category trait.

### 2. Environment-based self-skip

`LiveApiIndexingTests` skip themselves when `OpenRouter__ApiKey` is not set — they pass as skipped, they do not fail. This is why CI can use the broader filter `Category!=Integration` without excluding `LiveApi` explicitly.

### 3. Implicit Docker dependency

Integration tests start the PostgreSQL container in `IAsyncLifetime.InitializeAsync`. Without a running Docker daemon they **fail** (they do not skip).

## Tier Details

### Tier 1: Unit Tests

Parser, tools, services — no external dependencies, fast.

```bash
dotnet test RfcRag.slnx --filter "Category!=Integration&Category!=LiveApi"
```

CI equivalent (`ci.yml`): `dotnet test RfcRag.slnx --configuration Release --no-build --filter "Category!=Integration"` after a separate build step (LiveApi tests self-skip in CI).

### Tier 2: Integration Tests

Migrations, indexing, hybrid search, sections, metadata, and embedding-dimension behavior against real PostgreSQL + pgvector.

```bash
dotnet test RfcRag.slnx --filter "Category=Integration"
```

**Prerequisites:**
1. Docker daemon running (`docker info` succeeds)
2. Pull access to `pgvector/pgvector:pg17`

### Tier 3: Live API Indexing

Indexes ~90 well-known RFCs through the real OpenRouter embedding API and verifies that semantic search returns domain-relevant results. Consumes real API credits — run only when embedding or search-relevance changes need end-to-end validation.

```bash
OpenRouter__ApiKey=<key> dotnet test RfcRag.slnx --filter "Category=LiveApi"
```

### Tier 4: Coverage (CI parity)

Matches the `sonar.yml` coverage run:

```bash
dotnet build RfcRag.slnx --configuration Release
dotnet test RfcRag.slnx \
  --configuration Release \
  --no-build \
  --filter "Category!=Integration" \
  --collect:"XPlat Code Coverage" \
  --settings coverlet.runsettings \
  --results-directory TestResults/coverage
```

## Prerequisites Verification

```bash
# .NET SDK
dotnet --version          # expect 10.0.x

# Docker (tier 2)
docker info               # prints daemon info, no error

# OpenRouter key (tier 3)
echo "${OpenRouter__ApiKey:+set}"   # prints "set" when available
```

## CI Parity

| CI workflow | Test step | Filter |
|-------------|-----------|--------|
| `ci.yml` | unit tests (Release, `--no-build`) | `Category!=Integration` |
| `sonar.yml` | coverage run for SonarCloud | `Category!=Integration` + XPlat coverage |

Integration tests (tier 2) require Docker and run locally — verify them before merging when changes touch persistence, migrations, indexing, or search SQL.
