# Releasing RFC RAG

This file owns the release process for RFC RAG. Releases are marked **pre-release** while the project is experimental; do not mark a release as stable until the project exits the experimental phase.

## Release Checklist

1. Confirm CI passes on `master` (build, test, coverage, format check, docker-smoke, NuGet vuln scan).
2. Confirm `CHANGELOG.md` has a release-ready entry for the version under `## [X.Y.Z]`.
3. Confirm all version references in documentation (compose files, README examples) match the release version if hardcoded.
4. Tag and push:
   ```bash
   git tag -a vX.Y.Z -m "vX.Y.Z"
   git push origin vX.Y.Z
   ```
5. Confirm CI `pack-publish` job succeeds — NuGet package published to GitHub Packages, GitHub Release created.
6. Confirm `publish.yml` succeeds — GHCR image pushed (`ghcr.io/mirusser/rfc-rag:X.Y.Z`), Trivy scan passes, GitHub Release created.
7. Confirm the GHCR package is set to **public** (Settings → Packages → rfc-rag → Change visibility).
8. Confirm the GitHub Release includes correct image name and tag in its notes.
9. Confirm the release is marked as **pre-release** (should be automatic; verify it).
10. Run the smoke test against the published tag:
    ```bash
    TAG=vX.Y.Z ./scripts/smoke-test-release.sh
    ```
11. Verify quickstart commands reference the released tag:
    ```bash
    TAG=vX.Y.Z make quickstart
    ```
12. Verify no secrets, tokens, or live credentials are present in docs, compose files, or example env files.

## Release Notes Template

Paste this into GitHub Releases and fill in `VERSION` and the change sections:

```markdown
## RFC RAG VERSION — Experimental Preview

This is an experimental release of RFC RAG.

### Images

GitHub Container Registry:

- `ghcr.io/mirusser/rfc-rag:VERSION`

### Status

Experimental. Not recommended for production workloads.

### Changes

- Summarize the `CHANGELOG.md` entry for this version.

### Known limitations

- ...

### Upgrade notes

- ...
```

## Smoke Test

Run the published-image smoke test against the release tag before announcing:

```bash
TAG=vX.Y.Z ./scripts/smoke-test-release.sh
```

The script boots `deploy/compose/release/rfc-rag.yaml` with the published GHCR image, waits for PostgreSQL, starts the MCP server, verifies `tools/list` returns the expected tool count, queries `rfc_stats`, and tears everything down. It exits non-zero and dumps logs on failure.
