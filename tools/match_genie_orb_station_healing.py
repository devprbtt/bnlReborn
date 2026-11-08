"""Match normal/Speed Ball orb healing to stations; deploy station suppression code first.

Default online mode is read-only. --apply uses CouchDB revision guards and backs up
the old card. --from-file audits a snapshot without connecting; --output writes an
offline candidate catalogue for fixtures. Only the orb's player_heal changes.
"""
import argparse
import base64
import copy
import json
import math
import time
import urllib.request
from pathlib import Path

AURA = 'effect_hero_djinn_heavy_orb_heal_aura'
REGEN = 'effect_status_regen_health'
ORBS = ('unit_projectile_djinn_heavy_orb', 'unit_projectile_djinn_heavy_orb_speed_ball')
READ = (AURA, REGEN, *ORBS)

def plan(cards):
    aura = cards[AURA]['effect']
    regen = cards[REGEN]['effect']
    assert aura['type'] == 'aura' and regen['type'] == 'buff'
    interval = aura['interval']
    rate = regen['buffs']['health_regen']
    assert math.isclose(interval, .2) and math.isclose(rate, 10), 'Unexpected station rate or orb interval'
    heals = [e for e in aura['interval_effects'] if e['type'] == 'heal']
    assert len(heals) == 1 and heals[0]['player_heal'] in (1.4, 2), 'Unexpected orb heal'
    for name in ORBS:
        assert AURA in json.dumps(cards[name]), 'Orb no longer uses shared aura: '+name
    after = copy.deepcopy(cards[AURA])
    next(e for e in after['effect']['interval_effects'] if e['type'] == 'heal')['player_heal'] = round(rate * interval, 6)
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
    before = cards[AURA]
    after = plan(cards)
    changed = before != after
    print(json.dumps({'changed':changed, 'orb_hp_per_tick':2, 'interval_seconds':.2, 'hp_per_second':10}))
    if args.from_file:
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(json.dumps([after if c['_id']==AURA else c for c in catalogue]), encoding='utf-8')
        return
    if not args.apply or not changed:
        return
    # Detect edits to dependencies too; the PUT itself is revision guarded.
    assert all(request(name)==cards[name] for name in READ), 'Catalogue changed during review'
    backup = Path('/root/config-backups')/('genie-station-healing-'+time.strftime('%Y%m%dT%H%M%SZ', time.gmtime()))
    backup.mkdir(mode=0o700)
    (backup/'before.json').write_text(json.dumps(cards, indent=2))
    request(AURA, after)
    saved = request(AURA)
    assert {k:v for k,v in saved.items() if k!='_rev'} == {k:v for k,v in after.items() if k!='_rev'}, 'Readback differs'
    print(json.dumps({'backup':str(backup), 'revision':saved['_rev']}))

if __name__ == '__main__':
    main()
