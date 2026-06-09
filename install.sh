#!/usr/bin/env bash
# install.sh — bootstrap RFC RAG without cloning the repo
#
# Usage:
#   bash <(curl -fsSL https://raw.githubusercontent.com/mirusser/rfc-rag/master/install.sh)
#
# What it does:
#   1. Downloads deploy/compose/rfc-rag.yaml to the current directory
#   2. Creates .env.rfc-rag from the example template (if not already present)
#   3. Prints next steps

set -euo pipefail

REPO_RAW="https://raw.githubusercontent.com/mirusser/rfc-rag/master"
COMPOSE_OUT="rfc-rag.yaml"
ENV_OUT=".env.rfc-rag"

echo "Downloading RFC RAG compose file..."
curl -fsSL "$REPO_RAW/deploy/compose/rfc-rag.yaml" -o "$COMPOSE_OUT"
echo "  -> $COMPOSE_OUT"

if [[ ! -f "$ENV_OUT" ]]; then
    echo "Downloading env template..."
    curl -fsSL "$REPO_RAW/deploy/compose/rfc-rag.env.example" -o "$ENV_OUT"
    echo "  -> $ENV_OUT (created from example — edit before starting)"
else
    echo "  -> $ENV_OUT already exists, skipping"
fi

echo ""
echo "Before first start:"
echo "  1. Edit $ENV_OUT — set OpenRouter__ApiKey and RFC_MIRROR_HOST_PATH"
echo "  2. Sync RFC mirror (one-time, ~2 GB):"
echo "       rsync -avz --delete rsync.rfc-editor.org::rfcs-text-only ~/rfc-mirror/"
echo ""
echo "Start the stack:"
echo "  docker compose --env-file $ENV_OUT -f $COMPOSE_OUT up"
echo ""
echo "Connect Claude Code after the stack is running:"
echo "  claude mcp add-json --scope user rfc-rag \\"
echo "    '{\"type\":\"stdio\",\"command\":\"docker\",\"args\":[\"exec\",\"-i\",\"rfc-rag-rfc-rag-1\",\"dotnet\",\"RfcRag.dll\"]}'"
echo ""
echo "Connect Codex CLI after the stack is running:"
echo "  # Add to ~/.codex/config.toml:"
echo "  # [mcp_servers.rfc-rag]"
echo "  # command = \"docker\""
echo "  # args = [\"exec\", \"-i\", \"rfc-rag-rfc-rag-1\", \"dotnet\", \"RfcRag.dll\"]"
