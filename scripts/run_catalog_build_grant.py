"""BUILD-only claim gate; delegates all resource custody to sealed V8."""
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import sys

SCRIPT = Path(__file__).resolve()
FIELDS = {'schemaVersion', 'scope', 'transportSha', 'sourceManifestSha256', 'capsuleSha256',
          'issuedUtc', 'admitBeforeUtc', 'maximumWorkSeconds', 'nonce'}

def unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('Duplicate build claim key')
        result[key] = value
    return result

def event_permit(path):
    # Read the runner's event file, never an echoed step environment value.
    target = Path(path)
    if not path or not target.is_absolute() or target.is_symlink():
        raise ValueError('Absolute regular runner event file required')
    before = target.stat()
    if not stat.S_ISREG(before.st_mode) or before.st_size > 1048576:
        raise ValueError('Bounded regular runner event file required')
    with target.open('rb') as stream:
        opened = os.fstat(stream.fileno())
        if (opened.st_dev, opened.st_ino) != (before.st_dev, before.st_ino):
            raise ValueError('Runner event identity changed')
        data = stream.read(1048577)
    if len(data) > 1048576:
        raise ValueError('Runner event exceeds bound')
    event = json.loads(data, object_pairs_hook=unique)
    inputs = event.get('inputs') if isinstance(event, dict) else None
    raw = inputs.get('build_permit') if isinstance(inputs, dict) else None
    if not isinstance(raw, str) or len(raw.encode()) > 4096:
        raise ValueError('Bounded runner BUILD-only input required')
    return raw

def mask_permit(raw):
    # Workflow-command escaping prevents multiline input from injecting commands.
    # Register both the complete value and nonblank lines before later steps.
    for value in dict.fromkeys([raw, *raw.splitlines()]):
        if value:
            encoded = value.replace('%', '%25').replace('\r', '%0D').replace('\n', '%0A')
            print('::add-mask::' + encoded, flush=True)

def validate(raw, policy, head, now):
    if not isinstance(raw, str) or len(raw.encode()) > 4096:
        raise ValueError('Bounded BUILD-only claim required')
    value = json.loads(raw, object_pairs_hook=unique)
    if not isinstance(value, dict) or set(value) != FIELDS:
        raise ValueError('Exact BUILD-only claim schema required')
    if type(value['schemaVersion']) is not int or value['schemaVersion'] != 1:
        raise ValueError('Unsupported claim schema')
    if value['scope'] != 'catalog-v8-build-only':
        raise ValueError('No future-phase grant accepted')
    if not re.fullmatch('[0-9a-f]{40}', head or '') or value['transportSha'] != head:
        raise ValueError('Claim transport head differs')
    if value['sourceManifestSha256'] != policy['sourceManifestSha256'] or value['capsuleSha256'] != policy['capsuleSha256']:
        raise ValueError('Claim immutable source differs')
    if type(value['maximumWorkSeconds']) is not int or value['maximumWorkSeconds'] != 600:
        raise ValueError('Exact finite work budget required')
    if not isinstance(value['nonce'], str) or not re.fullmatch('[0-9a-f]{32}', value['nonce']):
        raise ValueError('Exact grant nonce required')
    timestamps = []
    for key in ('issuedUtc', 'admitBeforeUtc'):
        text = value[key]
        if not isinstance(text, str) or not re.fullmatch(r'\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z', text):
            raise ValueError('Exact UTC admission window required')
        timestamps.append(dt.datetime.fromisoformat(text.replace('Z', '+00:00')))
    issued, expires = timestamps
    if not 0 < (expires - issued).total_seconds() <= 300 or not issued <= now < expires:
        raise ValueError('Expired, future or unbounded admission claim')
    return value

