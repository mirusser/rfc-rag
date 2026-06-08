# Changelog

All notable changes to RFC RAG will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- Initial standalone extraction from the Kubernetes MCP Guard monorepo.
- Ten MCP tools: `search_rfc`, `get_rfc`, `get_rfc_section`, `get_rfc_toc`, `search_normative`, `search_abnf`, `find_updates_obsoletes`, `rfc_stats`, `get_rfc_metadata`, `list_indexed_rfcs`.
- Hybrid search combining pgvector cosine similarity and PostgreSQL full-text search with reciprocal rank fusion.
- Incremental SHA256-based indexing (subsequent starts complete in seconds).
- Docker Compose setup with pgvector PostgreSQL sidecar.
- Standalone Dockerfile for containerized stdio MCP server.
- Grammar-style detection: `grammarStyle` field classifies RFCs as `abnf`, `tls-presentation-lang`, `cddl`, `asn.1`, or `none`.
- `get_rfc_section` supports `depth` (child-section expansion) and `expand` (type-reference resolution).
