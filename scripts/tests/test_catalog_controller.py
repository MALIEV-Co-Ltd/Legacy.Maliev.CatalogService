import copy
import ast
import importlib.util
import os
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import Mock, patch

SCRIPT = Path(__file__).resolve().parents[1] / 'probe_catalog_controller.py'
spec = importlib.util.spec_from_file_location('catalog_controller', SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

def identity(path):
    value = path.stat()
    return value.st_dev, value.st_ino

def fd_identity(fd):
    value = os.fstat(fd)
    return value.st_dev, value.st_ino

class ControllerControls(unittest.TestCase):
    def setUp(self):
        for name, value in [('SIG_BLOCK', 0), ('SIG_SETMASK', 2)]:
            change = patch.object(module.signal, name, value, create=True)
            change.start()
            self.addCleanup(change.stop)
        change = patch.object(module.signal, 'pthread_sigmask', return_value=set(), create=True)
        change.start()
        self.addCleanup(change.stop)
    def test_actual_snapshot_shape_and_each_identity_boundary(self):
        snapshot = {'pid': 123, 'uid': 1001, 'gid': 1001, 'groups': [1001, 1234],
                    'noNewPrivileges': '1', 'cgroup': '0::/maliev-catalog-' + 'a' * 32}
        module.check_snapshot(snapshot, 123, 'a' * 32, 1001, 1001, [1001, 1234])
        for key, wrong in [('pid', 124), ('uid', 0), ('gid', 0), ('groups', [1001]),
                           ('noNewPrivileges', '0'), ('cgroup', '0::/foreign')]:
            with self.subTest(key=key):
                changed = copy.deepcopy(snapshot)
                changed[key] = wrong
                with self.assertRaises(RuntimeError):
                    module.check_snapshot(changed, 123, 'a' * 32, 1001, 1001, [1001, 1234])
        with self.assertRaises(RuntimeError):
            module.check_snapshot(dict(snapshot, extra=True), 123, 'a' * 32, 1001, 1001, [1001, 1234])
    def ownership(self, root):
        group = root / 'own-group'
        group.mkdir()
        slot = root / 'own-slot'
        fd = os.open(slot, os.O_CREAT | os.O_EXCL | os.O_RDWR, 0o600)
        return {'group': group, 'slot': slot, 'groupAttempted': True, 'groupCreated': True,
            'slotCreated': True, 'groupIdentity': identity(group), 'slotIdentity': identity(slot),
            'groupFd': None, 'slotFd': fd, 'child': None, 'pidfd': None, 'reaped': False,
            'handles': {fd: fd_identity(fd)}}
    def primitives(self):
        return SimpleNamespace(identity=identity, descriptor_identity=fd_identity,
            current_group_empty=lambda _: True,
            retained_creation_identity=lambda path, fd: fd_identity(fd))
    def test_exact_owned_release_has_actual_absence(self):
        with tempfile.TemporaryDirectory(prefix='catalog-controller-pure-') as temporary:
            owned = self.ownership(Path(temporary))
            self.assertIs(True, module.cleanup(self.primitives(), owned))
            self.assertFalse(owned['group'].exists())
            self.assertFalse(owned['slot'].exists())
            self.assertEqual({}, owned['handles'])
    def test_failed_close_retains_slot_and_retries_same_handle(self):
        with tempfile.TemporaryDirectory(prefix='catalog-controller-pure-') as temporary:
            owned = self.ownership(Path(temporary))
            descriptor = owned['slotFd']
            other = os.open(Path(temporary) / 'other-owned-file', os.O_CREAT | os.O_EXCL | os.O_RDWR, 0o600)
            owned['handles'][other] = fd_identity(other)
            real_close = os.close
            def release(fd):
                if fd == descriptor:
                    raise OSError('controlled close fault')
                real_close(fd)
            close = Mock(side_effect=release)
            with patch.object(module.os, 'close', close):
                with self.assertRaises(OSError):
                    module.cleanup(self.primitives(), owned)
            self.assertTrue(owned['slot'].exists())
            self.assertIn(descriptor, owned['handles'])
            self.assertNotIn(other, owned['handles'])
            self.assertEqual(2, close.call_count)
            self.assertIs(True, module.cleanup(self.primitives(), owned))
    def test_unknown_unacknowledged_group_cannot_release_claim(self):
        with tempfile.TemporaryDirectory(prefix='catalog-controller-pure-') as temporary:
            owned = self.ownership(Path(temporary))
            owned['groupCreated'] = False
            try:
                with self.assertRaisesRegex(RuntimeError, 'Unacknowledged'):
                    module.cleanup(self.primitives(), owned)
                self.assertTrue(owned['slot'].exists())
                self.assertTrue(owned['group'].exists())
            finally:
                os.close(owned['slotFd'])
    def test_changed_group_identity_never_removes_foreign_path(self):
        with tempfile.TemporaryDirectory(prefix='catalog-controller-pure-') as temporary:
            owned = self.ownership(Path(temporary))
            owned['groupIdentity'] = (-1, -1)
            try:
                with self.assertRaisesRegex(RuntimeError, 'identity differs'):
                    module.cleanup(self.primitives(), owned)
                self.assertTrue(owned['slot'].exists())
                self.assertTrue(owned['group'].exists())
            finally:
                os.close(owned['slotFd'])
    def test_unreaped_child_fences_all_dependency_and_claim_removal(self):
        with tempfile.TemporaryDirectory(prefix='catalog-controller-pure-') as temporary:
            owned = self.ownership(Path(temporary))
            owned['child'] = 999999
            try:
                with patch.object(module.os, 'WNOHANG', 1, create=True), patch.object(module.signal, 'SIGKILL', 9, create=True), \
                     patch.object(module.os, 'waitpid', return_value=(0, 0)), patch.object(module.os, 'kill') as kill, \
                     patch.object(module, 'wait_child', side_effect=TimeoutError('unreaped')):
                    with self.assertRaises(TimeoutError):
                        module.cleanup(self.primitives(), owned)
                    kill.assert_called_once()
                self.assertTrue(owned['slot'].exists())
                self.assertTrue(owned['group'].exists())
            finally:
                os.close(owned['slotFd'])
    def test_workflow_controller_precedes_sdk_and_smoke_skips_all_sdk(self):
        text = (SCRIPT.parents[1] / '.github/workflows/catalog-candidate-qualification.yml').read_text()
        self.assertLess(text.index('Actual controller-only admission'), text.index('uses: actions/setup-dotnet'))
        self.assertIn('default: controller-smoke', text)
        self.assertEqual(4, text.count("if: inputs.mode == 'sealed-native'"))
        self.assertNotIn('continue-on-error', text)
    def test_already_reaped_child_requires_original_readable_pidfd(self):
        with tempfile.TemporaryDirectory(prefix='catalog-controller-pure-') as temporary:
            owned = self.ownership(Path(temporary))
            owned.update(child=999999, pidfd=owned['slotFd'])
            with patch.object(module.os, 'WNOHANG', 1, create=True), \
                 patch.object(module.os, 'waitpid', side_effect=ChildProcessError()), \
                 patch.object(module.select, 'select', return_value=([owned['pidfd']], [], [])):
                self.assertIs(True, module.cleanup(self.primitives(), owned))
                self.assertTrue(owned['reaped'])
    def test_unavailable_wait_active_pidfd_retains_ownership(self):
        with tempfile.TemporaryDirectory(prefix='catalog-controller-pure-') as temporary:
            owned = self.ownership(Path(temporary))
            owned.update(child=999999, pidfd=owned['slotFd'])
            try:
                with patch.object(module.os, 'WNOHANG', 1, create=True), \
                     patch.object(module.os, 'waitpid', side_effect=ChildProcessError()), \
                     patch.object(module.select, 'select', return_value=([], [], [])):
                    with self.assertRaisesRegex(RuntimeError, 'still active'):
                        module.cleanup(self.primitives(), owned)
                self.assertTrue(owned['slot'].exists())
            finally:
                os.close(owned['slotFd'])
    def test_actual_acquisition_statements_defer_cancel_until_owner_published(self):
        tree = ast.parse(SCRIPT.read_text())
        function = next(node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name == 'probe')
        windows = [node for node in ast.walk(function) if isinstance(node, ast.With) and
                   any(isinstance(item.context_expr, ast.Call) and isinstance(item.context_expr.func, ast.Name) and
                       item.context_expr.func.id == 'registration_window' for item in node.items)]
        for acquisition in ('open-slot', 'open-group', 'pipe', 'fork', 'pidfd_open'):
            with self.subTest(acquisition=acquisition):
                pending = {'value': False}
                owned = {'slot': 'own-slot', 'slotCreated': False, 'slotFd': None, 'groupCreated': False,
                         'group': SimpleNamespace(mkdir=lambda: None), 'groupFd': None, 'child': None,
                         'pidfd': None, 'handles': {}}
                def returned(value):
                    pending['value'] = True  # Cancellation exactly after kernel return.
                    return value
                fake_os = SimpleNamespace(O_CREAT=0, O_EXCL=0, O_RDWR=0, O_RDONLY=0, O_DIRECTORY=0, O_NOFOLLOW=0,
                    open=lambda *args: returned(42), pipe=lambda: returned((43, 44)),
                    fork=lambda: returned(123), pidfd_open=lambda *args: returned(45))
                target = next(node for node in windows if
                    (acquisition == 'open-slot' and "owned['slot']" in ast.unparse(node)) or
                    (acquisition == 'open-group' and '.mkdir()' in ast.unparse(node)) or
                    (acquisition == 'pipe' and 'os.pipe()' in ast.unparse(node)) or
                    (acquisition == 'fork' and 'os.fork()' in ast.unparse(node)) or
                    (acquisition == 'pidfd_open' and 'os.pidfd_open(' in ast.unparse(node)))
                def mask(kind, previous):
                    if kind == module.signal.SIG_SETMASK and pending['value']:
                        raise KeyboardInterrupt('deferred controlled cancellation')
                    return set()
                with patch.object(module.signal, 'pthread_sigmask', side_effect=mask):
                    with self.assertRaises(KeyboardInterrupt):
                        exec(compile(ast.fix_missing_locations(ast.Module(body=[target], type_ignores=[])),
                                     str(SCRIPT), 'exec'), {'registration_window': module.registration_window,
                                         'os': fake_os, 'owned': owned, 'child': 123})
                if acquisition == 'open-slot':
                    self.assertTrue(owned['slotCreated'])
                    self.assertEqual(42, owned['slotFd'])
                    self.assertIn(42, owned['handles'])
                elif acquisition == 'open-group':
                    self.assertTrue(owned['groupCreated'])
                    self.assertEqual(42, owned['groupFd'])
                    self.assertIn(42, owned['handles'])
                elif acquisition == 'pipe':
                    self.assertEqual({43, 44}, set(owned['handles']))
                elif acquisition == 'fork':
                    self.assertEqual(123, owned['child'])
                else:
                    self.assertEqual(45, owned['pidfd'])
                    self.assertIn(45, owned['handles'])

if __name__ == '__main__':
    unittest.main(verbosity=2)
