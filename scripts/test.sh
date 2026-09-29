#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
# Unique project and project-scoped volumes: never destroy demo data.
project="health-tests-$(date +%s)-$$"
compose=(docker compose -p "$project" -f compose.yaml -f compose.test.yaml --profile test)
cleanup() {
  result=$?
  if [ "$result" -ne 0 ]; then "${compose[@]}" logs --no-color > artifacts/compose-test.log 2>&1 || true; fi
  "${compose[@]}" down -v --remove-orphans --rmi local
  exit "$result"
}
trap cleanup EXIT
mkdir -p artifacts/backend ../website-health-checker-frontend/playwright-report ../website-health-checker-frontend/test-results
"${compose[@]}" up -d --build --wait frontend demo-target
"${compose[@]}" run --build --rm backend-tests
"${compose[@]}" run --build --rm browser-tests
"${compose[@]}" exec -T demo-target node persistence.mjs prepare
"${compose[@]}" restart postgres
"${compose[@]}" up -d --wait postgres
"${compose[@]}" restart backend
"${compose[@]}" up -d --wait backend
"${compose[@]}" exec -T demo-target node persistence.mjs verify
