import importlib.util
import json
import io
from contextlib import redirect_stdout
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

SCRIPTS = Path(__file__).resolve().parents[1]
def load(name):
    spec = importlib.util.spec_from_file_location(name, SCRIPTS / (name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module
agent = load('agent')
headers = load('mcp_headers')

class VerificationTests(unittest.TestCase):
    def test_fingerprint_detects_edit_delete_and_new_file(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = Path(directory)
            subprocess.run(['git', 'init', '-q', directory], check=True)
            p = repo / 'source.txt'
            p.write_text('before')
            subprocess.run(['git', '-C', directory, 'add', '.'], check=True)
            before = agent.fingerprint(repo)
            p.write_text('after')
            self.assertNotEqual(before, agent.fingerprint(repo))
            p.unlink()
            self.assertNotEqual(before, agent.fingerprint(repo))
            p.write_text('before')
            (repo / 'new.txt').write_text('new')
            self.assertNotEqual(before, agent.fingerprint(repo))

    def test_gate_rejects_stale_failed_and_insufficient_evidence(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(agent, 'ARTIFACTS', Path(directory)), patch.object(agent, 'snapshot', return_value={'repo':'current'}):
            p = Path(directory) / 'verification.json'
            for scope, passed, fingerprint in [('full',False,'current'),('full',True,'stale'),('fast',True,'current')]:
                p.write_text(json.dumps(dict(scope=scope, passed=passed, fingerprints={'repo':fingerprint})))
                self.assertFalse(agent.check_evidence('full')[0])
            p.write_text(json.dumps(dict(scope='full', passed=True, fingerprints={'repo':'current'})))
            self.assertTrue(agent.check_evidence('full')[0])

    def test_github_credentials_are_loaded_without_committed_secrets(self):
        with patch.object(headers, 'credentials', return_value={'GITHUB_TOKEN':'test-only'}):
            self.assertEqual(headers.headers('github'), {'Authorization':'Bearer test-only'})

    def test_read_only_session_does_not_require_unrelated_dirty_work_verification(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(agent, 'ARTIFACTS', Path(directory)), patch.object(agent, 'snapshot', return_value={'repo':'dirty-before-session'}):
            start = {'hook_event_name':'SessionStart', 'session_id':'test'}
            with patch('sys.stdin', io.StringIO(json.dumps(start))), redirect_stdout(io.StringIO()):
                agent.hook()
            stop = {'hook_event_name':'Stop', 'session_id':'test'}
            output = io.StringIO()
            with patch('sys.stdin', io.StringIO(json.dumps(stop))), redirect_stdout(output):
                agent.hook()
            self.assertEqual(output.getvalue(), '')
            with patch.object(agent, 'snapshot', return_value={'repo':'edited'}), patch('sys.stdin', io.StringIO(json.dumps(stop))), patch.object(agent, 'check_evidence', return_value=(False,'stale')), redirect_stdout(output):
                agent.hook()
            self.assertEqual(json.loads(output.getvalue())['decision'], 'block')

    def test_every_agent_is_haiku(self):
        for path in (agent.ROOT / '.claude/agents').glob('*.md'):
            self.assertIn('model: haiku', path.read_text(), path.name)
        settings = json.loads((agent.ROOT / '.claude/settings.json').read_text())
        self.assertEqual(settings['model'], agent.CONFIG['model'])

if __name__ == '__main__':
    unittest.main()
