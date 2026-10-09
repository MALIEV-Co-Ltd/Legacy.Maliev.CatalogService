import base64
import copy
import hashlib
import importlib.util
import json
import fnmatch
import runpy
import contextlib
import signal
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).resolve().parents[1] / 'materialize_catalog_candidate.py'
spec = importlib.util.spec_from_file_location('catalog_transport', SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class TransportControls(unittest.TestCase):
    def test_actual_ordinary_seal_binds_reviewed_fixture_and_candidate_policy(self):
        repository = SCRIPT.parents[1]
        policy = module.parse((SCRIPT.parent / 'catalog-candidate-policy.json').read_bytes())
        rows = {row['path']: row for row in policy['sourceFiles']}
        seal = module.parse((SCRIPT.parent / 'catalog-ordinary-controller-seal.json').read_bytes())
        fixture = Path(__file__).with_name('fixtures') / 'catalog-v16-owned-supervisor.py.txt'
        self.assertEqual(hashlib.sha256(fixture.read_bytes()).hexdigest(), seal['sha256'])
        self.assertEqual(len(fixture.read_bytes()), seal['bytes'])
        for path in ['scripts/probe_catalog_controller.py']:
            raw = (repository / path).read_bytes()
            self.assertEqual(rows[path]['sha256'], hashlib.sha256(raw).hexdigest())
            self.assertEqual(rows[path]['bytes'], len(raw))
        loader = runpy.run_path(str(SCRIPT.parent / 'probe_catalog_controller.py'))['supervisor']
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / 'scripts').mkdir()
            (root / 'scripts/catalog-ordinary-controller-seal.json').write_bytes((SCRIPT.parent / 'catalog-ordinary-controller-seal.json').read_bytes())
            (root / seal['path']).write_bytes(fixture.read_bytes())
            self.assertEqual(600, loader(root, ordinary=True).SDK_SECONDS)
            self.assertFalse((root / 'candidate').exists())
            with self.assertRaises(FileNotFoundError):
                loader(root)  # No implicit ordinary fallback for native transport.

    def test_actual_ordinary_binding_refuses_stale_fixture_before_import(self):
        loader = runpy.run_path(str(SCRIPT.parent / 'probe_catalog_controller.py'))['supervisor']
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / 'scripts').mkdir()
            (root / 'scripts/catalog-ordinary-controller-seal.json').write_bytes((SCRIPT.parent / 'catalog-ordinary-controller-seal.json').read_bytes())
            fixture = Path(__file__).with_name('fixtures') / 'catalog-v16-owned-supervisor.py.txt'
            (root / 'scripts/run-catalog-owned-qualification.py').write_bytes(fixture.read_bytes() + b'\nraise AssertionError("must not execute")\n')
            with self.assertRaisesRegex(RuntimeError, 'seal differs'):
                loader(root, ordinary=True)

    def test_inspect_producer_fixture_changes_trigger_transport_validation(self):
        workflow = (SCRIPT.parents[1] / '.github/workflows/catalog-candidate-qualification.yml').read_text()
        trigger = workflow.split('  pull_request:\n')[1].split('  workflow_dispatch:\n')[0]
        fixture = '      - scripts/tests/fixtures/catalog-v16-owned-supervisor.py.txt'
        self.assertEqual(1, trigger.splitlines().count(fixture))

    def test_actual_inspect_writer_frames_are_retained_by_workflow_upload(self):
        repository = SCRIPT.parents[1]
        fixture = Path(__file__).with_name('fixtures') / 'catalog-v16-owned-supervisor.py.txt'
        raw = fixture.read_bytes()
        policy = json.loads((repository / 'scripts/catalog-candidate-policy.json').read_bytes())
        row, = [item for item in policy['sourceFiles']
                if item['path'] == 'scripts/run-catalog-owned-qualification.py']
        self.assertEqual(54830, len(raw))
        self.assertEqual('85082f82ea9aaf228290dca28d852202fee2348b76c1883ab5d7cbb00726e283', hashlib.sha256(raw).hexdigest())
        producer = runpy.run_path(str(fixture))
        child = 'c' * 32
        expected = {'Id': 'e' * 64, 'Created': '2026-10-08T00:00:01+00:00',
                    'Image': 'sha256:' + 'f' * 64, 'Name': '/catalog-http-' + 'd' * 32,
                    'labels': {'maliev.codex.owner': producer['OWNER']},
                    'portBindings': {'5432/tcp': [{'HostIp': '127.0.0.1', 'HostPort': ''}]}}
        value = {key: expected[key] for key in ('Id', 'Created', 'Image', 'Name')}
        value.update(Config={'Labels': expected['labels'], 'Env': ['SYNTHETIC_PRIVATE_VALUE=must-not-export']},
                     HostConfig={'Binds': [], 'Mounts': []}, State={'Running': True},
                     Mounts=[{'Type': 'tmpfs', 'Destination': '/var/lib/postgresql'}],
                     NetworkSettings={'Ports': {'5432/tcp': [{'HostIp': '127.0.0.1', 'HostPort': '45678'}]}})
        workflow = (repository / '.github/workflows/catalog-candidate-qualification.yml').read_text()
        block = workflow.split('      - name: Preserve raw test and bounded ownership evidence, never SDK private logs/cache\n')
        self.assertEqual(2, len(block))
        upload = block[1].split('          path: |\n')[1].split('          if-no-files-found:')[0]
        patterns = [line.strip() for line in upload.splitlines() if line.strip()]
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / 'candidate/runner-results/owned-focused-synthetic'
            root.mkdir(parents=True)
            # Windows substitutes only POSIX signal masking/directory fsync.
            # Filename, projection and capture use the reviewed producer;
            # hosted Linux exercises its durable writer unchanged.
            def portable_write(path, observation):
                with path.open('xb') as stream:
                    stream.write((json.dumps(observation, sort_keys=True, separators=(',', ':')) + '\n').encode())
            with contextlib.ExitStack() as stack:
                if not hasattr(signal, 'pthread_sigmask'):
                    stack.enter_context(patch.dict(producer['write_new'].__globals__,
                        registration_window=contextlib.nullcontext, _write_new=portable_write))
                for stage in ('initial', 'pre-mutation', 'stopped'):
                    value['State']['Running'] = stage != 'stopped'
                    self.assertEqual('captured', producer['capture_inspect_observation'](root, child, stage, value, expected))
            frames = list(root.glob('*.json'))
            self.assertEqual(3, len(frames))
            for frame in frames:
                relative = frame.relative_to(temporary).as_posix()
                self.assertTrue(any(fnmatch.fnmatchcase(relative, pattern) for pattern in patterns), relative)
                self.assertLessEqual(frame.stat().st_size, 8192)
                self.assertNotIn('SYNTHETIC_PRIVATE_VALUE', frame.read_text())
                self.assertFalse(frame.name.startswith('container-'))
            private = 'candidate/runner-results/owned-focused-synthetic/private-sdk.log'
            self.assertFalse(any(fnmatch.fnmatchcase(private, pattern) for pattern in patterns))

    def test_actual_workflow_routes_both_addresses_from_their_dispatch_inputs(self):
        workflow = (SCRIPT.parents[1] / '.github/workflows/catalog-candidate-qualification.yml').read_text()
        self.assertIn('MANIFEST_BLOB: ${{ inputs.manifest_blob }}', workflow)
        self.assertIn('CAPSULE_BLOB: ${{ inputs.capsule_blob }}', workflow)
        self.assertIn('--manifest-blob "$MANIFEST_BLOB" --capsule-blob "$CAPSULE_BLOB"', workflow)

    def test_actual_cli_fetches_and_validates_both_supplied_blob_addresses(self):
        import os
        import sys
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary); (root/'scripts').mkdir(); (root/'candidate').mkdir()
            (root/'scripts/catalog-candidate-policy.json').write_text(json.dumps(self.policy))
            manifest_blob, capsule_blob = '1'*40, '2'*40
            head = 'cc5280b94959b8f6cfc924ff83357274cd65103b'
            argv = ['materializer', '--manifest-blob', manifest_blob, '--capsule-blob', capsule_blob]
            def fetch(blob):
                return {manifest_blob: self.manifest, capsule_blob: self.raw}[blob]
            with patch.object(module, '__file__', str(root/'scripts/materialize_catalog_candidate.py')), patch.object(sys, 'argv', argv), patch.dict(os.environ, GITHUB_REPOSITORY=module.REPOSITORY, GITHUB_SHA=head), patch.object(module, 'git', return_value=head.encode()), patch.object(module, 'fetch', side_effect=fetch) as fetched, patch.object(module, 'materialize') as materialized:
                module.main()
                self.assertEqual([manifest_blob, capsule_blob], [call.args[0] for call in fetched.call_args_list])
                self.assertEqual(49, len(materialized.call_args.args[2]))
            receipt = json.loads((root/'evidence/source-materialization.json').read_text())
            self.assertEqual(manifest_blob, receipt['manifestBlob'])
            self.assertEqual(capsule_blob, receipt['capsuleBlob'])
            self.assertFalse(receipt['nativeValidated'])

    def setUp(self):
        self.manifest = b'reviewed synthetic manifest'
        self.files = [{'path': f'source/{number}.cs', 'content': 'literal\r\n'} for number in range(49)]
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
    def test_exact26_preserves_crlf(self):
        result = self.validate()
        self.assertEqual(49, len(result))
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
    def test_committed_policy_is_exact_catalog_v26_collector_inventory(self):
        policy = module.parse((SCRIPT.parent / 'catalog-candidate-policy.json').read_bytes())
        self.assertEqual(module.REPOSITORY, policy['repository'])
        self.assertEqual(module.BASE, policy['acceptedBase'])
        self.assertEqual(module.SOURCE_PINS, policy['sourcePins'])
        self.assertEqual(49, len(policy['sourceFiles']))
        self.assertEqual(49, len({row['path'] for row in policy['sourceFiles']}))
        self.assertEqual('5998b162b47c55e5198d2af19dc3483afa42e7bab07e8c10912af9339016e0c6', policy['sourceManifestSha256'])
        self.assertEqual('6d62cef8e8690b414af75e291946dbb2595396a056a8a727fff58130abb35634', policy['capsuleSha256'])
        rows = {row['path']: row for row in policy['sourceFiles']}
        for path, size, sha in (('.github/workflows/_build-and-test.yml', 5653, 'c7a6220f253e1dc8e79b475dc23cbfd6bcc012a7b0cc998744e2dc4602109422'), ('.github/workflows/catalog-owned-qualification.yml', 2925, '616d1eca0195bef1a35a5aafc9f5d77e5f2b233bf2627f1630e34b41a5dce4bb'), ('docs/catalog-owned-qualification-source-20261008.md', 29766, 'fb3f4bf61112824132ce312a7d8a151ec480f6a6a730cd9a0ff8272e30f0996b'), ('docs/material-group-literal-name-source-parity-20261008.md', 3171, '3f8a71164210a34f7d8db54c0b39d9236e0d9db515bbb1cd2ff5423f36df5853'), ('Legacy.Maliev.CatalogService.Application/Models/CatalogModels.cs', 7402, 'c8980aadfbcf2a65518522aee08f994c93288861ff2727686f4d3e31c4e62399'), ('Legacy.Maliev.CatalogService.Tests/CatalogResourceAssembly.cs', 327, '86a9bc214a3bc1619d3c22c83d32c754b69e534a057b72a640fa8f0ee383f63c'), ('Legacy.Maliev.CatalogService.Tests/Integration/AdditiveMaterialReconciliationStartupTests.cs', 26623, '90447b98454b86b53c33622fd7af230ec05353731dde7c59066f404e00e20298'), ('Legacy.Maliev.CatalogService.Tests/Integration/CatalogEntrypointSettlement.cs', 4938, '0ba30672ea3346d9ee45faf9c0657bfc7e7004ccf211eb71e33a5c49ba3baf05'), ('Legacy.Maliev.CatalogService.Tests/Integration/CatalogHttpLifecycleTests.cs', 46328, '2f10ff2acc324bc8875046eab1979271221ffa75df52a0c59795f74dbc97f638'), ('Legacy.Maliev.CatalogService.Tests/Integration/CatalogMaterialCollectionFailureHttpTests.cs', 12153, 'db3ac02f997b803474ac2ba88809225702709d8a5bbd1295b0cedcb1594f4bab'), ('Legacy.Maliev.CatalogService.Tests/Integration/CatalogMethodProgressFramework.cs', 7073, '10bf6ca33620988e859467d130bb0003c130d219c0993c0483150a71be2ee52f'), ('Legacy.Maliev.CatalogService.Tests/Integration/CatalogNativeScope.cs', 10572, '4599193298b8cacbeb15670229379ccc8c509f7cc7f2db3cf7fb4da8ba353b82'), ('Legacy.Maliev.CatalogService.Tests/Integration/CatalogOwnedPostgres.cs', 13180, '45120461ef895cc3a7ecd00300314c8a6373ac2a44a59a0be136c167da7ecbe4'), ('Legacy.Maliev.CatalogService.Tests/Integration/CatalogOwnedRedis.cs', 12416, 'a408c157f8a11a022dafff5651f1920c9b0b8d96911e555ab2f2f90e290b17d0'), ('Legacy.Maliev.CatalogService.Tests/Integration/CatalogOwnedRedisConnection.cs', 1354, '1bbacf811a2d1015b1ee86a057c444c85804c71dd50a66384252d2f4a8895f30'), ('Legacy.Maliev.CatalogService.Tests/Integration/CatalogResourceLifetime.cs', 19974, '901a0155ad789fa5150b13ac60c63da5f0c07a9a52ed6d9b51fc50d0c0851276'), ('Legacy.Maliev.CatalogService.Tests/Integration/CatalogResourceLifetimeTests.cs', 18844, '6af08249463622988319f181d41cc68334fb48e5167524593886d3fcab6b19c3'), ('Legacy.Maliev.CatalogService.Tests/Integration/CatalogSdkOwnership.cs', 5454, '4f02c026b5f77a4ba66268f6b2fd5266d662f836ff5793f336a16a06c97ee21d'), ('Legacy.Maliev.CatalogService.Tests/Integration/MaterialCollectionFailureFixture.cs', 15295, '5e361a9fafce427e9746ebb00494295478af8684a52517dd9901933f44c9b7a8'), ('Legacy.Maliev.CatalogService.Tests/Integration/PostgreSqlMigrationTests.cs', 9632, 'c73370f2dad340bed8178c3e06d0e5963cfcff3e2d22087ef1ce0ab0c5f2d1a0'), ('Legacy.Maliev.CatalogService.Tests/Integration/ReadOnlyCatalogStartupFixture.cs', 12664, 'b76a1f8f64e3306e77c9900a6aeb2cc915baf0d17e9ce35d0bf86ef5aeb9b909'), ('Legacy.Maliev.CatalogService.Tests/Integration/ReadOnlyCatalogStartupTests.cs', 12139, '324b4ff5019452d066ec867f260ad948add06e95cda39c4ed5116d971225d1ea'), ('Legacy.Maliev.CatalogService.Tests/Lookups/ThaiAddressLookupTests.cs', 12518, 'c376c9b5e0b85a4ebdd1cfd249b7c570c7bc71c156623925e1f05a64c428b3ab'), ('scripts/catalog-ordinary-controller-seal.json', 185, '842fda15e857fd1f51f4f867813f72311e6e250757f30cdcad919c2c235305b1'), ('scripts/catalog_method_progress_projection.py', 4510, 'c073c4aa14418315c8459bbba5f212b36aba9ddb52b1fde4d3ab105f3cc9d43a'), ('scripts/catalog_platform_progress_projection.py', 7951, '4d9058c01437cbe274e33e73cc91670ae25be67edb59ec882ee3c88fe5c93089'), ('scripts/country-health-diagnostic-expected.json', 2090, '5238291032265b16d3739058a1db6451525de7c363f09959dae90162ae019125'), ('scripts/probe_catalog_controller.py', 18135, 'bfa2ea6082912f270c8a6bc208923c5750fa81ec2be8232305fdb0035d3abe85'), ('scripts/run-catalog-owned-qualification.py', 66648, '26ebd7cc5036eadd959dce31f582c91ef18d3e8cc5625893c9b6e5d8ac4d78b1'), ('scripts/test-catalog-owned-qualification.py', 63884, '9f878479d4c65e525fcbc97c59bd702c990d9cc586dfd6f6c9a3d16cdafa5a63'), ('scripts/test_catalog_method_progress_projection.py', 6942, '6cd33d40bbc9fcb184b29cf326c343b00268e45196f3520ba67901ffa0587ffa'), ('scripts/test_catalog_platform_progress_projection.py', 15638, '589a4321694a6de809e236202b31dbada4386ea62196d7ec1e18b5c6de614b8a'), ('Legacy.Maliev.CatalogService.Data/FrankfurterExchangeRateClient.cs', 1484, 'ac87ba56e81132481686691e3131739259c6bf112e1777544bcf1d5ebd3d80a9'), ('Legacy.Maliev.CatalogService.Tests/Data/FrankfurterExchangeRateClientTests.cs', 1498, '8cabc47965a162778ef14807114177f99aaa3de98454ad7c8d3a711c1ed83bd1'), ('Legacy.Maliev.CatalogService.Tests/Integration/CatalogExchangeRateHttpTests.cs', 9166, '990854f812bed9085b170e7cdde4aa1e8ec2031ccb24b92fc21d878c33ba450f'), ('Legacy.Maliev.CatalogService.Tests/Integration/MaterialGroupNameSourceHttpTests.cs', 4967, 'b7e503e6f3c8ed90311605b9e539722d39dc4607ad79ea9b38e6d210b66d34be'), ('Legacy.Maliev.CatalogService.Tests/Integration/MaterialResourceNameSourceHttpTests.cs', 12085, '28eaa1a3e8a1b3c846c78e9490862e22d64d9d4022a9acdfb7f804ab5d942e47'), ('Legacy.Maliev.CatalogService.Tests/Integration/CountryCurrencyNameSourceHttpTests.cs', 7335, 'e9e8731420c5ed03015bbe270d7a4702fde74c5e7ceb2e2ac808e6372abbaf3f'), ('Legacy.Maliev.CatalogService.Application/Interfaces/ICatalogRepository.cs', 2714, '519c9bf9da2374b63c0fc64f0b64cd720dc169065498149d1668bb6c675eece5'), ('Legacy.Maliev.CatalogService.Application/Services/CatalogApplicationService.cs', 25773, '46593f78c6a02b5564dbe19919ea12ab244fee8c4238697c4b7049a968b952d9'), ('Legacy.Maliev.CatalogService.Data/CatalogRepository.cs', 5987, '1fb708084d2658684db8a885455842a010def7196ec565dc1c81cba9c03a9b0b'), ('Legacy.Maliev.CatalogService.Tests/Integration/MaterialAssociationExistenceSourceHttpTests.cs', 6874, 'f9ca2f50bf1d10b7b68167a2927dc70e5ea83100934ebfa09e321cc5b2b16e53'), ('Legacy.Maliev.CatalogService.Tests/Integration/FilteredMaterialSourceHttpTests.cs', 6286, '9f7710a942f51e8b69f3d5436456146ccd734498db2ac6f5156de77cfdb47961'), ('Legacy.Maliev.CatalogService.Tests/Integration/MaterialZeroSizeSourceHttpTests.cs', 6821, 'dbfb85176127e2a224bfbe31aa8635c8e3336e710239c1c99a38ceed7410055a'), ('Legacy.Maliev.CatalogService.Tests/Integration/CatalogReferenceCollectionFailureHttpTests.cs', 12149, '06ae2bba6daeafa799d7b328973427bbea84ebfe376af2c464d3fcb1a5db7964'), ('Legacy.Maliev.CatalogService.Tests/Integration/MaterialDeleteIntegrityHttpTests.cs', 7113, '5c637062fc709b1da4966cc4d97997e28d37fbfe3fe0e2dbdc058f7b2c09d78e'), ('Legacy.Maliev.CatalogService.Tests/Integration/MaterialLinkedCollectionSourceHttpTests.cs', 6896, '9639a628c9be10cf7e6fa812b9afc34aff1fc2665a5b270d1b2add793b4da071'), ('Legacy.Maliev.CatalogService.Application/Models/LegacyQuotedBooleanJsonConverter.cs', 855, '15bddea4f0c58ddf5d347832ed5d86dae6ce8b45b94a50ee0561cdff64f25737'), ('Legacy.Maliev.CatalogService.Tests/Integration/MaterialQuotedBooleanSourceHttpTests.cs', 8915, '5a74e2cec371ec3939b977a32b3d8d0f83e9ce214d38405e7a6ad578767e84c2')):
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
