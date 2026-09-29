#!/usr/bin/env python3
"""Credential adapter for official MCP servers. Never log returned headers."""
import base64
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
STORE = ROOT / '.claude' / 'mcp-auth.local.json'
NAMES = ('GITHUB_TOKEN', 'GH_TOKEN', 'ATLASSIAN_MCP_AUTH', 'JIRA_EMAIL', 'JIRA_API_TOKEN')


def credentials():
    # Project headersHelper commands have credential env vars scrubbed by Claude.
    # This ignored, mode-0600 file is populated explicitly by setup, never by Git.
    saved = json.loads(STORE.read_text()) if STORE.exists() else {}
    return {name: os.environ.get(name) or saved.get(name, '') for name in NAMES}


def headers(service, values=None):
    values = credentials() if values is None else values
    if service == 'github':
        token = values.get('GH_TOKEN') or values.get('GITHUB_TOKEN')
        if not token:
            try:
                result = subprocess.run(['gh', 'auth', 'token', '--hostname', 'github.com'],
                                        capture_output=True, text=True, timeout=5)
                token = result.stdout.strip() if result.returncode == 0 else ''
            except (OSError, subprocess.TimeoutExpired):
                token = ''
        return {'Authorization': f'Bearer {token}'} if token else {}
    if service == 'jira':
        auth = values.get('ATLASSIAN_MCP_AUTH')
        if not auth and values.get('JIRA_EMAIL') and values.get('JIRA_API_TOKEN'):
            encoded = base64.b64encode(
                f"{values['JIRA_EMAIL']}:{values['JIRA_API_TOKEN']}".encode()).decode()
            auth = f'Basic {encoded}'
        return {'Authorization': auth} if auth else {}  # OAuth locally when absent
    raise ValueError('Unknown MCP service')


def capture():
    values = {k: os.environ[k] for k in NAMES if os.environ.get(k)}
    if not values:
        print('No MCP credential environment variables to capture; existing store unchanged.')
        return
    STORE.parent.mkdir(exist_ok=True)
    saved = json.loads(STORE.read_text()) if STORE.exists() else {}
    saved.update(values)
    fd = os.open(STORE, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    os.fchmod(fd, 0o600)
    with os.fdopen(fd, 'w') as output:
        json.dump(saved, output)
    print('Saved MCP credentials to ignored .claude/mcp-auth.local.json (0600).')


if __name__ == '__main__':
    try:
        if len(sys.argv) != 2:
            raise ValueError('Usage: mcp_headers.py github|jira|capture')
        if sys.argv[1] == 'capture':
            capture()
        else:
            print(json.dumps(headers(sys.argv[1])))
    except Exception:
        print('MCP credential helper failed; check credentials/configuration locally.', file=sys.stderr)
        sys.exit(1)
