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
    def test_committed_policy_is_exact_catalog_v11_inventory(self):
        policy = module.parse((SCRIPT.parent / 'catalog-candidate-policy.json').read_bytes())
        self.assertEqual(module.REPOSITORY, policy['repository'])
        self.assertEqual(module.BASE, policy['acceptedBase'])
        self.assertEqual(module.SOURCE_PINS, policy['sourcePins'])
        self.assertEqual(24, len(policy['sourceFiles']))
        self.assertEqual(24, len({row['path'] for row in policy['sourceFiles']}))
        self.assertEqual('1163b9f2f22bf3cec518bf74b540524efe1a514fa61efafe56bd5d04d04d2376', policy['sourceManifestSha256'])
        self.assertEqual('498cc3d359690a9d1b8711808e0c1b900174311f29dcd58e690d265ccf8b07ca', policy['capsuleSha256'])
        rows = {row['path']: row for row in policy['sourceFiles']}
        for path, size, sha in (
            ('scripts/run-catalog-owned-qualification.py', 48031, '2bedc53e0e6133070be84331761f3b756947ddaa9128098a6c1bfad8d846b851'),
            ('scripts/test-catalog-owned-qualification.py', 32714, '34352467bc18184247608927c1749fc47e83a430707e1d984acd77c441130363'),
            ('docs/catalog-owned-qualification-source-20261008.md', 9784, '09330c3562ea6d2029550594fb863d99c2cff425cba83f227b772a7bf196936f'),
            ('Legacy.Maliev.CatalogService.Tests/Integration/CatalogOwnedPostgres.cs', 13101, 'a88215463abda2c476d5d13e544317067e024573fc0e806a7ce20b5a45c3ab6a'),
            ('Legacy.Maliev.CatalogService.Tests/Integration/CatalogOwnedRedis.cs', 12167, '577e3244790fdeefc9b87cfb9f949a804ed6990da267c87514c2cafaab3b1977')):
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
