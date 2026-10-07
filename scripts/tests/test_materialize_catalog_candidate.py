import base64
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).resolve().parents[1] / 'materialize_catalog_candidate.py'
spec = importlib.util.spec_from_file_location('catalog_transport', SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class TransportControls(unittest.TestCase):
    def setUp(self):
        self.manifest = b'reviewed synthetic manifest'
        self.files = [{'path': f'source/{number}.cs', 'content': 'literal\r\n'} for number in range(24)]
        self.capsule = {'schemaVersion': 1, 'repository': module.REPOSITORY, 'acceptedBase': module.BASE, 'sourceFiles': self.files}
        self.raw = json.dumps(self.capsule).encode()
        self.policy = {'schemaVersion': 1, 'repository': module.REPOSITORY, 'acceptedBase': module.BASE,
            'sourcePins': module.SOURCE_PINS,
            'sourceManifestSha256': module.digest(self.manifest), 'capsuleSha256': module.digest(self.raw),
            'sourceFiles': [{'path': row['path'], 'bytes': len(row['content'].encode()),
                             'sha256': module.digest(row['content'].encode())} for row in self.files]}
    def validate(self, capsule=None, policy=None):
        raw = self.raw if capsule is None else json.dumps(capsule).encode()
        policy = copy.deepcopy(self.policy if policy is None else policy)
        if capsule is not None:
            policy['capsuleSha256'] = module.digest(raw)
        return module.validate(raw, self.manifest, policy)
    def test_exact24_preserves_crlf(self):
        result = self.validate()
        self.assertEqual(24, len(result))
        self.assertEqual(b'literal\r\n', result['source/0.cs'])
    def test_foreign_repository_base_schema_rejected(self):
        for key, value in [('repository', 'foreign/repo'), ('acceptedBase', '0' * 40), ('schemaVersion', True)]:
            with self.subTest(key=key):
                capsule = copy.deepcopy(self.capsule)
                capsule[key] = value
                with self.assertRaises(ValueError):
                    self.validate(capsule)
    def test_foreign_paths_duplicate_missing_and_extra_refused(self):
        for name in ['../escape', '.git/config', '/absolute', 'C:/foreign', 'source\\foreign', 'source/./x']:
            with self.subTest(name=name):
                capsule = copy.deepcopy(self.capsule)
                capsule['sourceFiles'][0]['path'] = name
                with self.assertRaises(ValueError):
                    self.validate(capsule)
        for change in ['duplicate', 'missing', 'extra']:
            with self.subTest(change=change):
                capsule = copy.deepcopy(self.capsule)
                if change == 'duplicate':
                    capsule['sourceFiles'][1] = capsule['sourceFiles'][0]
                elif change == 'missing':
                    capsule['sourceFiles'].pop()
                else:
                    capsule['sourceFiles'].append(capsule['sourceFiles'][0])
                with self.assertRaises(ValueError):
                    self.validate(capsule)
    def test_digest_and_size_failclosed(self):
        for field, value in [('sha256', '0' * 64), ('bytes', True), ('bytes', module.MAX_FILE + 1)]:
            with self.subTest(field=field, value=value):
                policy = copy.deepcopy(self.policy)
                policy['sourceFiles'][0][field] = value
                with self.assertRaises(ValueError):
                    self.validate(policy=policy)
        with self.assertRaises(ValueError):
            module.validate(self.raw + b' ', self.manifest, self.policy)
        with self.assertRaises(ValueError):
            module.validate(self.raw, self.manifest + b' ', self.policy)
    def test_blob_actual_git_oid_size_and_encoding(self):
        value = b'raw\r\n'
        oid = hashlib.sha1(b'blob 5\0' + value).hexdigest()
        row = {'sha': oid, 'encoding': 'base64', 'size': len(value), 'content': base64.b64encode(value).decode()}
        self.assertEqual(value, module.decode_blob(json.dumps(row).encode(), oid))
        for key, wrong in [('sha', '0' * 40), ('encoding', 'raw'), ('size', True), ('content', '!')]:
            with self.subTest(key=key):
                changed = dict(row, **{key: wrong})
                with self.assertRaises(ValueError):
                    module.decode_blob(json.dumps(changed).encode(), oid)
    def test_recursive_duplicate_json_and_redirect_refused(self):
        with self.assertRaises(ValueError):
            module.parse(b'{"outer":{"id":1,"id":2}}')
        with self.assertRaises(ValueError):
            module.NoRedirect().redirect_request(None)
    def test_dirty_or_foreign_base_before_any_source_write(self):
        with tempfile.TemporaryDirectory(prefix='catalog-transport-pure-') as temporary:
            root = Path(temporary)
            for responses in [(b'0' * 40, b''), (module.BASE.encode(), b'M dirty')]:
                with self.subTest(responses=responses), patch.object(module, 'git', side_effect=responses):
                    with self.assertRaises(ValueError):
                        module.materialize(root, self.policy, self.validate())
                    self.assertEqual([], list(root.iterdir()))
    def test_foreign_dependency_pin_refused(self):
        policy = copy.deepcopy(self.policy)
        policy['sourcePins'][next(iter(policy['sourcePins']))] = '0' * 40
        with self.assertRaisesRegex(ValueError, 'dependency pins'):
            self.validate(policy=policy)
    def test_committed_policy_is_exact_catalog_v8_inventory(self):
        policy = module.parse((SCRIPT.parent / 'catalog-candidate-policy.json').read_bytes())
        self.assertEqual(module.REPOSITORY, policy['repository'])
        self.assertEqual(module.BASE, policy['acceptedBase'])
        self.assertEqual(module.SOURCE_PINS, policy['sourcePins'])
        self.assertEqual(24, len(policy['sourceFiles']))
        self.assertEqual(24, len({row['path'] for row in policy['sourceFiles']}))
        self.assertEqual('b39e3c4af055cf9c8275492fad2982eafbf5b00528e547a427db5fa0bf4466e9', policy['sourceManifestSha256'])
        self.assertEqual('5bfe305a962f28216c709d744d8b004825fc7a78039d6b7d2a1c82cf7f35d09c', policy['capsuleSha256'])
        rows = {row['path']: row for row in policy['sourceFiles']}
        for path, size, sha in (
            ('scripts/run-catalog-owned-qualification.py', 43392, '33e84c4311ebe843a8692048e1715436fbea3c7ef56c9c12e92bc764b109df8c'),
            ('scripts/test-catalog-owned-qualification.py', 20619, 'bf9420f68bba18d8aca733e4a8366231740de33471f520b95fdfbbf075839613'),
            ('docs/catalog-owned-qualification-source-20261008.md', 5102, '67d5269b902c60ac773964ed7f80efaceab17b409e27ce46c38b146205c12793')):
            self.assertEqual(size, rows[path]['bytes'])
            self.assertEqual(sha, rows[path]['sha256'])
    def test_workflow_keeps_pr_pure_and_native_main_only(self):
        workflow = (SCRIPT.parents[1] / '.github/workflows/catalog-candidate-qualification.yml').read_text()
        self.assertIn("if: github.event_name == 'workflow_dispatch' && github.ref == 'refs/heads/main'", workflow)
        self.assertIn('needs: transport-controls', workflow)
        self.assertIn('for phase in build focused full format audit;', workflow)
        self.assertIn('scripts/materialize_catalog_candidate.py --verify-only', workflow)
        self.assertIn('contents: read', workflow)
        self.assertNotIn('continue-on-error', workflow)
        for pin in module.SOURCE_PINS.values():
            self.assertIn(pin, workflow)

if __name__ == '__main__':
    unittest.main(verbosity=2)
