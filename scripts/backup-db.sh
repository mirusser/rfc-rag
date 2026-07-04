#!/usr/bin/env bash
# backup-db.sh — dump the rfc_rag PostgreSQL database to /backups/
#
# Usage:
#   ./scripts/backup-db.sh [output-dir]
#
# Reads connection settings from .env.rfc-rag at the repo root.
# Requires pg_dump to be installed on the host.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$REPO_ROOT/.env.rfc-rag"
BACKUP_DIR="${1:-$REPO_ROOT/backups}"

if [[ ! -f "$ENV_FILE" ]]; then
  echo "Error: $ENV_FILE not found" >&2
  exit 1
fi

# Load env vars (ignore comments and empty lines)
set -a
# shellcheck disable=SC1090
source <(grep -v '^\s*#' "$ENV_FILE" | grep -v '^\s*$')
set +a

HOST="${PG_BIND_ADDRESS:-127.0.0.1}"
PORT="${PG_BIND_PORT:-5433}"
DB="${PG_DATABASE:-rfc_rag}"
USER="${PG_USER:-rfc_rag}"
PGPASSWORD="${PG_PASSWORD}"
export PGPASSWORD

if ! command -v pg_dump &>/dev/null; then
  echo "Error: pg_dump not found. Install it with:" >&2
  echo "  Debian/Ubuntu: sudo apt install postgresql-client" >&2
  echo "  Arch:          sudo pacman -S postgresql-libs" >&2
  echo "  macOS:         brew install libpq && brew link --force libpq" >&2
  exit 1
fi

mkdir -p "$BACKUP_DIR"

TIMESTAMP="$(date +%Y%m%d_%H%M%S)"
OUTPUT="$BACKUP_DIR/${DB}_${TIMESTAMP}.dump"

pg_dump -h "$HOST" -p "$PORT" -U "$USER" -Fc "$DB" -f "$OUTPUT"

echo "Backup written to: $OUTPUT"
