"""Create missing class-perk CDB cards. Dry run by default; never overwrite tuned cards.
Run on the server after deploying ClassPerkCatalogue support: python3 register_class_perks.py --apply
"""
import argparse, base64, json, time, urllib.request, urllib.error, urllib.parse
from pathlib import Path

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--apply',action='store_true')
parser.add_argument('--config',default='/opt/bnlreloaded/current/Configs/configs.json')
parser.add_argument('--cards-dir',type=Path,default=Path(__file__).parent/'catalogue/class-perks')
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
cards=[json.loads(p.read_text(encoding='utf-8-sig')) for p in sorted(a.cards_dir.glob('*.json'))]
assert {c['_id'] for c in cards}=={'perk_class_brawn_1','perk_class_brains_1','perk_class_skill_1'}
before={card['_id']:request(card['_id']) for card in cards}
for card in cards:
    old=before[card['_id']]
    if old:
        assert old['category']=='perk' and old['slot_type']=='class' and isinstance(old.get('class_perk'),dict),'Unexpected existing card; refusing replacement'
print(json.dumps({'apply':a.apply,'cards':{id:{'exists':doc is not None,'values':doc.get('class_perk') if doc else None} for id,doc in before.items()}}),flush=True)
if not a.apply:raise SystemExit()
# Old binaries cannot deserialize the new slot enum. Never publish ahead of server support.
dll=Path(a.config).parent.parent/'BNLReloadedServer.dll'
assert b'ClassPerkCatalogue' in dll.read_bytes(),'Deploy class-perk server support first'
backup=Path('/root/config-backups')/('class-perks-'+time.strftime('%Y%m%dT%H%M%SZ',time.gmtime()))
backup.mkdir(mode=0o700)
(backup/'before.json').write_text(json.dumps(before,indent=2))
for card in cards:
    if before[card['_id']] is None:request(card['_id'],card)
after={card['_id']:request(card['_id']) for card in cards}
assert all(after[id] and after[id]['class_perk']==(before[id] or next(c for c in cards if c['_id']==id))['class_perk'] for id in after)
(backup/'after.json').write_text(json.dumps(after,indent=2))
print(json.dumps({'backup':str(backup),'revisions':{id:doc['_rev'] for id,doc in after.items()}}))
