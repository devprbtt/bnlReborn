"""Prepare editable Fast Restock descriptions for station-only healing and ammo.

Deploy the matching server fix before applying. Online mode is read-only unless
--apply is supplied; writes use revision guards, backup and readback. Offline
--from-file/--output prepares a candidate catalogue. Custom text is preserved.
"""
import argparse
import base64
import copy
import json
import time
import urllib.request
from pathlib import Path

IDS = ('perk_def_fast_restock', 'shop_item_perk_def_fast_restock')
READ = IDS
OLD_SCOPES = (
    'healing from health and ammo stations and ammo regeneration from all sources',
    'healing and ammo regeneration from all sources',
)
NEW_SCOPE = 'healing and ammo regeneration from health and ammo stations'

def plan(card):
    after = copy.deepcopy(card)
    description = after.get('description')
    if not isinstance(description, dict):
        return after
    text = description.get('text') or ''
    for old in OLD_SCOPES:
        if old in text:
            description['text'] = text.replace(old, NEW_SCOPE)
            # One-time migration only. Later CDB edits/localizations remain authoritative.
            description['data'] = {}
            break
    return after


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--from-file', type=Path)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    if args.from_file:
        assert not args.apply, 'Offline mode cannot apply to production'
        catalogue = json.loads(args.from_file.read_bytes())
        cards = {c['_id']: c for c in catalogue}
    else:
        assert not args.output, '--output is only for an offline snapshot'
        config = json.loads(Path('/opt/bnlreloaded/current/Configs/configs.json').read_text())
        c = {k.replace('_', '').lower(): v for k, v in config.items()}
        base = c['couchdbendpoint'].rstrip('/')+'/'+c['couchdbdatabasename']
        auth = 'Basic '+base64.b64encode((c['couchdbusername']+':'+c['couchdbpassword']).encode()).decode()
        def request(name, data=None):
            req = urllib.request.Request(base+'/'+name, data=None if data is None else json.dumps(data).encode(),
                method='GET' if data is None else 'PUT', headers={'Authorization':auth, 'Content-Type':'application/json'})
            with urllib.request.urlopen(req, timeout=30) as response:
                return json.load(response)
        cards = {name: request(name) for name in READ}
    changes = {name: plan(cards[name]) for name in IDS if plan(cards[name]) != cards[name]}
    print(json.dumps({'changed': list(changes), 'descriptions': {
        name: plan(cards[name]).get('description') for name in IDS}}))
    if args.from_file:
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(json.dumps([changes.get(c['_id'], c) for c in catalogue]), encoding='utf-8')
        return
    if not args.apply or not changes:
        return
    # Detect edits to dependencies too; the PUT itself is revision guarded.
    assert all(request(name)==cards[name] for name in READ), 'Catalogue changed during review'
    backup = Path('/root/config-backups')/('fast-restock-stations-'+time.strftime('%Y%m%dT%H%M%SZ', time.gmtime()))
    backup.mkdir(mode=0o700)
    (backup/'before.json').write_text(json.dumps(cards, indent=2))
    for name, after in changes.items():
        request(name, after)
        saved = request(name)
        assert {k:v for k,v in saved.items() if k!='_rev'} == {k:v for k,v in after.items() if k!='_rev'}, 'Readback differs: '+name
    print(json.dumps({'backup':str(backup), 'updated':list(changes)}))

if __name__ == '__main__':
    main()
