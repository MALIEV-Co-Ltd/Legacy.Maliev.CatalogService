"""Pure claim/control tests: no SDK, fork, signal, cgroup or Docker execution."""
import copy
import ast
import datetime as dt
import importlib.util
import json
import io
import contextlib
from pathlib import Path
import tempfile
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
             patch.object(module, 'event_permit', return_value=json.dumps(claim)) as event, \
             patch.object(module.os, 'geteuid', create=True, side_effect=AssertionError('native allocation reached')), \
             patch.object(module.importlib.util, 'spec_from_file_location', side_effect=AssertionError('supervisor import reached')):
            module.main()
            event.return_value = ''
            with self.assertRaises(ValueError): module.main()
    def event(self, value):
        temporary = tempfile.TemporaryDirectory(prefix='catalog-claim-pure-')
        self.addCleanup(temporary.cleanup)
        target = Path(temporary.name) / 'event.json'
        target.write_bytes(value)
        return target
    def test_actual_bounded_event_preserves_literal_claim_bytes(self):
        raw = json.dumps(self.claim, indent=2) + '\n'
        target = self.event(json.dumps({'inputs': {'build_permit': raw}}).encode())
        self.assertEqual(raw, module.event_permit(str(target)))
    def test_actual_event_missing_duplicate_wrong_type_and_oversized_refused(self):
        for raw in (b'{}', b'{"inputs":{}}', b'{"inputs":{"build_permit":false}}',
                    b'{"inputs":{"build_permit":"first","build_permit":"second"}}',
                    json.dumps({'inputs': {'build_permit': 'x' * 4097}}).encode(),
                    b' ' * 1048577):
            with self.subTest(size=len(raw)):
                target = self.event(raw)
                with self.assertRaises(ValueError): module.event_permit(str(target))
    def test_actual_event_nonabsolute_symlink_and_replaced_identity_refused(self):
        with self.assertRaises(ValueError): module.event_permit('event.json')
        target = self.event(b'{"inputs":{"build_permit":"bounded"}}')
        with patch.object(module.Path, 'is_symlink', return_value=True):
            with self.assertRaises(ValueError): module.event_permit(str(target))
        real = target.stat()
        changed = SimpleNamespace(st_dev=real.st_dev, st_ino=real.st_ino + 1)
        with patch.object(module.os, 'fstat', return_value=changed):
            with self.assertRaises(ValueError): module.event_permit(str(target))
    def test_actual_mask_escapes_multiline_percent_and_workflow_commands(self):
        raw = 'first%line\r\n::warning::private-value\nlast'
        out = io.StringIO()
        with contextlib.redirect_stdout(out): module.mask_permit(raw)
        commands = out.getvalue().splitlines()
        self.assertEqual(4, len(commands))
        self.assertTrue(all(line.startswith('::add-mask::') for line in commands))
        self.assertEqual('::add-mask::first%25line%0D%0A::warning::private-value%0Alast', commands[0])
        self.assertNotIn('\r', out.getvalue())
        self.assertEqual('::add-mask::::warning::private-value', commands[2])
    def test_actual_mask_main_never_loads_policy_or_supervisor(self):
        target = self.event(b'{"inputs":{"build_permit":"synthetic-claim"}}')
        out = io.StringIO()
        with patch.dict(module.os.environ, GITHUB_EVENT_PATH=str(target), CATALOG_BUILD_EVENT_PATH=''), \
             patch.object(module.sys, 'argv', ['claim-gate', '--mask-permit']), \
             patch.object(module.Path, 'read_bytes', side_effect=AssertionError('policy source reached')), \
             patch.object(module.importlib.util, 'spec_from_file_location', side_effect=AssertionError('supervisor reached')), \
             patch.object(module.os, 'geteuid', create=True, side_effect=AssertionError('native reached')), \
             contextlib.redirect_stdout(out):
            # A nonblank explicit override is required; normal runner uses GITHUB_EVENT_PATH.
            module.os.environ.pop('CATALOG_BUILD_EVENT_PATH')
            module.main()
        self.assertEqual('::add-mask::synthetic-claim\n', out.getvalue())
    def test_actual_read_only_gate_uses_event_not_untrusted_echoed_environment(self):
        policy = json.loads((SCRIPT.parent / 'catalog-candidate-policy.json').read_bytes())
        claim = dict(self.claim, sourceManifestSha256=policy['sourceManifestSha256'], capsuleSha256=policy['capsuleSha256'])
        target = self.event(json.dumps({'inputs': {'build_permit': json.dumps(claim)}}).encode())
        instant = self.now
        class FixedClock(dt.datetime):
            @classmethod
            def now(cls, tz=None): return instant
        with patch.dict(module.os.environ, CATALOG_BUILD_EVENT_PATH=str(target), CATALOG_BUILD_PERMIT='foreign-or-expired',
                        GITHUB_SHA=self.head, GITHUB_RUN_ID='123', GITHUB_RUN_ATTEMPT='1'), \
             patch.object(module.sys, 'argv', ['claim-gate', '--verify-permit']), \
             patch.object(module.dt, 'datetime', FixedClock), \
             patch.object(module.importlib.util, 'spec_from_file_location', side_effect=AssertionError('supervisor reached')):
            module.main()
    def test_workflow_masks_first_and_never_interpolates_claim_into_environment(self):
        workflow = (SCRIPT.parents[1] / '.github/workflows/catalog-candidate-qualification.yml').read_text()
        mask = workflow.index('run_catalog_build_grant.py --mask-permit')
        self.assertLess(mask, workflow.index('Check out exact clean accepted base'))
        self.assertLess(mask, workflow.index('run_catalog_build_grant.py --verify-permit'))
        self.assertLess(mask, workflow.index('uses: actions/setup-dotnet@'))
        self.assertNotIn('${{ inputs.build_permit }}', workflow)
        self.assertNotIn('BUILD_PERMIT:', workflow)
        self.assertIn('CATALOG_BUILD_EVENT_PATH="$GITHUB_EVENT_PATH"', workflow)
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
