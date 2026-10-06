"""Seal original synthetic diagnostics; source parity gaps remain explicitly open."""
import argparse
import datetime
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import subprocess

REPOSITORY = Path(__file__).resolve().parent.parent
SOURCES = [
    'Legacy.Maliev.CatalogService.Api/Program.cs',
    'Legacy.Maliev.CatalogService.Api/InstantQuotationCatalogStartupService.cs',
    'Legacy.Maliev.CatalogService.Application/Models/CatalogModels.cs',
    'Legacy.Maliev.CatalogService.Application/Services/CatalogApplicationService.cs',
    'Legacy.Maliev.CatalogService.Data/CatalogDbContext.cs',
    'Legacy.Maliev.CatalogService.Data/LookupDbContexts.cs',
    'Legacy.Maliev.CatalogService.Data/CountryIsoSchemaUpdater.cs',
    'Legacy.Maliev.CatalogService.Data/Migrations/20261006202000_PreserveLiteralCountryIsoCodes.cs',
    'Legacy.Maliev.CatalogService.Data/Migrations/20261006202000_PreserveLiteralCountryIsoCodes.Designer.cs',
    'Legacy.Maliev.CatalogService.Data/Migrations/CatalogDbContextModelSnapshot.cs',
    'Legacy.Maliev.CatalogService.Tests/Integration/CountryIsoSchemaUpdaterTests.cs',
    'Legacy.Maliev.CatalogService.Tests/Integration/CatalogHttpLifecycleTests.cs',
    'Legacy.Maliev.CatalogService.Tests/Integration/CountryIsoAndHealthSourceDiagnosticsTests.cs',
    'tools/CountryIsoAndHealthSourceDiagnostics/README.md',
    'scripts/country-health-diagnostic-expected.json',
    'scripts/verify-country-health-diagnostics.py',
    'scripts/retain-country-health-diagnostics.py',
    'scripts/test-country-health-diagnostic-reader.py',
    '.github/workflows/country-health-diagnostics.yml',
    '.github/workflows/_build-and-test.yml',
    'Legacy.Maliev.CatalogService.Tests/Workflows/WorkflowContractTests.cs',
]


def require(message, condition):
    if not condition:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def bounded(path, maximum):
    require('Require original regular file without symlinks', path.is_file() and not path.is_symlink())
    with path.open('rb') as stream:
        data = stream.read(maximum + 1)
    require('Bounded evidence required', len(data) <= maximum)
    return data


def load(data):
    def pairs(rows):
        result = {}
        for key, value in rows:
            require('Duplicate JSON key', key not in result)
            result[key] = value
        return result

    def reject(value):
        raise ValueError('Nonfinite JSON is not evidence: ' + value)

    value = json.loads(data.decode('utf-8', errors='strict'), object_pairs_hook=pairs, parse_constant=reject)

    def unicode_values(item):
        if isinstance(item, str):
            item.encode('utf-8', errors='strict')
        elif isinstance(item, dict):
            for key, child in item.items():
                unicode_values(key)
                unicode_values(child)
        elif isinstance(item, list):
            for child in item:
                unicode_values(child)

    unicode_values(value)
    return value


def git(*arguments):
    return subprocess.check_output(['git', '-C', str(REPOSITORY), *arguments], timeout=30)


def country(data):
    value = load(data)
    allowed = {'Id', 'Name', 'Continent', 'CountryCode', 'Iso2', 'Iso3', 'CreatedDate', 'ModifiedDate'}
    require('Exact synthetic Country schema required', isinstance(value, dict) and set(value) <= allowed
        and {'Id', 'Name', 'CreatedDate', 'ModifiedDate'} <= set(value))
    require('Synthetic Country identity required', type(value['Id']) is int and value['Id'] > 0
        and value['Name'] == 'Synthetic ISO diagnostic')
    for key in ('Continent', 'CountryCode'):
        require('No unexpected synthetic metadata', value.get(key) is None)
    for key, limit in (('Iso2', 2), ('Iso3', 3)):
        item = value.get(key)
        require('Bounded nullable literal code required', item is None or type(item) is str and len(item) <= limit)
    for key in ('CreatedDate', 'ModifiedDate'):
        item = value[key]
        require('Original legacy timestamp required', type(item) is str and len(item) <= 40)
        datetime.datetime.fromisoformat(item.replace('Z', '+00:00'))
    return value


