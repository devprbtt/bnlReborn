"""Run as root on the game host; defaults to a read-only preview.

--apply stops the game service, backs up SQLite, changes only Users.rating_mean,
verifies all other data unchanged, and restarts the service. No historical rerating.
"""
import argparse
import collections
import datetime
from decimal import Decimal, ROUND_FLOOR
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import subprocess


def rounded(mean):
    value = Decimal(str(mean))
    if not value.is_finite():
        raise ValueError('Nonfinite rating; refusing adjustment')
    # Native means are displayed x100. A 500-point step is 5 native units.
    return float(min(Decimal(40), ((value + Decimal('2.5')) / 5).to_integral_value(rounding=ROUND_FLOOR) * 5))


def protected_digest(db):
    h = hashlib.sha256()
    tables = [r[0] for r in db.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")]
    for name in tables:
        quoted = '"' + name.replace('"', '""') + '"'
        columns = [r[1] for r in db.execute('PRAGMA table_info(' + quoted + ')') if not (name == 'Users' and r[1] == 'rating_mean')]
        select = ','.join('"' + c.replace('"', '""') + '"' for c in columns)
        h.update(repr((name, columns)).encode())
        for row in db.execute('SELECT ' + select + ' FROM ' + quoted + ' ORDER BY rowid'):
            h.update(repr(row).encode())
    return h.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--release', type=Path, help='Prepared release to activate during the same restart')
    parser.add_argument('--dll-sha256')
    args = parser.parse_args()
    if args.release:
        assert args.apply and args.dll_sha256
        release = args.release.resolve(strict=True)
        assert release.parent == Path('/opt/bnlreloaded/releases')
        assert (release/'REVISION').read_text().strip().startswith(release.name)
        assert hashlib.sha256((release/'BNLReloadedServer.dll').read_bytes()).hexdigest() == args.dll_sha256
    path = Path('/opt/bnlreloaded/shared/PlayerData/playerData.db')
    service = 'bnlreloaded.service'
    assert all(rounded(x) == y for x, y in [(12,10),(15.01,15),(12.5,15),(18,20),(40,40),(99,40),(0,0)])
    stopped = False
    folder = None
    try:
        if args.apply:
            assert os.geteuid() == 0
            subprocess.run(['systemctl','is-active','--quiet',service],check=True)
            subprocess.run(['systemctl','stop',service],check=True)
            stopped = True
        db = sqlite3.connect(path.as_uri() + ('?mode=rw' if args.apply else '?mode=ro'),uri=True)
        if not args.apply:
            db.execute('PRAGMA query_only=ON')
        if args.apply:
            assert db.execute('SELECT COUNT(*) FROM MmrInbox').fetchone()[0] == 0, 'Unsettled MMR inbox; restart and settle before changing ratings'
            stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ')
            folder = Path('/root/config-backups') / ('casual-mmr-round-' + stamp)
            folder.mkdir(mode=0o700)
            (folder/'previous-release').write_text(str(Path('/opt/bnlreloaded/current').resolve())+'\n')
            backup = sqlite3.connect(folder / 'playerData.db')
            db.backup(backup)
            assert backup.execute('PRAGMA quick_check').fetchone()[0] == 'ok'
            backup.close()
            os.chmod(folder / 'playerData.db',0o600)
        db.execute('BEGIN IMMEDIATE' if args.apply else 'BEGIN')
        rows = list(db.execute('SELECT id,rating_mean FROM Users ORDER BY id'))
        changes = [(player, old, rounded(old)) for player, old in rows]
        summary = dict(rule='Nearest 500 displayed Casual MMR; ties upward; cap 4000', accounts=len(rows), changed=sum(old!=new for _,old,new in changes), already_on_target=sum(old==new for _,old,new in changes), above_4000_before=sum(old>40 for _,old in rows), distribution={str(int(k*100)):v for k,v in sorted(collections.Counter(new for _,_,new in changes).items())}, backup=str(folder/'playerData.db') if folder else None, applied=False)
        if args.apply:
            protected = protected_digest(db)
            db.executemany('UPDATE Users SET rating_mean=? WHERE id=?',[(new,player) for player,old,new in changes if old!=new])
            assert protected_digest(db) == protected, 'Unexpected non-Casual-MMR mutation'
            assert list(db.execute('SELECT id,rating_mean FROM Users ORDER BY id')) == [(p,n) for p,_,n in changes]
            assert db.execute('PRAGMA quick_check').fetchone()[0] == 'ok'
            audit = dict(summary, protected_data_sha256=protected, changes=[dict(player=p,old_displayed=o*100,new_displayed=n*100) for p,o,n in changes])
            (folder/'adjustment.json').write_text(json.dumps(audit,indent=2)+'\n')
            os.chmod(folder/'adjustment.json',0o600)
            db.commit()
            summary['applied'] = True
            summary['protected_data_unchanged'] = True
            summary['protected_data_sha256'] = protected
            (folder/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
            if args.release:
                incoming = Path('/opt/bnlreloaded/.current-casual-round')
                assert not incoming.exists() and not incoming.is_symlink()
                incoming.symlink_to(release)
                incoming.replace('/opt/bnlreloaded/current')
                summary['release'] = str(release)
        else:
            db.rollback()
        db.close()
        print(json.dumps(summary,indent=2),flush=True)
    finally:
        if stopped:
            subprocess.run(['systemctl','start',service],check=True)


if __name__ == '__main__':
    main()
