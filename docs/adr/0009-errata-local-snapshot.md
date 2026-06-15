# Errata Ingestion via Local Snapshot

RFC errata is ingested from a local JSON snapshot specified by the `ErrataJsonPath` configuration key, rather than fetched from a live RFC Errata API. The snapshot is idempotent — repeated ingestion runs produce the same state. Errata entries of type `VerifiedErratum` generate `EvidenceWarning` records attached to citations, so downstream consumers can see when a cited RFC has a known erratum affecting the referenced section.

## Considered Options

- **Live API fetch (no local snapshot)** — requires network access at startup, adds latency proportional to the errata corpus, and introduces nondeterministic results if the API state changes between runs.
- **Local JSON snapshot (current)** — deterministic, fast (no network round-trip), works offline, and a plain file is trivially diffable in version control. The snapshot must be refreshed manually to pick up new errata.
- **Git submodule tracking** — automates snapshot updates via `git submodule update`, but adds a submodule dependency, complicates the clone workflow, and the errata data is small enough that diff-and-commit is simpler.

Local JSON snapshot is chosen for determinism and offline capability. The file is supplied locally, for example via `make fetch-errata`, so each deployment sees its configured snapshot until an explicit refresh updates it.
