#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
# This file is an explicit user-owned shell environment file; never commit it.
if [ -f .env.agent.local ]; then set -a; source .env.agent.local; set +a; fi
export ANTHROPIC_MODEL=claude-haiku-4-5-20251001
export ANTHROPIC_DEFAULT_HAIKU_MODEL="$ANTHROPIC_MODEL"
export CLAUDE_CODE_SUBAGENT_MODEL="$ANTHROPIC_MODEL"
export CLAUDE_CODE_SUBAGENT_MODEL_FORCE=true
if [ "${1:-}" = mcp ]; then
  shift
  exec claude mcp "$@"
fi
for arg in "$@"; do
  case "$arg" in
    --model|--model=*|--fallback-model|--fallback-model=*|--agent|--agent=*|--agents|--agents=*|--settings|--settings=*|--setting-sources|--setting-sources=*|--safe-mode|--bare)
      echo "Use the repository's pinned Haiku configuration; override rejected: $arg" >&2; exit 2;;
  esac
done
exec claude --model "$ANTHROPIC_MODEL" --mcp-config "$root/.mcp.json" "$@"
