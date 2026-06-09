#!/usr/bin/env bash
# smoke-test-release.sh — Verify a published RFC RAG image
#
# Boots the compose stack with a published GHCR image, verifies the MCP
# server starts and tools respond correctly, then tears down.
#
# Usage:
#   TAG=v0.1.0 ./scripts/smoke-test-release.sh
#
# Prerequisites:
#   - .env.rfc-rag at repo root (or RFC_RAG_ENV_FILE pointing to it)
#   - Docker Compose v2
#   - TAG must match a published image at ghcr.io/mirusser/rfc-rag

set -euo pipefail

# ── Required inputs ──────────────────────────────────────────────
TAG="${TAG:-}"
if [ -z "$TAG" ]; then
  echo "ERROR: TAG is required. Usage: TAG=vX.Y.Z ./scripts/smoke-test-release.sh" >&2
  exit 1
fi

IMAGE="ghcr.io/mirusser/rfc-rag:${TAG}"
COMPOSE_FILE="${COMPOSE_FILE:-deploy/compose/release/rfc-rag.yaml}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# ── Load environment ─────────────────────────────────────────────
ENV_FILE="${RFC_RAG_ENV_FILE:-${REPO_ROOT}/.env.rfc-rag}"
if [ ! -f "$ENV_FILE" ]; then
  echo "ERROR: Environment file not found: $ENV_FILE" >&2
  echo "Copy deploy/compose/rfc-rag.env.example to .env.rfc-rag and edit it." >&2
  exit 1
fi
set -a && source "$ENV_FILE" && set +a

# ── Colors ───────────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

pass() { echo -e "${GREEN}✓${NC} $*"; }
fail() { echo -e "${RED}✗${NC} $*"; }
info() { echo -e "${YELLOW}→${NC} $*"; }

# ── Cleanup ──────────────────────────────────────────────────────
cleanup() {
  local exit_code=$?
  info "Cleaning up..."
  docker rm -f rfc-rag-smoke 2>/dev/null || true
  docker compose -f "$COMPOSE_FILE" down -v 2>/dev/null || true
  if [ $exit_code -ne 0 ]; then
    echo ""
    fail "Smoke test FAILED (exit code: $exit_code)"
  fi
  exit $exit_code
}
trap cleanup EXIT

# ── Step 1: Pull image ───────────────────────────────────────────
info "Pulling $IMAGE ..."
docker pull "$IMAGE"

# ── Step 2: Start PostgreSQL ─────────────────────────────────────
info "Starting PostgreSQL ..."
docker compose -f "$COMPOSE_FILE" up -d --wait postgres

# ── Step 3: Verify PostgreSQL is accepting connections ───────────
info "Verifying PostgreSQL connectivity ..."
PG_USER="${PG_USER:-rfc_rag}"
PG_PASSWORD="${PG_PASSWORD:-rfc_rag}"
PG_DATABASE="${PG_DATABASE:-rfc_rag}"

ENABLE_PGVECTOR=$(docker exec "$(docker compose -f "$COMPOSE_FILE" ps -q postgres)" \
  psql -U "$PG_USER" -d "$PG_DATABASE" -tAc "CREATE EXTENSION IF NOT EXISTS vector; SELECT 'ok';" 2>&1)
if echo "$ENABLE_PGVECTOR" | grep -q "ok"; then
  pass "pgvector extension enabled"
else
  echo "$ENABLE_PGVECTOR" >&2
  exit 1
fi

# ── Step 4: Start rfc-rag from published image ───────────────────
# Derive the compose network name from the compose project
COMPOSE_NETWORK="${COMPOSE_PROJECT_NAME:-rfc-rag}_default"
CONNECTION_STRING="Host=postgres;Database=${PG_DATABASE};Username=${PG_USER};Password=${PG_PASSWORD}"

info "Starting RFC RAG MCP server from $IMAGE ..."
docker run -d --name rfc-rag-smoke \
  --network "$COMPOSE_NETWORK" \
  -e "RfcRag__PostgresConnectionString=${CONNECTION_STRING}" \
  -e "RfcRag__RfcMirrorPath=/nonexistent" \
  -e "RfcRag__EmbeddingBatchSize=1" \
  -e "RfcRag__OpenRouterEmbeddingEndpoint=https://httpstat.us/200" \
  -e "OpenRouter__ApiKey=${OpenRouter__ApiKey:-}" \
  --restart no \
  --entrypoint sleep \
  "$IMAGE" infinity

# ── Step 5: Verify server starts ─────────────────────────────────
info "Waiting for server startup ..."
# Run the server with stdin piped to keep it alive (MCP stdio server exits on stdin EOF)
# We pipe sleep into docker exec to keep stdin open long enough for our checks
SERVER_LOG="/tmp/rfc-rag-smoke-$$.log"

# Start server in background via docker exec with stdin kept open
# The `sleep N |` pattern keeps stdin open for N seconds
(sleep 30 | docker exec -i rfc-rag-smoke dotnet RfcRag.dll 2>&1 | tee "$SERVER_LOG") &
SERVER_PID=$!

# Wait for server to be ready (look for startup message or just wait a few seconds)
sleep 5

if ! docker inspect -f '{{.State.Running}}' rfc-rag-smoke 2>/dev/null | grep -q true; then
  fail "RFC RAG container is not running"
  cat "$SERVER_LOG" 2>/dev/null || true
  exit 1
fi
pass "Server started"

# ── Step 6: Verify MCP tools ─────────────────────────────────────
info "Querying MCP tools/list ..."

MCP_REQUEST='{"jsonrpc":"2.0","method":"tools/list","id":1,"params":{}}'
TOOLS_RESPONSE=$(echo "$MCP_REQUEST" | docker exec -i rfc-rag-smoke dotnet RfcRag.dll 2>/dev/null | head -1 || true)

if [ -z "$TOOLS_RESPONSE" ]; then
  fail "No response from tools/list"
  cat "$SERVER_LOG" 2>/dev/null || true
  exit 1
fi

TOOL_COUNT=$(echo "$TOOLS_RESPONSE" | python3 -c "import sys,json; d=json.load(sys.stdin); print(len(d.get('result',{}).get('tools',[])))" 2>/dev/null || echo "0")

if [ "$TOOL_COUNT" -ge 1 ] 2>/dev/null; then
  pass "tools/list returned $TOOL_COUNT tools"
else
  fail "tools/list returned $TOOL_COUNT tools (expected >= 1)"
  echo "Response: $TOOLS_RESPONSE"
  exit 1
fi

# ── Step 7: Verify rfc_stats tool ────────────────────────────────
info "Querying rfc_stats ..."
STATS_REQUEST='{"jsonrpc":"2.0","method":"tools/call","id":2,"params":{"name":"rfc_stats","arguments":{}}}'
STATS_RESPONSE=$(echo "$STATS_REQUEST" | docker exec -i rfc-rag-smoke dotnet RfcRag.dll 2>/dev/null | head -1 || true)

if echo "$STATS_RESPONSE" | python3 -c "import sys,json; d=json.load(sys.stdin)" 2>/dev/null; then
  pass "rfc_stats returned valid JSON"
else
  fail "rfc_stats did not return valid JSON"
  echo "Response: ${STATS_RESPONSE:-empty}"
  exit 1
fi

# ── Cleanup (handled by trap) ────────────────────────────────────
kill "$SERVER_PID" 2>/dev/null || true
rm -f "$SERVER_LOG"
pass "Smoke test PASSED for $IMAGE"
