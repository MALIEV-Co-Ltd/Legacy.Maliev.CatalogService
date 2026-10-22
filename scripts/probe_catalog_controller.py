"""Finite controller-only native proof; never executes SDK or Docker commands."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import select
import signal
import sys
import time
import uuid
from contextlib import contextmanager

LIMITS = {'memory.max': '2147483648', 'pids.max': '64', 'cpu.max': '100000 100000'}
FAULTS = ('none', 'slot-acquired', 'group-handle-acquired', 'child-assigned', 'identity-query')

class InjectedAcquisitionFailure(Exception):
    pass

@contextmanager
def registration_window():
    # Defer handled cancellation only across acquisition and exact-owner
    # publication. Pending cancellation is honored immediately on restoration.
    previous = signal.pthread_sigmask(signal.SIG_BLOCK, {signal.SIGINT, signal.SIGTERM})
    try:
        yield previous
    finally:
        signal.pthread_sigmask(signal.SIG_SETMASK, previous)

def supervisor(transport, ordinary=False):
    name = 'scripts/run-catalog-owned-qualification.py'
    if ordinary:
        seal = transport / 'scripts/catalog-ordinary-controller-seal.json'
        if seal.is_symlink():
            raise RuntimeError('Foreign ordinary supervisor seal')
        policy = json.loads(seal.read_bytes())
        if (set(policy) != {'schemaVersion', 'path', 'bytes', 'sha256'} or
                type(policy['schemaVersion']) is not int or policy['schemaVersion'] != 1 or
                policy['path'] != name or type(policy['bytes']) is not int or
                not 0 < policy['bytes'] <= 256 * 1024 or
                not isinstance(policy['sha256'], str) or len(policy['sha256']) != 64 or
                any(c not in '0123456789abcdef' for c in policy['sha256'])):
            raise RuntimeError('Foreign ordinary supervisor seal schema')
        row = policy
        path = transport / name
    else:
        policy = json.loads((transport / 'scripts/catalog-candidate-policy.json').read_bytes())
        row = next(row for row in policy['sourceFiles'] if row['path'] == name)
        path = transport / 'candidate' / name
    if path.is_symlink() or path.resolve().parent != path.parent.resolve():
        raise RuntimeError('Foreign supervisor source path')
    with path.open('rb') as stream:
        raw = stream.read(256 * 1024 + 1)
    if len(raw) != row['bytes'] or hashlib.sha256(raw).hexdigest() != row['sha256']:
        raise RuntimeError('Reviewed supervisor seal differs')
    spec = importlib.util.spec_from_file_location('reviewed_catalog_supervisor', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

def check_snapshot(snapshot, child, run, uid, gid, groups):
    if set(snapshot) != {'pid', 'uid', 'gid', 'groups', 'noNewPrivileges', 'cgroup'}:
        raise RuntimeError('Foreign controller snapshot schema')
    if (snapshot['pid'] != child or snapshot['uid'] != uid or snapshot['gid'] != gid or
            snapshot['groups'] != groups or snapshot['noNewPrivileges'] != '1' or
            snapshot['cgroup'] != '0::/maliev-catalog-' + run):
        raise RuntimeError('Actual tiny-child privilege/cgroup proof differs')

def wait_child(child, stopped, deadline, ownership=None):
    while time.monotonic() < deadline:
        with registration_window():
            pid, state = os.waitpid(child, os.WNOHANG | (os.WUNTRACED if stopped else 0))
            if pid == child and ownership is not None and (os.WIFEXITED(state) or os.WIFSIGNALED(state)):
                ownership['reaped'] = True
        if pid == child:
            if stopped and not os.WIFSTOPPED(state):
                raise RuntimeError('Tiny child failed before admission stop')
            return state
        time.sleep(.01)
    raise TimeoutError('Bounded tiny-child settlement deadline')

def verify_handle(module, descriptor, expected):
    if module.descriptor_identity(descriptor) != expected:
        raise RuntimeError('Retained original handle identity differs')

def cleanup(module, owned):
    # No backend exists. The only physical mutations are our exact direct child,
    # kernel-owned group, retained descriptors and exclusively created claim.
    if owned['groupAttempted'] and not owned['groupCreated'] and owned['group'].exists():
        raise RuntimeError('Unacknowledged group birth retained in quarantine')
    if owned['groupCreated'] and owned['groupIdentity'] is None:
        if owned['groupFd'] is None:
            raise RuntimeError('Unknown group birth retained in quarantine')
        owned['groupIdentity'] = module.retained_creation_identity(owned['group'], owned['groupFd'])
        owned['handles'][owned['groupFd']] = owned['groupIdentity']
    if owned['slotFd'] is not None and owned['slotIdentity'] is None:
        owned['slotIdentity'] = module.retained_creation_identity(owned['slot'], owned['slotFd'])
        owned['handles'][owned['slotFd']] = owned['slotIdentity']
    for descriptor, expected in list(owned['handles'].items()):
        if expected is None:
            owned['handles'][descriptor] = module.descriptor_identity(descriptor)
    child = owned['child']
    if child is not None and not owned['reaped']:
        try:
            with registration_window():
                pid, _ = os.waitpid(child, os.WNOHANG)
                if pid == child:
                    owned['reaped'] = True
        except ChildProcessError:
            if owned['pidfd'] is None:
                raise RuntimeError('Unobserved child settlement cannot be inferred')
            verify_handle(module, owned['pidfd'], owned['handles'][owned['pidfd']])
            ready, _, _ = select.select([owned['pidfd']], [], [], 0)
            if not ready:
                raise RuntimeError('Original pidfd still active after unavailable wait')
            pid = child  # ECHILD + original readable pidfd proves exit and reap.
        if pid == child:
            owned['reaped'] = True
        elif owned['pidfd'] is not None:
            verify_handle(module, owned['pidfd'], owned['handles'][owned['pidfd']])
            signal.pidfd_send_signal(owned['pidfd'], signal.SIGKILL)
        else:
            # Still our direct unreaped child: PID cannot be reused.
            os.kill(child, signal.SIGKILL)
    group = owned['group']
    if owned['groupCreated']:
        if module.identity(group) != owned['groupIdentity']:
            raise RuntimeError('Exact owned group identity differs')
        if not module.current_group_empty(group):
            (group / 'cgroup.kill').write_text('1')
        until = time.monotonic() + 20
        while not module.current_group_empty(group):
            if time.monotonic() >= until:
                raise TimeoutError('Exact controller group remains populated')
            time.sleep(.05)
    if child is not None and not owned['reaped']:
        wait_child(child, False, time.monotonic() + 20, owned)
        owned['reaped'] = True
    # Release the actual handles only after all producers settled. Pending
    # entries survive close failures; never substitute a fresh numeric handle.
    close_failures = []
    for descriptor, expected in list(owned['handles'].items()):
        try:
            with registration_window():
                try:
                    verify_handle(module, descriptor, expected)
                except OSError as failure:
                    if failure.errno != 9:
                        raise
                else:
                    os.close(descriptor)
                del owned['handles'][descriptor]
        except BaseException as failure:
            close_failures.append(failure)
    if close_failures:
        raise close_failures[0]
    if owned['groupCreated']:
        if module.identity(group) != owned['groupIdentity']:
            raise RuntimeError('Exact group changed before removal')
        with registration_window():
            group.rmdir()
            owned['groupCreated'] = False
        if group.exists():
            raise RuntimeError('Owned group physical absence unverified')
    if owned['slotCreated']:
        if module.identity(owned['slot']) != owned['slotIdentity']:
            raise RuntimeError('Exact claim changed before release')
        with registration_window():
            owned['slot'].unlink()
            owned['slotCreated'] = False
        if owned['slot'].exists():
            raise RuntimeError('Owned claim physical absence unverified')
    return not owned['handles'] and not owned['groupCreated'] and not owned['slotCreated'] and (child is None or owned['reaped'])

def probe(module, root, fault, uid, gid, groups):
    run = uuid.uuid4().hex
    owned = {'run': run, 'group': module.CGROUP_ROOT / ('maliev-catalog-' + run),
        'slot': Path('/tmp/maliev-catalog-sdk-exclusive.lock'), 'groupCreated': False,
        'groupAttempted': False,
        'slotCreated': False, 'groupIdentity': None, 'slotIdentity': None,
        'groupFd': None, 'slotFd': None, 'child': None, 'pidfd': None,
        'reaped': False, 'handles': {}}
    result = {'run': run, 'fault': fault, 'limits': LIMITS, 'sdkStarted': False,
        'child': None, 'snapshot': None, 'injectedFailureObserved': False, 'released': False}
    failure = None
    try:
        with registration_window():
            fd = os.open(owned['slot'], os.O_CREAT | os.O_EXCL | os.O_RDWR, 0o600)
            owned.update(slotCreated=True, slotFd=fd)
            owned['handles'][fd] = None
        owned['slotIdentity'] = module.retained_creation_identity(owned['slot'], fd)
        owned['handles'][fd] = owned['slotIdentity']
        os.write(fd, json.dumps({'run': run, 'ownerPid': os.getpid(), 'purpose': 'controller-smoke'}).encode())
        os.fsync(fd)
        if fault == 'slot-acquired':
            raise InjectedAcquisitionFailure()
        owned['groupAttempted'] = True
        with registration_window():
            owned['group'].mkdir()
            owned['groupCreated'] = True
            fd = os.open(owned['group'], os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
            owned['groupFd'] = fd
            owned['handles'][fd] = None
        if fault == 'identity-query':
            raise InjectedAcquisitionFailure()
        owned['groupIdentity'] = module.retained_creation_identity(owned['group'], fd)
        owned['handles'][fd] = owned['groupIdentity']
        for name, value in LIMITS.items():
            (owned['group'] / name).write_text(value)
            if (owned['group'] / name).read_text().strip() != value:
                raise RuntimeError('Actual controller cap did not bind')
        if fault == 'group-handle-acquired':
            raise InjectedAcquisitionFailure()
        with registration_window():
            read_fd, write_fd = os.pipe()
            owned['handles'][read_fd] = None
            owned['handles'][write_fd] = None
        owned['handles'][read_fd] = module.descriptor_identity(read_fd)
        owned['handles'][write_fd] = module.descriptor_identity(write_fd)
        owner_pid = os.getpid()
        with registration_window() as child_mask:
            child = os.fork()
            if child == 0:
                try:
                    for descriptor in owned['handles']:
                        if descriptor != write_fd:
                            os.close(descriptor)
                    module.drop_child_identity(uid, gid, groups)
                    import ctypes
                    libc = ctypes.CDLL(None, use_errno=True)
                    libc.prctl.argtypes = [ctypes.c_int, *([ctypes.c_ulong] * 4)]
                    libc.prctl.restype = ctypes.c_int
                    # UID changes clear PDEATHSIG, so install it after the drop.
                    if libc.prctl(1, signal.SIGKILL, 0, 0, 0) != 0 or os.getppid() != owner_pid:
                        os._exit(127)
                    signal.pthread_sigmask(signal.SIG_SETMASK, child_mask)
                    os.kill(os.getpid(), signal.SIGSTOP)
                    status = dict(line.split(':', 1) for line in Path('/proc/self/status').read_text().splitlines())
                    snapshot = {'pid': os.getpid(), 'uid': os.geteuid(), 'gid': os.getegid(),
                        'groups': sorted(os.getgroups()), 'noNewPrivileges': status['NoNewPrivs'].strip(),
                        'cgroup': Path('/proc/self/cgroup').read_text().strip()}
                    raw = json.dumps(snapshot).encode()
                    if len(raw) > 4096 or os.write(write_fd, raw) != len(raw):
                        os._exit(126)
                    os.close(write_fd)
                    child_expiry = time.monotonic() + 30
                    while time.monotonic() < child_expiry:
                        time.sleep(1)
                    os._exit(0)
                except BaseException:
                    os._exit(127)
            owned['child'] = child
        result['child'] = child
        with registration_window():
            fd = os.pidfd_open(child)
            owned['pidfd'] = fd
            owned['handles'][fd] = None
        owned['handles'][fd] = module.descriptor_identity(fd)
        wait_child(child, True, time.monotonic() + 20, owned)
        result['startTicks'] = Path(f'/proc/{child}/stat').read_text().rsplit(')', 1)[1].split()[19]
        (owned['group'] / 'cgroup.procs').write_text(str(child))
        if Path(f'/proc/{child}/cgroup').read_text().strip() != '0::/' + owned['group'].name:
            raise RuntimeError('Actual stopped child group assignment differs')
        if fault == 'child-assigned':
            raise InjectedAcquisitionFailure()
        signal.pidfd_send_signal(owned['pidfd'], signal.SIGCONT)
        ready, _, _ = select.select([read_fd], [], [], 20)
        if not ready:
            raise TimeoutError('Bounded privilege snapshot unavailable')
        raw = os.read(read_fd, 4097)
        if not 0 < len(raw) <= 4096:
            raise RuntimeError('Bounded child snapshot malformed')
        result['snapshot'] = module.unique_json(raw)
        check_snapshot(result['snapshot'], child, run, uid, gid, groups)
    except InjectedAcquisitionFailure:
        result['injectedFailureObserved'] = True
    except BaseException as error:
        failure = error
    finally:
        attempts = 0
        while True:
            try:
                if cleanup(module, owned) is not True:
                    raise RuntimeError('Physical release not proved')
                result['released'] = True
                break
            except BaseException as error:
                failure = failure or error  # Original failure is sticky.
                attempts += 1
                if attempts <= 16 or attempts % 180 == 0 and attempts // 180 <= 32:
                    try:
                        module.write_new(root / f'quarantine-{run}-{attempts}.json',
                            {'run': run, 'released': False, 'failureType': type(error).__name__})
                    except BaseException:
                        pass
                try:
                    time.sleep(5)
                except BaseException as error:
                    failure = failure or error
    result['cleanupFailures'] = attempts
    result['groupAbsent'] = not owned['group'].exists()
    result['slotAbsent'] = not owned['slot'].exists()
    module.write_new(root / f'probe-{run}.json', result)
    if failure is not None:
        raise failure
    if not result['groupAbsent'] or not result['slotAbsent']:
        raise RuntimeError('Exact controller physical absence is not proved')
    if (fault != 'none') != result['injectedFailureObserved']:
        raise RuntimeError('Requested acquisition fault was not causally observed')
    return result

def main():
    if (sys.platform != 'linux' or os.geteuid() != 0 or
            sys.argv[1:] not in ([], ['--ordinary-checkout'])):
        raise RuntimeError('Only explicit root-owned Linux controller proof supported')
    transport = Path(__file__).resolve().parents[1]
    module = supervisor(transport, ordinary=sys.argv[1:] == ['--ordinary-checkout'])
    memory = dict(line.split(':', 1) for line in Path('/proc/meminfo').read_text().splitlines())
    if int(memory['MemAvailable'].split()[0]) < 4096 * 1024:
        raise RuntimeError('Memory below4096MiB before controller acquisition')
    if not (module.CGROUP_ROOT / 'cgroup.controllers').exists():
        raise RuntimeError('Actual cgroup-v2 controller unavailable')
    for path in Path('/proc').glob('[0-9]*/exe'):
        try:
            if path.resolve(strict=True).name == 'dotnet':
                raise RuntimeError('Existing SDK blocks controller admission')
        except FileNotFoundError:
            pass
    uid = int(os.environ.get('MALIEV_CATALOG_EVIDENCE_UID', '-1'))
    gid = int(os.environ.get('MALIEV_CATALOG_EVIDENCE_GID', '-1'))
    import pwd
    if uid <= 0 or gid < 0 or pwd.getpwuid(uid).pw_gid != gid:
        raise RuntimeError('Original owner identity unavailable')
    groups = sorted(set(os.getgrouplist(pwd.getpwuid(uid).pw_name, gid)))
    if len(groups) > 32:
        raise RuntimeError('Original groups exceed cap')
    root = transport / 'evidence' / ('controller-' + uuid.uuid4().hex)
    root.mkdir(mode=0o700)
    def cancel(signum, frame):
        raise KeyboardInterrupt('Controller owner cancellation')
    original_handlers = {kind: signal.signal(kind, cancel) for kind in (signal.SIGTERM, signal.SIGINT)}
    try:
        results = [probe(module, root, fault, uid, gid, groups) for fault in FAULTS]
    finally:
        for kind, original in original_handlers.items():
            signal.signal(kind, original)
    module.write_new(root / 'controller-proof.json', {'cases': len(results), 'sdkStarted': False,
        'physicalAbsence': all(row['released'] and row['groupAbsent'] and row['slotAbsent'] for row in results),
        'results': results, 'boundary': 'Registered acquired ownership faults only; unknown birth cannot be released'})
    os.chown(root, uid, gid)
    for path in root.iterdir():
        os.chown(path, uid, gid)
    print('Actual controller proof completed:5 cases, no SDK, owned physical absence verified.')

if __name__ == '__main__':
    main()
