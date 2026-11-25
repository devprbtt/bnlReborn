"""Root-only code/maps transaction for beta101. Preview by default; UPLOAD [--apply]."""
import argparse
from datetime import datetime, timezone
import hashlib
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


def health(started, revision, manifest, maps, upload):
    root = Path('/opt/bnlreloaded/current')
    cfg = {k.replace('_', '').lower(): v for k, v in json.loads((root/'Configs/configs.json').read_text()).items()}
    for _ in range(45):
        try:
            log = run('journalctl','-q','-u','bnlreloaded.service','--since',started,'--no-pager')
            listeners = run('ss','-ltn')
            with urllib.request.urlopen('http://127.0.0.1:'+str(cfg.get('controlpanelport',8080))+'/api/public/home',timeout=2) as response:
                home = json.load(response)
            if (run('systemctl','is-active','bnlreloaded.service') == 'active' and
                all('127.0.0.1:'+str(p) in listeners for p in (28100,28101,28102)) and
                'BNL Reborn signed game-ticket authentication is enabled.' in log and
                'Server running; press Ctrl+C to stop.' in log and
                home['play']['map_preferences_version'] == 2 and len(home['play']['friendly_maps']) == 26):
                break
        except Exception:
            pass
        time.sleep(1)
    else:
        raise RuntimeError('Startup health gate failed')
    assert not run('journalctl','-q','-u','bnlreloaded.service','--since',started,'--no-pager','-p','err..alert')
    assert (root/'REVISION').read_text().strip() == revision
    assert sha(root/'BNLReloadedServer.dll') == manifest['sha256']['BNLReloadedServer.dll']
    for row in manifest['maps']:
        assert sha(maps/(row['map_id']+'.bnlbin')) == row['after_sha256']


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('upload',type=Path)
    parser.add_argument('--apply',action='store_true')
    args = parser.parse_args()
    assert os.geteuid() == 0
    upload = args.upload.resolve()
    manifest = json.loads((upload/'deploy-manifest.json').read_text())
    revision = manifest['revision']
    assert re.fullmatch('[0-9a-f]{40}',revision)
    assert str(upload) == '/home/bnladmin/bnl-server-beta101-'+revision[:12]
    base = Path('/opt/bnlreloaded')
    current = base/'current'
    previous = current.resolve()
    assert current.is_symlink() and previous.parent == base/'releases'
    assert (current/'REVISION').read_text().strip() == manifest['expected_base']
    maps = (current/'Maps').resolve()
    assert maps == base/'shared/Maps' and maps.is_dir()
    assert len(manifest['maps']) == 4
    assert len({r['map_id'] for r in manifest['maps']}) == 4
    for name,digest in manifest['sha256'].items():
        assert Path(name).name == name and sha(upload/name) == digest,name
    for row in manifest['maps']:
        name = row['map_id']+'.bnlbin'
        assert re.fullmatch(r'map_sr2_[a-z_]+\.bnlbin',name)
        assert sha(upload/name) == row['after_sha256'],name
        assert sha(maps/name) == row['before_sha256'],'Map baseline changed: '+name
    assert (upload/'REVISION').read_text().strip() == revision
    assert json.loads((current/'BNLReloadedServer.deps.json').read_text())['libraries'] == json.loads((upload/'BNLReloadedServer.deps.json').read_text())['libraries'],'Dependency drift'
    assert run('systemctl','is-active','bnlreloaded.service') == 'active'
    print(json.dumps({'preflight':'passed','base':manifest['expected_base'],'revision':revision,'map_files':4,'catalogue_changes':0,'apply':args.apply}),flush=True)
    if not args.apply:
        return
    backup = Path('/root/config-backups')/('beta101-'+datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ'))
    backup.mkdir(mode=0o700)
    (backup/'maps').mkdir(mode=0o700)
    (backup/'previous-release').write_text(str(previous)+'\n')
    (backup/'deploy-manifest.json').write_bytes((upload/'deploy-manifest.json').read_bytes())
    for row in manifest['maps']:
        name=row['map_id']+'.bnlbin'; shutil.copy2(maps/name,backup/'maps'/name)
    release = base/'releases'/revision[:12]
    assert not release.exists(),'Immutable release target exists'
    subprocess.run(['cp','-a','--reflink=auto',str(previous),str(release)],check=True)
    for name in ('BNLReloadedServer.dll','BNLReloadedServer.pdb','REVISION'):
        shutil.copyfile(upload/name,release/name); os.chmod(release/name,0o644)
    started = datetime.now(timezone.utc).isoformat()
    stopped = False
    try:
        subprocess.run(['systemctl','stop','bnlreloaded.service'],check=True); stopped=True
        assert (current/'REVISION').read_text().strip() == manifest['expected_base']
        source=base/'shared/PlayerData/playerData.db'
        with sqlite3.connect(source.as_uri()+'?mode=ro',uri=True) as src,sqlite3.connect(backup/'playerData.db') as dst:
            src.backup(dst); assert dst.execute('PRAGMA quick_check').fetchone()[0] == 'ok'
        os.chmod(backup/'playerData.db',0o600)
        for row in manifest['maps']:
            name=row['map_id']+'.bnlbin'; assert sha(maps/name)==row['before_sha256']
            temporary=maps/(name+'.beta101-new'); assert not temporary.exists()
            shutil.copyfile(upload/name,temporary);os.chmod(temporary,0o644);temporary.replace(maps/name)
        pointer=base/'.current-beta101';assert not pointer.exists() and not pointer.is_symlink();pointer.symlink_to(release);pointer.replace(current)
        subprocess.run(['systemctl','start','bnlreloaded.service'],check=True)
        health(started,revision,manifest,maps,upload)
    except Exception:
        if stopped:
            subprocess.run(['systemctl','stop','bnlreloaded.service'],check=True)
            for row in manifest['maps']:
                name=row['map_id']+'.bnlbin';temporary=maps/(name+'.beta101-rollback')
                shutil.copyfile(backup/'maps'/name,temporary);os.chmod(temporary,0o644);temporary.replace(maps/name)
            pointer=base/'.current-beta101-rollback';assert not pointer.exists() and not pointer.is_symlink();pointer.symlink_to(previous);pointer.replace(current)
            subprocess.run(['systemctl','start','bnlreloaded.service'],check=True)
            print(json.dumps({'rolled_back_to':str(previous),'maps_restored':4}),flush=True)
        raise
    result={'revision':revision,'release':str(release),'backup':str(backup),'status':'healthy','maps_applied':4,'catalogue_changes':0,'dll_sha256':sha(current/'BNLReloadedServer.dll')}
    (backup/'result.json').write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(result),flush=True)


if __name__ == '__main__':
    main()
