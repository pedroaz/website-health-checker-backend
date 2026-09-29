#!/usr/bin/env python3
"""Small, dependency-free verification and environment CLI shared by both repos."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[2]
CONFIG = json.loads((ROOT / '.openhands/project.json').read_text())
ARTIFACTS = ROOT / 'artifacts/agent'


def run(args, cwd=ROOT, **kwargs):
    return subprocess.run(args, cwd=cwd, **kwargs)


def workspace():
    sibling = ROOT.parent / CONFIG['companion']
    return [ROOT, sibling] if (sibling / '.git').exists() else [ROOT]


def fingerprint(repo):
    result = run(['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'],
                 cwd=repo, capture_output=True, check=True)
    digest = hashlib.sha256()
    for name in sorted(set(result.stdout.split(b'\0')) - {b''}):
        path = repo / os.fsdecode(name)
        digest.update(name + b'\0')
        if path.exists():
            digest.update(str(path.lstat().st_mode).encode() + b'\0')
        if path.is_symlink():
            digest.update(os.readlink(path).encode())
        elif path.is_file():
            digest.update(path.read_bytes())
        else:
            digest.update(b'<missing>')
        digest.update(b'\0')
    return digest.hexdigest()


def snapshot():
    return {repo.name: fingerprint(repo) for repo in workspace()}


def changed_files(repo):
    result = run(['git', 'diff', 'HEAD', '--name-only', '-z'], cwd=repo,
                 capture_output=True, check=True)
    other = run(['git', 'ls-files', '--others', '--exclude-standard', '-z'], cwd=repo,
                capture_output=True, check=True)
    return sorted(set(os.fsdecode(x) for x in (result.stdout + other.stdout).split(b'\0') if x))


def required_scope():
    files = [name for repo in workspace() for name in changed_files(repo)]
    if not files:
        return None
    if all(name.endswith('.md') and not name.startswith(('.claude/', '.openhands/'))
           for name in files):
        return 'docs'
    return 'full'


def check_evidence(scope=None):
    needed = scope or required_scope()
    if needed is None:
        return True, 'No uncommitted changes.'
    evidence_file = ARTIFACTS / 'verification.json'
    if not evidence_file.exists():
        return False, f'Run python3 scripts/agent/agent.py verify {needed}.'
    evidence = json.loads(evidence_file.read_text())
    ranks = {'docs': 0, 'fast': 1, 'full': 2}
    good = (evidence.get('passed') is True
            and evidence.get('fingerprints') == snapshot()
            and ranks.get(evidence.get('scope'), -1) >= ranks[needed])
    return good, 'Verification is current.' if good else f'Checks failed or evidence is stale; run verify {needed}.'


def verify(scope):
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    before = snapshot()
    commands = []
    for repo in workspace():
        commands += [(repo, ['git', 'diff', '--check'])]
    commands += [(ROOT, ['python3', '-m', 'unittest', 'discover', '-s', 'scripts/agent/tests', '-v'])]
    if scope != 'docs':
        if CONFIG['role'] == 'backend':
            commands += [(ROOT, ['dotnet', 'build', '--no-restore']),
                         (ROOT, ['dotnet', 'format', 'WebsiteHealthChecker.slnx', '--verify-no-changes', '--no-restore']),
                         (ROOT, ['dotnet', 'test', 'tests/HealthChecker.Tests', '--no-restore', '--filter', 'Category!=Integration'])]
        else:
            commands += [(ROOT, ['npm', 'run', 'format:check']),
                         (ROOT, ['npm', 'run', 'typecheck']),
                         (ROOT, ['npm', 'run', 'build'])]
    if scope == 'full':
        if len(workspace()) != 2:
            raise RuntimeError('Full verification requires the sibling checkout. Run scripts/agent/setup.sh.')
        companion = workspace()[1]
        # Both repositories' fast checks, then the existing isolated Compose suite.
        commands += [(companion, ['python3', 'scripts/agent/agent.py', 'verify', 'fast'])]
        backend = ROOT if CONFIG['role'] == 'backend' else companion
        commands += [(backend, ['bash', 'scripts/test.sh'])]
    evidence = {'scope': scope, 'passed': False, 'startedAt': datetime.now(timezone.utc).isoformat(),
                'fingerprints': before, 'commits': {}, 'commands': []}
    for repo in workspace():
        evidence['commits'][repo.name] = run(['git', 'rev-parse', 'HEAD'], cwd=repo,
                                            capture_output=True, text=True, check=True).stdout.strip()
    # Replace previous success immediately, including on interruption or setup failure.
    evidence_path = ARTIFACTS / 'verification.json'
    evidence_path.write_text(json.dumps(evidence, indent=2) + '\n')
    passed = True
    with (ARTIFACTS / 'verification.log').open('w') as log:
        for cwd, command in commands:
            line = f'[{cwd.name}] {" ".join(command)}'
            print(line, flush=True)
            log.write(line + '\n'); log.flush()
            result = run(command, cwd=cwd, stdout=log, stderr=subprocess.STDOUT)
            evidence['commands'].append({'repository': cwd.name, 'argv': command, 'exitCode': result.returncode})
            print(f'  exit {result.returncode}', flush=True)
            if result.returncode:
                passed = False
                break
    evidence['passed'] = passed and snapshot() == before
    evidence['finishedAt'] = datetime.now(timezone.utc).isoformat()
    evidence_path.write_text(json.dumps(evidence, indent=2) + '\n')
    print(f"{'PASS' if evidence['passed'] else 'FAIL'}: {evidence_path}")
    if not evidence['passed']:
        print('Inspect artifacts/agent/verification.log. No failing check was skipped.')
        return 1
    return 0


def doctor():
    failures = []
    for name in ('git', 'python3', 'node', 'npm', 'gh', 'claude', 'dotnet', 'docker'):
        present = bool(shutil.which(name))
        print(f"{'OK' if present else 'MISSING'} {name}")
        if not present:
            failures.append(name)
    print('Sibling checkout:', 'OK' if len(workspace()) == 2 else 'MISSING')
    if len(workspace()) != 2:
        failures.append('sibling')
    if shutil.which('gh'):
        good = run(['gh', 'api', f"repos/{CONFIG['repository']}", '--jq', '.full_name'],
                   capture_output=True).returncode == 0
        print('GitHub repository API:', 'OK' if good else 'UNAVAILABLE (run gh auth login)')
    if shutil.which('docker'):
        good = run(['docker', 'info'], capture_output=True).returncode == 0
        print('Docker daemon:', 'OK' if good else 'UNAVAILABLE (full verification requires Docker/CI)')
    import mcp_headers
    jira = bool(mcp_headers.headers('jira'))
    print('Jira:', 'token configured; validate with /mcp' if jira else 'OAuth required: open Claude, then /mcp → jira')
    print('Slack:', 'configured' if os.environ.get('SLACK_WEBHOOK_URL') else 'optional, not configured')
    print('MCP connectivity: use scripts/agent/claude.sh mcp list; status above is not an authentication test.')
    return 1 if failures else 0


def hook():
    event = json.load(sys.stdin)
    name = event.get('hook_event_name')
    session_id = hashlib.sha256(str(event.get('session_id', '')).encode()).hexdigest()
    baseline = ARTIFACTS / ('session-' + session_id + '.json')
    if name == 'SessionStart':
        ARTIFACTS.mkdir(parents=True, exist_ok=True)
        baseline.write_text(json.dumps(snapshot()))
        print('Health Checker: use work-ticket for Jira/feature work. Haiku only; no fallback. '
              'Read docs/AGENT_SETUP.md. Verify via scripts/agent/agent.py and cite actual exit codes. '
              'Do not merge, deploy, or mark Jira Done merely because a PR exists.')
    elif name == 'Stop':
        # Report a blocker once; never force an infinite repair loop.
        if event.get('stop_hook_active'):
            return
        if baseline.exists() and json.loads(baseline.read_text()) == snapshot():
            return
        good, message = check_evidence()
        if not good:
            print(json.dumps({'decision': 'block', 'reason': message +
                ' If blocked by credentials/runtime or after two repairs, finish with an explicit blocked report; do not claim success.'}))
    elif name == 'PreToolUse':
        model = event.get('tool_input', {}).get('model')
        if model and model not in ('haiku', CONFIG['model'], 'inherit'):
            print(json.dumps({'hookSpecificOutput': {'hookEventName': name,
                  'permissionDecision': 'deny', 'permissionDecisionReason': 'This test setup permits Haiku only.'}}))
    elif name == 'PreModelSwitch':
        # All switching is disabled; launch a fresh session with the pinned model.
        print(json.dumps({'decision': 'block', 'reason': 'Model switching is disabled in this Haiku-only test setup.'}))


def main():
    parser = argparse.ArgumentParser()
    subs = parser.add_subparsers(dest='command', required=True)
    subs.add_parser('doctor')
    subs.add_parser('hook')
    gate = subs.add_parser('gate')
    gate.add_argument('--scope', choices=['docs', 'fast', 'full'])
    verification = subs.add_parser('verify')
    verification.add_argument('scope', choices=['docs', 'fast', 'full'], default='fast', nargs='?')
    args = parser.parse_args()
    if args.command == 'doctor':
        return doctor()
    if args.command == 'verify':
        return verify(args.scope)
    if args.command == 'gate':
        good, message = check_evidence(args.scope)
        print(message)
        return 0 if good else 1
    hook()
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as exc:
        print(f'Agent tooling failed: {exc}', file=sys.stderr)
        sys.exit(1)
