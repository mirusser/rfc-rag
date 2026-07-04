#!/usr/bin/env bash
# restore-db.sh — restore the rfc_rag PostgreSQL database from a backup file
#
# Usage:
#   ./scripts/restore-db.sh <backup-file>
#
# Reads connection settings from .env.rfc-rag at the repo root.
# Requires pg_restore to be installed on the host.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$REPO_ROOT/.env.rfc-rag"

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <backup-file>" >&2
  exit 1
fi

BACKUP_FILE="$1"

if [[ ! -f "$BACKUP_FILE" ]]; then
  echo "Error: backup file not found: $BACKUP_FILE" >&2
  exit 1
fi

if ! command -v pg_restore &>/dev/null; then
  echo "Error: pg_restore not found. Install it with:" >&2
  echo "  Debian/Ubuntu: sudo apt install postgresql-client" >&2
  echo "  Arch:          sudo pacman -S postgresql-libs" >&2
  echo "  macOS:         brew install libpq && brew link --force libpq" >&2
  exit 1
fi

if [[ ! -f "$ENV_FILE" ]]; then
  echo "Error: $ENV_FILE not found" >&2
  exit 1
fi

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

pg_restore -h "$HOST" -p "$PORT" -U "$USER" -d "$DB" --clean --if-exists "$BACKUP_FILE"

echo "Restore complete from: $BACKUP_FILE"