def evidence(root, head):
    require('Explicit immutable source head required', re.fullmatch('[0-9a-f]{40}', head) is not None)
    folder = root / 'country-health-diagnostics'
    require('Exact owned diagnostic directory required', folder.is_dir() and not folder.is_symlink()
        and folder.resolve().parent == root.resolve())
    manifest = load(git('show', head + ':scripts/country-health-diagnostic-expected.json'))
    require('Diagnostic scope must remain explicit', manifest['diagnosticAcceptanceIsNotSourceParityAcceptance'] is True
        and manifest['actualAuthOrDeploymentProven'] is False and manifest['forecast'] == 20)
    names = {case + '-' + suffix for case in manifest['cases'] for suffix in
        ('created.json', 'fresh.json', 'repeated.json', 'after-put.json', 'after-db.json', 'observation.json')}
    names.update('health-' + case + '-' + suffix for case in manifest['health'] for suffix in
        ('body.bin', 'observation.json'))
    require('Exact48 original exports required', len(names) == 48 and {path.name for path in folder.iterdir()} == names)
    proof_bytes = bounded(root / 'country-health-diagnostic-proof.json', 64 * 1024)
    proof = load(proof_bytes)
    spec = importlib.util.spec_from_file_location('trusted_current_diagnostic_gate', REPOSITORY / 'scripts/verify-country-health-diagnostics.py')
    gate = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(gate)
    require('Recheck original native evidence rather than trusting proof claims', gate.verify_native(
        root, proof['actualPassed'] != manifest['forecast'], manifest) == proof)
    wires, gaps = [], []
    for name in sorted(names):
        data = bounded(folder / name, 16 * 1024)
        wires.append({'file': name, 'sha256': digest(data), 'bytes': len(data)})
    for case, requested in manifest['cases'].items():
        payloads = {kind: country(bounded(folder / (case + '-' + kind + '.json'), 16 * 1024))
            for kind in ('created', 'fresh', 'repeated', 'after-put', 'after-db')}
        created, fresh, repeated, after = (payloads[key] for key in ('created', 'fresh', 'repeated', 'after-put'))
        require('Actual created/repeated identities and original row required', created['Id'] == fresh['Id'] == after['Id']
            and repeated['Id'] != created['Id'])
        require('Same literal update and fresh reads must remain stable', all(fresh.get(key) == after.get(key) for key in ('Iso2', 'Iso3')))
        after_db = payloads['after-db']
        require('Fresh post-PUT own database row and HTTP values required', after_db['Id'] == after['Id']
            and all(after_db.get(key) == after.get(key) for key in ('Iso2', 'Iso3')))
        observation = load(bounded(folder / (case + '-observation.json'), 16 * 1024))
        require('Exact diagnostic observation schema required', isinstance(observation, dict) and set(observation) == {
            'SyntheticOnly', 'DiagnosticOnly', 'SourceLiteralParityObserved', 'RequestedIso2', 'RequestedIso3',
            'StoredIso2', 'StoredIso3', 'Iso2ColumnType', 'Iso3ColumnType', 'CountryRows', 'CatalogCountryRows',
            'CurrencyRows', 'ActualAuthProducerJoinProven'})
        require('No actual Auth/customer outcome assertion', observation['SyntheticOnly'] is True
            and observation['DiagnosticOnly'] is True and observation['ActualAuthProducerJoinProven'] is False)
        require('Original requested literal binding required', [observation['RequestedIso2'], observation['RequestedIso3']] == requested)
        require('Own database and fresh HTTP values must agree', [observation['StoredIso2'], observation['StoredIso3']]
            == [fresh.get('Iso2'), fresh.get('Iso3')])
        parity = requested == [fresh.get('Iso2'), fresh.get('Iso3')]
        require('Cannot relabel a literal gap as accepted parity', observation['SourceLiteralParityObserved'] is parity)
        require('Preserve exact observed target model', observation['Iso2ColumnType'] == manifest['observedTargetColumnTypes']['Iso2']
            and observation['Iso3ColumnType'] == manifest['observedTargetColumnTypes']['Iso3'])
        for key, count in (('CountryRows', 2), ('CatalogCountryRows', 0), ('CurrencyRows', 0)):
            require('Exact independent role counts required', type(observation[key]) is int and observation[key] == count)
        if not parity:
            gaps.append(case)
    for case, expected in manifest['health'].items():
        observation = load(bounded(folder / ('health-' + case + '-observation.json'), 16 * 1024))
        require('Exact health observation schema required', isinstance(observation, dict) and set(observation) == {
            'SyntheticOnly', 'DiagnosticOnly', 'Path', 'Status', 'ExpectedHealthyCatalogEndpoint',
            'SlowStartupBudgetOrDeployedRolloutProven'})
        require('Bounded health observation cannot claim deployment', observation['SyntheticOnly'] is True
            and observation['DiagnosticOnly'] is True and observation['SlowStartupBudgetOrDeployedRolloutProven'] is False)
        require('Exact currently declared route/control required', observation['Path'] == expected[0]
            and observation['ExpectedHealthyCatalogEndpoint'] is expected[1]
            and type(observation['Status']) is int and 100 <= observation['Status'] <= 599
            and (observation['Status'] == 200) is expected[1])
        body = bounded(folder / ('health-' + case + '-body.bin'), 16 * 1024)
        if case == 'liveness':
            require('Original liveness body required', body == b'Healthy')
        elif case == 'readiness':
            status = load(body)
            require('Sanitized original healthy readiness required', isinstance(status, dict)
                and set(status) == {'status', 'checks', 'totalDuration'} and status['status'] == 'Healthy'
                and isinstance(status['checks'], dict) and status['checks'])
            for check in status['checks'].values():
                require('No readiness exceptions, data or unsanitized metadata', isinstance(check, dict)
                    and set(check) == {'status', 'duration'} and check['status'] == 'Healthy'
                    and type(check['duration']) in (int, float) and check['duration'] >= 0)
            require('Bounded original readiness duration required', type(status['totalDuration']) in (int, float)
                and status['totalDuration'] >= 0)
    require('Passing native exact-literal assertions cannot coexist with retained literal gaps', not gaps)
    return {'schemaVersion': 1, 'sourceHead': head, 'sourceTree': git('rev-parse', head + '^{tree}').decode().strip(),
        'sources': {path: digest(git('show', head + ':' + path)) for path in SOURCES},
        'nativeProofSha256': digest(proof_bytes), 'trxSha256': proof['trxSha256'], 'rawSha256': proof['rawSha256'],
        'actualPassed': proof['actualPassed'], 'nativeDiagnosticCasesPassed': 10, 'originalExports': wires,
        'sourceLiteralParitySatisfied': not gaps, 'sourceLiteralGapCases': gaps,
        'diagnosticAcceptanceIsNotSourceParityAcceptance': True, 'actualAuthOrDeploymentProven': False}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('results', type=Path)
    parser.add_argument('--seal', action='store_true')
    parser.add_argument('--expected-head')
    arguments = parser.parse_args()
    head = arguments.expected_head or git('rev-parse', 'HEAD').decode().strip()
    root = arguments.results.resolve()
    actual = evidence(root, head)
    receipt = root / 'country-health-diagnostic-receipt.json'
    if arguments.seal:
        require('Seal only current exact tracked source', git('rev-parse', 'HEAD').decode().strip() == head)
        for path, sha in actual['sources'].items():
            require('Source worktree must equal immutable Git blob', digest(bounded(REPOSITORY / path, 2 * 1024 * 1024)) == sha)
        require('Never replace a sealed original receipt', not receipt.exists())
        receipt.write_text(json.dumps(actual, indent=2) + '\n', encoding='utf-8')
    require('Exact original source/native/export receipt required', load(bounded(receipt, 64 * 1024)) == actual)
    print(json.dumps({'receiptSha256': digest(bounded(receipt, 64 * 1024)), 'sourceHead': head,
        'nativeDiagnosticCasesPassed': 10, 'sourceLiteralGapCases': actual['sourceLiteralGapCases'],
        'observedAllLiteralCasesMatched': actual['sourceLiteralParitySatisfied'],
        'diagnosticSourceParityAcceptanceClaimed': False, 'actualAuthOrDeploymentProven': False}))


if __name__ == '__main__':
    main()