def run_build(supervisor, raw, policy, head, executable, clock):
    claim = validate(raw, policy, head, clock())
    original_fork = supervisor.os.fork
    original_arguments = sys.argv
    def admitted_fork():
        # BEFORE the syscall: a synchronous refusal cannot hide a returned child.
        validate(raw, policy, head, clock())
        return original_fork()
    try:
        supervisor.os.fork = admitted_fork
        sys.argv = ['sealed-catalog-supervisor', 'build', str(executable)]
        supervisor.main()
    finally:
        supervisor.os.fork = original_fork
        sys.argv = original_arguments
    return claim

def main():
    raw = event_permit(os.environ.get('CATALOG_BUILD_EVENT_PATH', os.environ.get('GITHUB_EVENT_PATH', '')))
    if len(sys.argv) == 2 and sys.argv[1] == '--mask-permit':
        mask_permit(raw)
        return  # No policy/supervisor import or native allocation.
    policy = json.loads((SCRIPT.parent / 'catalog-candidate-policy.json').read_bytes(), object_pairs_hook=unique)
    head = os.environ.get('GITHUB_SHA', '')
    clock = lambda: dt.datetime.now(dt.timezone.utc)
    claim = validate(raw, policy, head, clock())
    run = os.environ.get('GITHUB_RUN_ID', '')
    if not re.fullmatch('[1-9][0-9]{0,19}', run) or os.environ.get('GITHUB_RUN_ATTEMPT') != '1':
        raise RuntimeError('Original hosted run and first attempt required')
    if len(sys.argv) == 2 and sys.argv[1] == '--verify-permit':
        return  # Read-only gate before SDK setup; no supervisor import/allocation.
    if sys.platform != 'linux' or os.geteuid() != 0 or len(sys.argv) != 2:
        raise RuntimeError('Only root-owned Linux BUILD-only delegation supported')
    candidate = SCRIPT.parents[1] / 'candidate'
    if Path.cwd().resolve() != candidate.resolve(strict=True):
        raise RuntimeError('Exact materialized candidate directory required')
    source = candidate / 'scripts/run-catalog-owned-qualification.py'
    row = next(item for item in policy['sourceFiles'] if item['path'] == 'scripts/run-catalog-owned-qualification.py')
    data = source.read_bytes()
    if len(data) != row['bytes'] or hashlib.sha256(data).hexdigest() != row['sha256']:
        raise RuntimeError('Sealed supervisor raw source differs')
    spec = importlib.util.spec_from_file_location('catalog_sealed_build', source)
    supervisor = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(supervisor)
    evidence = SCRIPT.parents[1] / 'evidence'
    evidence.mkdir(exist_ok=True)
    uid = int(os.environ.get('MALIEV_CATALOG_EVIDENCE_UID', '-1'))
    gid = int(os.environ.get('MALIEV_CATALOG_EVIDENCE_GID', '-1'))
    if uid <= 0 or gid < 0:
        raise RuntimeError('Original evidence owner required')
    receipt = evidence / ('build-claim-' + claim['nonce'] + '.json')
    supervisor.write_new(receipt,
        {'claim': claim, 'githubRunId': run, 'attempt': 1,
         'authority': 'Explicit Root-issued claim; JSON fields are not cryptographic issuer authentication',
         'expiryScope': 'Admission within300 seconds of issue; V8 work deadline starts before fork and is600 seconds, so authorized work ends no later than issue+900; cleanup-only custody afterward'})
    body_failure = None
    try:
        run_build(supervisor, raw, policy, head, Path(sys.argv[1]), clock)
    except BaseException as failure:
        body_failure = failure
    finally:
        # The SDK identity cannot edit the private claim while custody is live.
        # Unresolved supervisor cleanup never returns into this release path.
        try:
            os.chown(receipt, uid, gid, follow_symlinks=False)
        except BaseException as failure:
            if body_failure is None:
                body_failure = failure
            else:
                try:
                    print('Secondary build-claim ownership finalization fault: ' + type(failure).__name__, file=sys.stderr)
                except BaseException:
                    pass  # Diagnostic failure cannot replace original owned failure.
    if body_failure is not None:
        raise body_failure

if __name__ == '__main__':
    main()
