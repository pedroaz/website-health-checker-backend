#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
if [ "$#" -ne 1 ] || [[ ! "$1" =~ ^[A-Z][A-Z0-9]*-[0-9]+$ ]]; then
  echo 'Usage: scripts/agent/run-ticket.sh KAN-123' >&2; exit 2
fi
command -v timeout >/dev/null || { echo 'Install GNU timeout (coreutils).' >&2; exit 1; }
command -v flock >/dev/null || { echo 'Install flock (util-linux).' >&2; exit 1; }
mkdir -p artifacts/agent
# A lock is held for the whole run; resumed work must not race another agent.
exec 9>artifacts/agent/run.lock
flock -n 9 || { echo 'An agent run is already active in this checkout.' >&2; exit 1; }
timeout --signal=TERM --kill-after=15s 30m scripts/agent/claude.sh \
  --print --permission-mode acceptEdits --max-budget-usd 2 --max-turns 30 \
  --output-format json --allowedTools Read Edit Write Glob Grep Agent \
  'Bash(git *)' 'Bash(gh *)' 'Bash(python3 scripts/agent/agent.py *)' \
  'Bash(npm *)' 'Bash(dotnet *)' 'Bash(bash scripts/test.sh)' \
  'mcp__github__*' 'mcp__jira__*' 'mcp__playwright-test__*' \
  -- "/work-ticket $1" > artifacts/agent/last-run.json
echo 'Run complete. Inspect artifacts/agent/last-run.json and the PR/Jira links.'
