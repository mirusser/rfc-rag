#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TEST_PROJECT="$REPO_ROOT/tests/RfcRag.Tests"
FILTER="${1:-}"

run_tests() {
    local label="$1"
    local filter="$2"
    echo "--- $label ---"
    dotnet test "$TEST_PROJECT" --no-build --filter "$filter" -v q
}

echo "Building..."
dotnet build "$REPO_ROOT/tests/RfcRag.Tests" -v q

if [[ -n "$FILTER" ]]; then
    echo "--- Custom filter: $FILTER ---"
    dotnet test "$TEST_PROJECT" --no-build --filter "$FILTER" -v q
    exit $?
fi

run_tests "Unit tests" "Category!=Integration&Category!=RetrievalQuality"
run_tests "Integration tests" "Category=Integration"
run_tests "Retrieval quality tests" "Category=RetrievalQuality"

echo ""
echo "All test suites completed."
