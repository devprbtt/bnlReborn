"""Create missing class-perk CDB cards. Dry run by default; never overwrite tuned cards.
Run on the server after deploying class-perk support: python3 register_class_perks.py --apply

Effect cards (catalogue/class-perk-effects) are created before the perk cards that reference them. An existing
perk card keeps every tuned value; the only change made to one is filling an empty perk_mods list from the repo
(Brawn I gains the Bulwark guard immunity so Brawns never receive that aura).
"""
import argparse, base64, json, time, urllib.request, urllib.error, urllib.parse
from pathlib import Path

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--apply',action='store_true')
parser.add_argument('--config',default='/opt/bnlreloaded/current/Configs/configs.json')
parser.add_argument('--cards-dir',type=Path,default=Path(__file__).parent/'catalogue/class-perks')
parser.add_argument('--effects-dir',type=Path,default=Path(__file__).parent/'catalogue/class-perk-effects')
a=parser.parse_args()
c={k.replace('_','').lower():v for k,v in json.loads(Path(a.config).read_text()).items()}
base=c['couchdbendpoint'].rstrip('/')+'/'+urllib.parse.quote(c['couchdbdatabasename'],safe='')
auth='Basic '+base64.b64encode((c['couchdbusername']+':'+c['couchdbpassword']).encode()).decode()
def request(id,doc=None):
    req=urllib.request.Request(base+'/'+urllib.parse.quote(id,safe=''),data=None if doc is None else json.dumps(doc).encode(),method='GET' if doc is None else 'PUT',headers={'Authorization':auth,'Content-Type':'application/json'})
    try:
        with urllib.request.urlopen(req,timeout=20) as response:return json.load(response)
    except urllib.error.HTTPError as error:
        if error.code==404 and doc is None:return None
        raise RuntimeError('CDB status '+str(error.code)+' for '+id) from None
load=lambda d:[json.loads(p.read_text(encoding='utf-8-sig')) for p in sorted(d.glob('*.json'))]
effects,perks=load(a.effects_dir),load(a.cards_dir)
assert {p['_id'] for p in perks}=={f'perk_class_{k}_{n}' for k in ('brawn','brains','skill') for n in (1,2,3)}
assert all(e['category']=='effect' and e['_id'].startswith('effect_class_') for e in effects)
cards=effects+perks  # creation order
before={card['_id']:request(card['_id']) for card in cards}
plan={}
for card in cards:
    old=before[card['_id']]
    if old is None:plan[card['_id']]='create';continue
    assert old['category']==card['category'],'Unexpected existing card; refusing replacement: '+card['_id']
    if card['category']=='perk':
        assert old['slot_type']=='class' and isinstance(old.get('class_perk'),dict),'Unexpected existing card; refusing replacement: '+card['_id']
        plan[card['_id']]='add perk_mods' if not old.get('perk_mods') and card['perk_mods'] else 'keep'
    else:plan[card['_id']]='keep'
print(json.dumps({'apply':a.apply,'plan':plan,'class_perk_values':{id:(doc or {}).get('class_perk') for id,doc in before.items() if id.startswith('perk_')}}),flush=True)
if not a.apply:raise SystemExit()
# Old binaries reject class perk ids they do not know (II/III) and cannot apply them. Never publish ahead of support.
dll=Path(a.config).parent.parent/'BNLReloadedServer.dll'
assert b'RespawnReductionPercent' in dll.read_bytes(),'Deploy class perk II/III server support first'
backup=Path('/root/config-backups')/('class-perks-'+time.strftime('%Y%m%dT%H%M%SZ',time.gmtime()))
backup.mkdir(mode=0o700)
(backup/'before.json').write_text(json.dumps(before,indent=2))
for card in cards:
    action=plan[card['_id']]
    if action=='create':request(card['_id'],card)
    elif action=='add perk_mods':request(card['_id'],{**before[card['_id']],'perk_mods':card['perk_mods']})
after={card['_id']:request(card['_id']) for card in cards}
for id,doc in after.items():
    assert doc,'missing after apply: '+id
    if id.startswith('perk_') and before[id]:assert doc['class_perk']==before[id]['class_perk'],'tuned values changed: '+id
(backup/'after.json').write_text(json.dumps(after,indent=2))
print(json.dumps({'backup':str(backup),'revisions':{id:doc['_rev'] for id,doc in after.items()}}))
