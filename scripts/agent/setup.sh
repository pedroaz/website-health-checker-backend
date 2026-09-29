#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
for tool in git python3 node npm dotnet; do
  command -v "$tool" >/dev/null || { echo "Missing $tool. See docs/AGENT_SETUP.md." >&2; exit 1; }
done
node -e 'if (Number(process.versions.node.split(".")[0]) < 24) process.exit(1)' || {
  echo 'Node 24 or newer is required; Node 24 is the tested CI version.' >&2; exit 1;
}
companion="$(python3 -c 'import json; print(json.load(open(".openhands/project.json"))["companion"])')"
sibling="$root/../$companion"
if [ ! -e "$sibling" ]; then
  git clone "https://github.com/pedroaz/$companion.git" "$sibling"
elif [ ! -e "$sibling/.git" ]; then
  echo "Refusing to replace non-Git directory: $sibling" >&2; exit 1
fi
# Existing checkouts are never reset, pulled, or switched by setup.
if [ -f package.json ]; then frontend="$root"; backend="$sibling";
else frontend="$sibling"; backend="$root"; fi
(cd "$frontend" && npm ci)
(cd "$backend" && dotnet restore --locked-mode && dotnet tool restore)
if [ "${1:-}" != '--no-browser' ]; then
  (cd "$frontend" && npx --no-install playwright install chromium)
fi
python3 scripts/agent/mcp_headers.py capture
echo 'Setup complete. Existing checkouts preserved. Run python3 scripts/agent/agent.py doctor.'
