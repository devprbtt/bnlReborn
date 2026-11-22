"""Read back the deployed binary, raw CDB and runtime catalogue. No mutations."""
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import subprocess
import sys
import urllib.error
import urllib.request


def subset(expected, actual, path):
    if isinstance(expected, dict):
        for key, value in expected.items():
            if key in ('_rev', 'hercules_metadata'):
                continue
            if value is None and key not in actual:
                continue  # Runtime JSON omits optional nulls; Couch readback above is exact.
            assert key in actual, path + '.' + key
            subset(value, actual[key], path + '.' + key)
    elif isinstance(expected, list):
        assert len(expected) == len(actual), path
        for index, (a, b) in enumerate(zip(expected, actual)):
            subset(a, b, path + '[' + str(index) + ']')
    elif isinstance(expected, (int, float)) and not isinstance(expected, bool):
        assert math.isclose(expected, actual, rel_tol=1e-6, abs_tol=1e-6), path
    else:
        assert expected == actual, path


def main():
    upload = Path(sys.argv[1])
    root = Path('/opt/bnlreloaded/current')
    manifest = json.loads((upload / 'deploy-manifest.json').read_text())
    plan = json.loads((upload / 'combined.patch.json').read_text())
    spec = importlib.util.spec_from_file_location('writer', upload / 'apply_gameplay_rollout.py')
    writer = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(writer)
    db = writer.Couch()
    cfg = {k.replace('_', '').lower(): v for k, v in json.loads((root / 'Configs/configs.json').read_text()).items()}
    url = 'http://127.0.0.1:' + str(cfg.get('controlpanelport', 8080))
    cards = {}
    for row in plan['cards']:
        assert writer.content(db.request(row['id'])) == writer.content(row['after']), row['id']
        with urllib.request.urlopen(url + '/api/cards/' + row['id'], timeout=20) as response:
            cards[row['id']] = json.load(response)
        if row['before'] is None:
            subset(row['after'], cards[row['id']], row['id'])
        else:
            # The runtime expands defaults/perk lists; validate changed fields rather
            # than requiring untouched stored values to equal computed runtime data.
            def changed(before, after, actual, path):
                if before == after:
                    return
                if isinstance(before, dict) and isinstance(after, dict):
                    for key, value in after.items():
                        if key in ('_rev', 'hercules_metadata'):
                            continue
                        if before.get(key) != value:
                            changed(before.get(key), value, actual.get(key), path + '.' + key)
                else:
                    subset(after, actual, path)
            changed(row['before'], row['after'], cards[row['id']], row['id'])
    historical = json.loads((upload / 'TimeAssaultOriginalTraps.json').read_text())['cards']
    for expected in historical:
        with urllib.request.urlopen(url + '/api/cards/' + expected['_id'], timeout=20) as response:
            cards[expected['_id']] = json.load(response)
        subset(expected, cards[expected['_id']], expected['_id'])
    with urllib.request.urlopen(url + '/api/public/status', timeout=20) as response:
        assert json.load(response)
    with urllib.request.urlopen('https://auth.blocknload.cc/api/v1/status.php', timeout=20) as response:
        auth = json.load(response)
        assert auth['status'] == 'ok' and auth['game_tickets']
    try:
        urllib.request.urlopen('https://cdb.blocknload.cc/_all_dbs', timeout=20)
        raise AssertionError('Anonymous CouchDB unexpectedly accessible')
    except urllib.error.HTTPError as error:
        assert error.code == 401
    def run(*args):
        return subprocess.check_output(args, text=True).strip()
    started = run('systemctl', 'show', 'bnlreloaded.service', '--property=ActiveEnterTimestamp', '--value')
    log = run('journalctl', '-q', '-u', 'bnlreloaded.service', '--since', started, '--no-pager')
    errors = run('journalctl', '-q', '-u', 'bnlreloaded.service', '--since', started, '--no-pager', '-p', 'err..alert')
    listeners = run('ss', '-ltn')
    assert not errors
    assert run('systemctl', 'is-active', 'bnlreloaded.service') == 'active'
    assert 'BNL Reborn signed game-ticket authentication is enabled.' in log
    assert 'Server running; press Ctrl+C to stop.' in log
    assert all('127.0.0.1:' + str(p) in listeners for p in (28100, 28101, 28102))
    assert (root / 'REVISION').read_text().strip() == manifest['revision']
    for name in ('BNLReloadedServer.dll', 'BNLReloadedServer.pdb'):
        assert hashlib.sha256((root / name).read_bytes()).hexdigest() == manifest['sha256'][name]
    (upload / 'runtime-cards.json').write_text(json.dumps(cards, indent=2))
    result = {'revision': manifest['revision'], 'release': str(root.resolve()), 'active': True,
        'raw_and_runtime_documents_verified': len(plan['cards']), 'original_trap_cards_verified': len(historical),
        'public_status_healthy': True, 'public_auth_healthy': True, 'anonymous_couchdb_status': 401,
        'signed_authentication': True, 'startup_complete': True, 'game_listeners_healthy': True,
        'error_journal_empty': True}
    print(json.dumps(result), flush=True)


if __name__ == '__main__':
    main()
