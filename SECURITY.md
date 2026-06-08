# Security Policy

## Supported Versions

The project is experimental. Security fixes apply to the latest commit on `main`.

## Reporting a Vulnerability

Please report security vulnerabilities privately to the repository maintainer.
Do not open a public issue.

## Dependencies

This project depends on:

- **OpenRouter API** for embedding generation. Your API key should be kept secret and never committed.
- **PostgreSQL** for data storage. Ensure your database is not exposed to untrusted networks.
- **Local RFC mirror** accessed via filesystem. The server only reads from this path.

## MCP Server Security

The RFC RAG MCP server is a read-only tool server. It does not:

- Execute arbitrary commands or shell scripts
- Write to the filesystem (except SQL migrations on startup)
- Accept network connections (stdio transport only)
- Expose secrets in tool responses
