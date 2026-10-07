"""Pure claim/control tests: no SDK, fork, signal, cgroup or Docker execution."""
import copy
import ast
import datetime as dt
import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

SCRIPT = Path(__file__).resolve().parents[1] / 'run_catalog_build_grant.py'
spec = importlib.util.spec_from_file_location('catalog_build_claim', SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class BuildClaimControls(unittest.TestCase):
    def setUp(self):
        self.now = dt.datetime(2026, 10, 8, tzinfo=dt.timezone.utc)
        self.policy = {'sourceManifestSha256': 'b' * 64, 'capsuleSha256': 'c' * 64}
        self.head = 'a' * 40
        self.claim = dict(schemaVersion=1, scope='catalog-v8-build-only', transportSha=self.head,
            sourceManifestSha256='b' * 64, capsuleSha256='c' * 64,
            issuedUtc='2026-10-08T00:00:00Z', admitBeforeUtc='2026-10-08T00:05:00Z',
            maximumWorkSeconds=600, nonce='d' * 32)
    def validate(self, claim=None, now=None):
        return module.validate(json.dumps(self.claim if claim is None else claim), self.policy,
                               self.head, self.now if now is None else now)
    def test_exact_build_claim(self):
        self.assertEqual(self.claim, self.validate())
    def test_foreign_phase_head_source_and_budget_refused(self):
        for key, value in [('scope', 'full'), ('transportSha', '0' * 40),
                           ('sourceManifestSha256', '0' * 64), ('capsuleSha256', '0' * 64),
                           ('maximumWorkSeconds', True), ('maximumWorkSeconds', 601), ('schemaVersion', True)]:
            with self.subTest(key=key, value=value):
                claim = copy.deepcopy(self.claim)
                claim[key] = value
                with self.assertRaises(ValueError): self.validate(claim)
    def test_missing_extra_duplicate_oversized_refused(self):
        for change in ('missing', 'extra'):
            claim = copy.deepcopy(self.claim)
            if change == 'missing': claim.pop('nonce')
            else: claim['issuer'] = 'counterfeit-field'
            with self.assertRaises(ValueError): self.validate(claim)
        for raw in ('{"scope":"a","scope":"b"}', ' ' * 4097):
            with self.assertRaises(ValueError): module.validate(raw, self.policy, self.head, self.now)
    def test_expired_future_and_unbounded_window_refused(self):
        with self.assertRaises(ValueError): self.validate(now=self.now + dt.timedelta(seconds=300))
        with self.assertRaises(ValueError): self.validate(now=self.now - dt.timedelta(seconds=1))
        claim = copy.deepcopy(self.claim)
        claim['admitBeforeUtc'] = '2026-10-08T00:05:01Z'
        with self.assertRaises(ValueError): self.validate(claim)
    def test_expiry_at_actual_fork_refuses_before_child_creation(self):
        fork = Mock()
        supervisor = SimpleNamespace(os=SimpleNamespace(fork=fork))
        supervisor.main = lambda: supervisor.os.fork()
        moments = iter([self.now, self.now + dt.timedelta(seconds=300)])
        with self.assertRaises(ValueError):
            module.run_build(supervisor, json.dumps(self.claim), self.policy, self.head, '/exact/dotnet', lambda: next(moments))
        fork.assert_not_called()
        self.assertIs(fork, supervisor.os.fork)
    def test_actual_delegate_uses_build_only_and_restores_after_failure(self):
        fork = Mock(return_value=123)
        supervisor = SimpleNamespace(os=SimpleNamespace(fork=fork))
        arguments = module.sys.argv
        def main():
            self.assertEqual(['sealed-catalog-supervisor', 'build', '/exact/dotnet'], module.sys.argv)
            self.assertEqual(123, supervisor.os.fork())
            raise KeyboardInterrupt('owned cleanup already settled in controlled supervisor')
        supervisor.main = main
        with self.assertRaises(KeyboardInterrupt):
            module.run_build(supervisor, json.dumps(self.claim), self.policy, self.head, '/exact/dotnet', lambda: self.now)
        self.assertIs(arguments, module.sys.argv)
        self.assertIs(fork, supervisor.os.fork)
    def test_build_mode_does_not_enable_future_sdk_steps(self):
        workflow = (SCRIPT.parents[1] / '.github/workflows/catalog-candidate-qualification.yml').read_text()
        self.assertIn("if: inputs.mode == 'sealed-build'", workflow)
        self.assertIn('python3 -B ../scripts/run_catalog_build_grant.py "$sdk"', workflow)
        self.assertEqual(2, workflow.count("if: inputs.mode == 'sealed-native'\n"))
        self.assertIn('default: controller-smoke', workflow)
        self.assertLess(workflow.index('run_catalog_build_grant.py --verify-permit'), workflow.index('uses: actions/setup-dotnet@'))
    def test_actual_pre_setup_verification_never_imports_or_allocates_supervisor(self):
        policy = json.loads((SCRIPT.parent / 'catalog-candidate-policy.json').read_bytes())
        claim = copy.deepcopy(self.claim)
        claim.update(sourceManifestSha256=policy['sourceManifestSha256'], capsuleSha256=policy['capsuleSha256'])
        instant = self.now
        class FixedClock(dt.datetime):
            @classmethod
            def now(cls, tz=None): return instant
        with patch.object(module.sys, 'argv', ['claim-gate', '--verify-permit']), \
             patch.object(module.dt, 'datetime', FixedClock), \
             patch.dict(module.os.environ, CATALOG_BUILD_PERMIT=json.dumps(claim), GITHUB_SHA=self.head,
                        GITHUB_RUN_ID='123', GITHUB_RUN_ATTEMPT='1'), \
             patch.object(module.os, 'geteuid', create=True, side_effect=AssertionError('native allocation reached')), \
             patch.object(module.importlib.util, 'spec_from_file_location', side_effect=AssertionError('supervisor import reached')):
            module.main()
            module.os.environ['CATALOG_BUILD_PERMIT'] = ''
            with self.assertRaises(ValueError): module.main()
    def finalization(self, body_failure=None, metadata_failure=None, report_failure=None):
        tree = ast.parse(SCRIPT.read_text())
        main = next(node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name == 'main')
        index = next(index for index, node in enumerate(main.body)
                     if isinstance(node, ast.Assign) and ast.unparse(node.targets[0]) == 'body_failure')
        exact = ast.Module(body=main.body[index:], type_ignores=[])
        body = Mock(side_effect=body_failure)
        close = Mock(side_effect=metadata_failure)
        report = Mock(side_effect=report_failure)
        namespace = dict(run_build=body, os=SimpleNamespace(chown=close), print=report,
            supervisor=object(), raw='claim', policy={}, head='head', Path=Path,
            sys=SimpleNamespace(argv=['claim-gate', '/exact/dotnet'], stderr=object()),
            clock=object(), receipt=Path('/retained-claim'), uid=1001, gid=1001)
        try:
            exec(compile(ast.fix_missing_locations(exact), '<actual-main-finalization>', 'exec'), namespace)
        finally:
            body.assert_called_once()
            close.assert_called_once_with(Path('/retained-claim'), 1001, 1001, follow_symlinks=False)
            self.report = report
    def test_actual_main_body_failure_not_replaced_by_metadata_failure(self):
        original = RuntimeError('original SDK failure')
        with self.assertRaises(RuntimeError) as caught:
            self.finalization(original, OSError('ownership fault'))
        self.assertIs(original, caught.exception)
        self.assertIn('OSError', self.report.call_args.args[0])
    def test_actual_main_cancellation_not_replaced_even_when_diagnostic_fails(self):
        original = KeyboardInterrupt('original owned interruption')
        with self.assertRaises(KeyboardInterrupt) as caught:
            self.finalization(original, OSError('ownership fault'), KeyboardInterrupt('diagnostic interrupted'))
        self.assertIs(original, caught.exception)
    def test_actual_main_success_reports_metadata_failure(self):
        metadata = OSError('ownership fault')
        with self.assertRaises(OSError) as caught:
            self.finalization(metadata_failure=metadata)
        self.assertIs(metadata, caught.exception)
    def test_actual_main_successful_finalization_is_success(self):
        self.finalization()
        self.report.assert_not_called()

if __name__ == '__main__':
    unittest.main(verbosity=2)
