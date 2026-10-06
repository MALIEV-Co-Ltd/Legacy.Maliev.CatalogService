"""Offline synthetic controls only; never native runtime or migration acceptance."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent


def module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def main():
    reader = module(ROOT / 'scripts/retain-country-health-diagnostics.py', 'diagnostic_reader_controls')
    native = module(ROOT / 'scripts/verify-country-health-diagnostics.py', 'diagnostic_native_controls')
    manifest = json.loads((ROOT / 'scripts/country-health-diagnostic-expected.json').read_text())
    passed = []

    def encode(value):
        return (json.dumps(value, ensure_ascii=True, separators=(',', ':')) + '\n').encode()

    valid = {'Id': 1, 'Name': 'Synthetic ISO diagnostic', 'Iso2': 'TH', 'Iso3': 'THA',
        'CreatedDate': '2026-08-01T00:00:00', 'ModifiedDate': '2026-08-01T00:00:00'}
    assert reader.country(encode(valid)) == valid
    passed.append('valid-bounded-synthetic-country')
    invalid = [
        ('privacy-field', encode(dict(valid, CustomerEmail='synthetic@invalid'))),
        ('boolean-id', encode(dict(valid, Id=True))),
        ('wrong-name', encode(dict(valid, Name='unexpected'))),
        ('nonnullable-code-type', encode(dict(valid, Iso2=12))),
        ('oversized-code', encode(dict(valid, Iso3='four'))),
        ('invalid-timestamp', encode(dict(valid, CreatedDate='yesterday'))),
        ('unexpected-metadata', encode(dict(valid, CountryCode='unexpected'))),
        ('duplicate-key', encode(valid).replace(b'"Id":1', b'"Id":1,"Id":2')),
        ('non-utf8', b'\xff'),
        ('nonfinite', encode(dict(valid, Id=1)).replace(b'"Id":1', b'"Id":NaN')),
        ('unpaired-surrogate', encode(dict(valid, Iso2='\ud800'))),
    ]
    for name, data in invalid:
        try:
            reader.country(data)
        except (ValueError, UnicodeError):
            passed.append('reject-' + name)
        else:
            raise AssertionError('Accepted invalid synthetic country: ' + name)

    with tempfile.TemporaryDirectory(prefix='catalog-source-diagnostic-controls-') as temporary:
        repository = Path(temporary) / 'repository'
        repository.mkdir()
        for source in reader.SOURCES:
            target = repository / source
            target.parent.mkdir(parents=True, exist_ok=True)
            origin = ROOT / source
            target.write_bytes(origin.read_bytes().replace(b'\r\n', b'\n') if origin.exists()
                else b'// Offline synthetic placeholder; never producer source evidence.\n')
        commands = [('init',), ('-c', 'core.autocrlf=false', 'add', '--', *reader.SOURCES),
            ('-c', 'user.name=Offline synthetic control', '-c', 'user.email=synthetic@invalid',
                '-c', 'commit.gpgsign=false', 'commit', '-m', 'Synthetic controls only')]
        for arguments in commands:
            subprocess.run(['git', '-C', str(repository), *arguments], check=True, capture_output=True, timeout=30)
        head = subprocess.check_output(['git', '-C', str(repository), 'rev-parse', 'HEAD'], timeout=30).decode().strip()
        results = repository / 'synthetic-results'
        exports = results / 'country-health-diagnostics'
        exports.mkdir(parents=True)
        for case, requested in manifest['cases'].items():
            stored = list(requested)  # Synthetic parser-positive fixture only, never actual fixed-width runtime evidence.
            created = dict(valid, Iso2=requested[0], Iso3=requested[1])
            fresh = dict(valid, Iso2=stored[0], Iso3=stored[1])
            for kind, value in (('created', created), ('fresh', fresh),
                    ('repeated', dict(created, Id=2)), ('after-put', fresh), ('after-db', fresh)):
                (exports / (case + '-' + kind + '.json')).write_bytes(encode(value))
            observation = {'SyntheticOnly': True, 'DiagnosticOnly': True, 'SourceLiteralParityObserved': requested == stored,
                'RequestedIso2': requested[0], 'RequestedIso3': requested[1], 'StoredIso2': stored[0], 'StoredIso3': stored[1],
                'Iso2ColumnType': 'character(2)', 'Iso3ColumnType': 'character(3)', 'CountryRows': 2,
                'CatalogCountryRows': 0, 'CurrencyRows': 0, 'ActualAuthProducerJoinProven': False}
            (exports / (case + '-observation.json')).write_bytes(encode(observation))
        for case, (path, healthy) in manifest['health'].items():
            body = b'Healthy' if case == 'liveness' else encode({'status': 'Healthy',
                'checks': {'self': {'status': 'Healthy', 'duration': 0}}, 'totalDuration': 0}) if case == 'readiness' else b''
            (exports / ('health-' + case + '-body.bin')).write_bytes(body)
            (exports / ('health-' + case + '-observation.json')).write_bytes(encode({'SyntheticOnly': True,
                'DiagnosticOnly': True, 'Path': path, 'Status': 200 if healthy else 401,
                'ExpectedHealthyCatalogEndpoint': healthy, 'SlowStartupBudgetOrDeployedRolloutProven': False}))
        namespace = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'

        def tag(name):
            return '{' + namespace + '}' + name

        document = ET.Element(tag('TestRun'))
        definitions = ET.SubElement(document, tag('TestDefinitions'))
        outcomes = ET.SubElement(document, tag('Results'))
        for identity, count in manifest['methods'].items():
            class_name, method = identity.rsplit('.', 1)
            for case in range(count):
                test_id, execution_id = str(uuid.uuid4()), str(uuid.uuid4())
                display = identity + '(' + str(case) + ')'
                unit = ET.SubElement(definitions, tag('UnitTest'), id=test_id, name=display)
                ET.SubElement(unit, tag('Execution'), id=execution_id)
                ET.SubElement(unit, tag('TestMethod'), className=class_name, name=method)
                ET.SubElement(outcomes, tag('UnitTestResult'), testId=test_id, executionId=execution_id,
                    testName=display, outcome='Passed')
        summary = ET.SubElement(document, tag('ResultSummary'), outcome='Completed')
        counters = {name: '10' for name in ('total', 'executed', 'passed')}
        counters.update({name: '0' for name in ('failed', 'error', 'timeout', 'aborted', 'inconclusive',
            'passedButRunAborted', 'notRunnable', 'notExecuted', 'disconnected', 'warning', 'completed', 'inProgress', 'pending')})
        ET.SubElement(summary, tag('Counters'), **counters)
        ET.ElementTree(document).write(results / 'synthetic.trx', encoding='utf-8')
        coverage = ET.Element('coverage')
        packages = ET.SubElement(coverage, 'packages')
        for assembly in ('Api', 'Application', 'Data', 'Domain'):
            package = ET.SubElement(packages, 'package', name='Legacy.Maliev.CatalogService.' + assembly)
            ET.SubElement(package, 'line', number='1', hits='1')
        for name in ('raw-a', 'raw-b'):
            folder = results / name
            folder.mkdir()
            ET.ElementTree(coverage).write(folder / 'coverage.cobertura.xml', encoding='utf-8')
        proof_path = results / 'country-health-diagnostic-proof.json'
        proof_path.write_text(json.dumps(native.verify_native(results, False, manifest), indent=2) + '\n')
        reader.REPOSITORY = repository
        original = reader.evidence(results, head)
        assert original['nativeDiagnosticCasesPassed'] == 10 and len(original['originalExports']) == 48
        assert original['sourceLiteralGapCases'] == []
        assert original['sourceLiteralParitySatisfied'] and original['diagnosticAcceptanceIsNotSourceParityAcceptance']
        passed.append('synthetic-only-parser-positive-exact-literals')
        receipt = results / 'country-health-diagnostic-receipt.json'
        receipt.write_text(json.dumps(original, indent=2) + '\n')

        def reject(name, path, mutation):
            before = path.read_bytes()
            try:
                path.write_bytes(mutation(before))
                try:
                    reader.evidence(results, head)
                except (ValueError, SystemExit, UnicodeError):
                    passed.append('reject-' + name)
                else:
                    raise AssertionError('Accepted invalid original evidence: ' + name)
            finally:
                path.write_bytes(before)

        def mutate_json(field, value):
            def mutation(data):
                document = reader.load(data)
                document[field] = value
                return encode(document)
            return mutation

        reject('false-parity-label', exports / 'empty-observation.json', mutate_json('SourceLiteralParityObserved', False))
        reject('wrong-request-binding', exports / 'short-observation.json', mutate_json('RequestedIso2', 'X'))
        reject('wrong-store-binding', exports / 'short-observation.json', mutate_json('StoredIso2', 'X'))
        changed = [exports / ('empty-' + kind + '.json') for kind in ('fresh', 'after-put', 'after-db', 'observation')]
        before_gap = {path: path.read_bytes() for path in changed}
        try:
            for path in changed:
                item = reader.load(path.read_bytes())
                if path.name.endswith('observation.json'):
                    item.update(StoredIso2='  ', StoredIso3='   ', SourceLiteralParityObserved=False)
                else:
                    item.update(Iso2='  ', Iso3='   ')
                path.write_bytes(encode(item))
            try:
                reader.evidence(results, head)
            except ValueError:
                passed.append('reject-passing-native-proof-with-real-literal-gap')
            else:
                raise AssertionError('Accepted contradictory passing native/source gap evidence')
        finally:
            for path, data in before_gap.items(): path.write_bytes(data)
        reject('boolean-role-count', exports / 'short-observation.json', mutate_json('CurrencyRows', False))
        reject('wrong-model-type', exports / 'short-observation.json', mutate_json('Iso2ColumnType', 'text'))
        reject('fake-auth-join', exports / 'short-observation.json', mutate_json('ActualAuthProducerJoinProven', True))
        reject('old-prefix-healthy', exports / 'health-old-material-prefix-observation.json', mutate_json('Status', 200))
        reject('deployment-claim', exports / 'health-readiness-observation.json', mutate_json('SlowStartupBudgetOrDeployedRolloutProven', True))
        reject('liveness-body', exports / 'health-liveness-body.bin', lambda data: b'Unexpected')
        reject('readiness-private-data', exports / 'health-readiness-body.bin', mutate_json('exception', 'synthetic'))
        reject('native-trx', results / 'synthetic.trx', lambda data: data.replace(b'outcome="Passed"', b'outcome="Failed"', 1))
        reject('raw-copy', results / 'raw-a/coverage.cobertura.xml', lambda data: data + b' ')
        reject('native-proof', proof_path, mutate_json('actualPassed', 999))
        extra = exports / 'unexpected.json'
        extra.write_bytes(b'{}')
        try:
            try:
                reader.evidence(results, head)
            except ValueError:
                passed.append('reject-extra-export')
            else:
                raise AssertionError('Accepted unexpected export')
        finally:
            extra.unlink()
        receipt.unlink()
        command = ['python', '-B', str(repository / 'scripts/retain-country-health-diagnostics.py'), str(results)]
        subprocess.run([*command, '--seal'], check=True, capture_output=True, timeout=60)
        subprocess.run([*command, '--expected-head', head], check=True, capture_output=True, timeout=60)
        passed.append('synthetic-seal-and-read-only-readback')
        before = receipt.read_bytes()
        try:
            receipt.write_bytes(encode(dict(reader.load(before), sourceLiteralParitySatisfied=False)))
            outcome = subprocess.run([*command, '--expected-head', head], capture_output=True, timeout=60)
            assert outcome.returncode != 0
            passed.append('reject-forged-accepted-parity-receipt')
        finally:
            receipt.write_bytes(before)
        source = repository / reader.SOURCES[0]
        source.write_bytes(source.read_bytes() + b'// synthetic worktree drift\n')
        receipt.unlink()
        outcome = subprocess.run([*command, '--seal'], capture_output=True, timeout=60)
        assert outcome.returncode != 0
        passed.append('reject-source-worktree-drift')
    assert not Path(temporary).exists()
    print(json.dumps({'status': 'PASS offline synthetic diagnostic reader controls only', 'passed': len(passed),
        'controls': passed, 'nativeProducerExecutions': 0, 'actualSourceParityAccepted': False,
        'temporaryRepositoriesAndFixturesRemoved': True}))


if __name__ == '__main__':
    main()
