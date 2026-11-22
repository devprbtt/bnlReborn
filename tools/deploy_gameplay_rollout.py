"""Run on host as root: UPLOAD_DIR [--apply]. Default is a read-only preflight."""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import sqlite3
import subprocess
import time
import urllib.request


def run(*args):
    return subprocess.check_output(args, text=True).strip()


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('upload', type=Path)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    upload = args.upload.resolve()
    manifest = json.loads((upload / 'deploy-manifest.json').read_text())
    revision = manifest['revision']
    assert re.fullmatch('[0-9a-f]{40}', revision)
    assert str(upload) == '/home/bnladmin/bnl-server-hotfix-' + revision[:12]
    assert os.geteuid() == 0
    root = Path('/opt/bnlreloaded')
    current = root / 'current'
    previous = current.resolve()
    assert current.is_symlink() and previous.parent == root / 'releases'
    assert (current / 'REVISION').read_text().strip() == manifest['expected_base']
    for name in ('BNLReloadedServer.dll', 'BNLReloadedServer.pdb', 'BNLReloadedServer.deps.json',
                 'combined.patch.json', 'apply_gameplay_rollout.py', 'deploy_gameplay_rollout.py'):
        assert sha(upload / name) == manifest['sha256'][name], name
    assert (upload / 'REVISION').read_text().strip() == revision
    assert json.loads((current / 'BNLReloadedServer.deps.json').read_text())['libraries'] == json.loads(
        (upload / 'BNLReloadedServer.deps.json').read_text())['libraries'], 'Dependency drift'
    spec = importlib.util.spec_from_file_location('writer', upload / 'apply_gameplay_rollout.py')
    writer = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(writer)
    plan = json.loads((upload / 'combined.patch.json').read_text())
    db = writer.Couch()
    pending = writer.preflight(db, plan)
    assert len(pending) == 19
    assert run('systemctl', 'is-active', 'bnlreloaded.service') == 'active'
    print(json.dumps({'preflight': 'passed', 'pending_cards': 19, 'base': manifest['expected_base'],
                      'revision': revision, 'apply': args.apply}), flush=True)
    if not args.apply:
        return
    backup = Path('/root/config-backups') / ('gameplay-rollout-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ'))
    backup.mkdir(mode=0o700)
    (backup / 'previous-release').write_text(str(previous) + '\n')
    (backup / 'before.json').write_text(json.dumps({cid: old for cid, old, _ in pending}, indent=2))
    (backup / 'patch.json').write_text(json.dumps(plan, indent=2))
    (backup / 'manifest.json').write_text(json.dumps(manifest, indent=2))
    print('ROLLBACK_SNAPSHOT=' + str(backup), flush=True)
    release = root / 'releases' / revision[:12]
    assert not release.exists(), 'Release target already exists'
    subprocess.run(['cp', '-a', '--reflink=auto', str(previous), str(release)], check=True)
    for name in ('BNLReloadedServer.dll', 'BNLReloadedServer.pdb', 'REVISION'):
        shutil.copyfile(upload / name, release / name)
        os.chmod(release / name, 0o644)
    started = datetime.now(timezone.utc).isoformat()
    stopped = False
    catalogue_attempted = False
    try:
        subprocess.run(['systemctl', 'stop', 'bnlreloaded.service'], check=True)
        stopped = True
        assert (current / 'REVISION').read_text().strip() == manifest['expected_base']
        # Back up player data after a clean stop; do not restore it on code-only rollback.
        source = root / 'shared/PlayerData/playerData.db'
        with sqlite3.connect(source.as_uri() + '?mode=ro', uri=True) as src, sqlite3.connect(backup / 'playerData.db') as dst:
            src.backup(dst)
            assert dst.execute('PRAGMA quick_check').fetchone()[0] == 'ok'
        os.chmod(backup / 'playerData.db', 0o600)
        pending = writer.preflight(db, plan)
        assert len(pending) == 19
        catalogue_attempted = True
        writer.apply(db, pending)
        pointer = root / '.current-gameplay-rollout'
        assert not pointer.exists() and not pointer.is_symlink()
        pointer.symlink_to(release)
        pointer.replace(current)
        subprocess.run(['systemctl', 'start', 'bnlreloaded.service'], check=True)
        cfg = {k.replace('_', '').lower(): v for k, v in json.loads((current / 'Configs/configs.json').read_text()).items()}
        port = cfg.get('controlpanelport', 8080)
        healthy = False
        for _ in range(45):
            try:
                log = run('journalctl', '-q', '-u', 'bnlreloaded.service', '--since', started, '--no-pager')
                listeners = run('ss', '-ltn')
                with urllib.request.urlopen('http://127.0.0.1:' + str(port) + '/api/public/status', timeout=2) as response:
                    status = json.load(response)
                healthy = (run('systemctl', 'is-active', 'bnlreloaded.service') == 'active' and bool(status) and
                    all('127.0.0.1:' + str(p) in listeners for p in (28100, 28101, 28102)) and
                    'BNL Reborn signed game-ticket authentication is enabled.' in log and
                    'Server running; press Ctrl+C to stop.' in log)
                if healthy:
                    break
            except Exception:
                pass
            time.sleep(1)
        assert healthy, 'Startup health gate failed'
        assert not run('journalctl', '-q', '-u', 'bnlreloaded.service', '--since', started, '--no-pager', '-p', 'err..alert')
        for row in plan['cards']:
            assert writer.content(db.request(row['id'])) == writer.content(row['after']), row['id']
        assert (current / 'REVISION').read_text().strip() == revision and sha(current / 'BNLReloadedServer.dll') == manifest['sha256']['BNLReloadedServer.dll']
    except Exception:
        if stopped:
            subprocess.run(['systemctl', 'stop', 'bnlreloaded.service'], check=True)
            # Roll data back before reverting the binary that understands new Tony effects.
            try:
                if catalogue_attempted:
                    writer.rollback(db, pending)
            except Exception:
                # Preserve the binary capable of handling any concurrently retained new effects.
                pointer = root / '.current-gameplay-recovery'
                pointer.symlink_to(release)
                pointer.replace(current)
                subprocess.run(['systemctl', 'start', 'bnlreloaded.service'], check=True)
                raise
            pointer = root / '.current-gameplay-rollback'
            pointer.symlink_to(previous)
            pointer.replace(current)
            subprocess.run(['systemctl', 'start', 'bnlreloaded.service'], check=True)
            print('ROLLED_BACK_TO=' + str(previous), flush=True)
        raise
    result = {'revision': revision, 'release': str(release), 'backup': str(backup),
              'applied_cards': 19, 'status': 'healthy', 'dll_sha256': sha(current / 'BNLReloadedServer.dll')}
    (backup / 'result.json').write_text(json.dumps(result, indent=2))
    print(json.dumps(result), flush=True)


if __name__ == '__main__':
    main()
