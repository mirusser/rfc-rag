#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TEST_PROJECT="$REPO_ROOT/tests/RfcRag.Tests"
FILTER="${1:-}"

overall_result=0

run_tests() {
    local label="$1"
    local filter="$2"
    echo "--- $label ---"
    dotnet test "$TEST_PROJECT" --no-build --filter "$filter" -v q || overall_result=1
}

echo "Building..."
dotnet build "$REPO_ROOT/tests/RfcRag.Tests" -v q

if [[ -n "$FILTER" ]]; then
    echo "--- Custom filter: $FILTER ---"
    dotnet test "$TEST_PROJECT" --no-build --filter "$FILTER" -v q || true
    exit $?
fi

run_tests "Unit tests" "Category!=Integration&Category!=RetrievalQuality&Category!=LiveApi"
run_tests "Integration tests" "Category=Integration"
run_tests "Retrieval quality tests" "Category=RetrievalQuality"
run_tests "Live API tests" "Category=LiveApi"

echo ""

if [[ "$overall_result" -eq 0 ]]; then
    echo "All test suites completed — ALL PASSED."
else
    echo "All test suites completed — SOME SUITES HAVE FAILURES (see above)."
fi
exit "$overall_result"
