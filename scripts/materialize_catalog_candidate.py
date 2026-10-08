"""Catalog-only bounded same-repository Git-blob transport; no SDK execution."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import urllib.request
import base64
import time

REPOSITORY = 'MALIEV-Co-Ltd/Legacy.Maliev.CatalogService'
BASE = '3f426723743570a6c20d2c014499445abb0774e1'
SOURCE_PINS = {'MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults': '7edcd961024868513fd5f373cab3dcb261197f77',
               'MALIEV-Co-Ltd/Legacy.Maliev.CompatibilityContracts': '78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7'}
MAX_BLOB = 2 * 1024 * 1024
MAX_FILE = 256 * 1024

def unique(pairs):
    value = {}
    for key, item in pairs:
        if key in value:
            raise ValueError('Duplicate JSON key')
        value[key] = item
    return value

def parse(raw):
    return json.loads(raw, object_pairs_hook=unique)

def digest(raw):
    return hashlib.sha256(raw).hexdigest()

def path_name(value):
    if not isinstance(value, str) or not value or '\\' in value or ':' in value:
        raise ValueError('Invalid Catalog source path')
    if any(p in ('', '.', '..', '.git') for p in value.split('/')) or str(PurePosixPath(value)) != value:
        raise ValueError('Noncanonical Catalog source path')
    return value

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args):
        raise ValueError('Git blob redirects prohibited')

def decode_blob(raw, oid):
    if not re.fullmatch('[0-9a-f]{40}', oid) or len(raw) > 3 * MAX_BLOB:
        raise ValueError('Invalid bounded Git object')
    item = parse(raw)
    if item.get('sha') != oid or item.get('encoding') != 'base64':
        raise ValueError('Git blob identity differs')
    data = base64.b64decode(item['content'].replace('\n', ''), validate=True)
    if type(item.get('size')) is not int or item['size'] != len(data) or len(data) > MAX_BLOB:
        raise ValueError('Git blob size differs')
    if hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest() != oid:
        raise ValueError('Git object digest differs')
    return data

def fetch(oid):
    if not isinstance(oid, str) or not re.fullmatch('[0-9a-f]{40}', oid):
        raise ValueError('Invalid blob ID')
    headers = {'Accept': 'application/vnd.github+json', 'User-Agent': 'catalog-sealed-source-transport'}
    if os.environ.get('GH_TOKEN'):
        headers['Authorization'] = 'Bearer ' + os.environ['GH_TOKEN']
    request = urllib.request.Request(f'https://api.github.com/repos/{REPOSITORY}/git/blobs/{oid}', headers=headers)
    with urllib.request.build_opener(NoRedirect()).open(request, timeout=30) as response:
        deadline = time.monotonic() + 30
        chunks = []
        size = 0
        while True:
            if time.monotonic() >= deadline:
                raise TimeoutError('Finite Git blob read deadline')
            block = response.read1(min(65536, 3 * MAX_BLOB - size + 1))
            if not block:
                break
            size += len(block)
            if size > 3 * MAX_BLOB:
                raise ValueError('Git blob response exceeds quota')
            chunks.append(block)
        raw = b''.join(chunks)
    return decode_blob(raw, oid)

def validate(raw, manifest, policy):
    if policy['repository'] != REPOSITORY or policy['acceptedBase'] != BASE or type(policy['schemaVersion']) is not int or policy['schemaVersion'] != 1:
        raise ValueError('Foreign Catalog policy')
    if policy.get('sourcePins') != SOURCE_PINS:
        raise ValueError('Exact Catalog dependency pins required')
    if digest(manifest) != policy['sourceManifestSha256'] or digest(raw) != policy['capsuleSha256'] or len(raw) > MAX_BLOB:
        raise ValueError('Reviewed raw source seal differs')
    capsule = parse(raw)
    if set(capsule) != {'schemaVersion', 'repository', 'acceptedBase', 'sourceFiles'} or type(capsule['schemaVersion']) is not int or capsule['schemaVersion'] != 1 or capsule['repository'] != REPOSITORY or capsule['acceptedBase'] != BASE:
        raise ValueError('Foreign capsule schema/base/repository')
    expected = {path_name(row['path']): row for row in policy['sourceFiles']}
    if len(expected) != 30 or len(policy['sourceFiles']) != 30 or len(capsule['sourceFiles']) != 30:
        raise ValueError('Exact30 source inventory required')
    files = {}
    for row in capsule['sourceFiles']:
        if set(row) != {'path', 'content'}:
            raise ValueError('Unexpected source row')
        name = path_name(row['path'])
        if name not in expected or name in files or not isinstance(row['content'], str):
            raise ValueError('Foreign/duplicate source row')
        value = row['content'].encode('utf-8')
        item = expected[name]
        if type(item['bytes']) is not int or not 0 < item['bytes'] <= MAX_FILE or len(value) != item['bytes'] or digest(value) != item['sha256']:
            raise ValueError('Raw source digest/size differs')
        files[name] = value
    return files

def git(root, *args):
    return subprocess.run(['git', '-C', str(root), *args], check=True, stdout=subprocess.PIPE,
                          stderr=subprocess.PIPE, timeout=30).stdout

def safe_target(root, name):
    target = root / path_name(name)
    current = target
    while current != root.parent:
        if current.is_symlink():
            raise ValueError('Symlink source destination')
        current = current.parent
    if target.exists() and not target.is_file():
        raise ValueError('Nonfile source destination')
    return target

def verify(root, policy):
    if git(root, 'rev-parse', 'HEAD').decode().strip() != BASE:
        raise ValueError('Accepted base changed')
    allowed = {row['path'] for row in policy['sourceFiles']}
    changed = set(filter(None, git(root, 'diff', 'HEAD', '--name-only', '-z').decode().split('\0')))
    if changed - allowed:
        raise ValueError('Unreviewed tracked source changed')
    for row in policy['sourceFiles']:
        with safe_target(root, row['path']).open('rb') as stream:
            raw = stream.read(MAX_FILE + 1)
        if len(raw) != row['bytes'] or digest(raw) != row['sha256']:
            raise ValueError('Materialized source seal differs')

def materialize(root, policy, files):
    if git(root, 'rev-parse', 'HEAD').decode().strip() != BASE or git(root, 'status', '--porcelain', '--untracked-files=all'):
        raise ValueError('Exact clean accepted checkout required')
    destinations = {name: safe_target(root, name) for name in files}
    for name, target in destinations.items():
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open('wb') as stream:
            stream.write(files[name])
    verify(root, policy)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--manifest-blob')
    parser.add_argument('--capsule-blob')
    parser.add_argument('--verify-only', action='store_true')
    args = parser.parse_args()
    transport = Path(__file__).resolve().parents[1]
    root = transport / 'candidate'
    if root.is_symlink():
        raise ValueError('Candidate checkout symlink refused')
    policy = parse((transport / 'scripts/catalog-candidate-policy.json').read_bytes())
    if os.environ.get('GITHUB_REPOSITORY') != REPOSITORY or git(transport, 'rev-parse', 'HEAD').decode().strip() != os.environ.get('GITHUB_SHA'):
        raise ValueError('Foreign workflow repository/commit')
    if args.verify_only:
        verify(root, policy)
        return
    evidence = transport / 'evidence/source-materialization.json'
    if evidence.exists():
        raise ValueError('Fresh receipt required')
    manifest = fetch(args.manifest_blob)
    capsule = fetch(args.capsule_blob)
    files = validate(capsule, manifest, policy)
    materialize(root.resolve(strict=True), policy, files)
    evidence.parent.mkdir(exist_ok=True)
    with evidence.open('x') as stream:
        json.dump({'transportCommit': os.environ['GITHUB_SHA'], 'acceptedBase': BASE,
            'manifestBlob': args.manifest_blob, 'capsuleBlob': args.capsule_blob,
            'sourceManifestSha256': policy['sourceManifestSha256'], 'capsuleSha256': policy['capsuleSha256'],
            'sourceFiles': policy['sourceFiles'], 'sourcePins': policy['sourcePins'], 'nativeValidated': False}, stream)

if __name__ == '__main__':
    main()
