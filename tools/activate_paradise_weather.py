import base64,copy,hashlib,json,sys,time,urllib.request,urllib.error
from pathlib import Path
original='map_sr2_paradise'
dynamic=original+'_weather_test'
c=json.loads(Path('/opt/bnlreloaded/current/Configs/configs.json').read_text())
c={k.replace('_','').lower():v for k,v in c.items()}
base=c['couchdbendpoint'].rstrip('/')+'/'+c['couchdbdatabasename']
auth='Basic '+base64.b64encode((c['couchdbusername']+':'+c['couchdbpassword']).encode()).decode()
def request(name,data=None):
 req=urllib.request.Request(base+'/'+name,headers={'Authorization':auth,'Content-Type':'application/json'},data=None if data is None else json.dumps(data).encode(),method='GET' if data is None else 'PUT')
 with urllib.request.urlopen(req,timeout=30) as r:return json.load(r)


import zlib
ids=[original,dynamic]
def optional(name):
 try:return request(name)
 except urllib.error.HTTPError as e:
  if e.code!=404:raise
  return None
before=request('map_list'); existing={k:optional(k) for k in ids}
source=json.loads((Path(__file__).resolve().parent/'original-card.json').read_text(encoding='utf-8-sig'))
source.pop('_rev',None);source.pop('hercules_metadata',None)
variant=copy.deepcopy(source);variant['_id']=dynamic
variant['name']={'text':'Paradise - Dynamic Weather','data':{}}
variant['description']={'text':'Day to night in 20 minutes; daylight returns at 25 minutes.','data':{}}
cards={original:source,dynamic:variant}
files={k:Path('/opt/bnlreloaded/current/Maps',k+'.bnlbin') for k in ids}
hashes={k:hashlib.sha256(p.read_bytes()).hexdigest() for k,p in files.items()}
assert json.loads(zlib.decompress(files[original].read_bytes()))['map']==json.loads(zlib.decompress(files[dynamic].read_bytes()))['map']
after=copy.deepcopy(before)
for pool in ['friendly','custom']:after[pool]=list(dict.fromkeys(before[pool]+ids))
assert after['ranked']==before['ranked']
print(json.dumps({'existing':{k:v is not None for k,v in existing.items()},'additions':ids,'preserves_existing':all(all(k in after[p] for k in before[p]) for p in ['friendly','custom']),'map_hashes':hashes}),flush=True)
if '--apply' not in sys.argv:sys.exit()
assert Path('/opt/bnlreloaded/current/Configs/minimum_client_version.txt').read_text().strip()=='0.2.0-beta.52','Publish beta.52 and set minimum before activation'
backup=Path('/root/config-backups')/('paradise-weather-'+time.strftime('%Y%m%dT%H%M%SZ',time.gmtime()))
backup.mkdir(mode=0o700)
(backup/'map_list.before.json').write_text(json.dumps(before,indent=2))
(backup/'cards.before.json').write_text(json.dumps(existing,indent=2))
for k in ids:
 if existing[k] is None:request(k,cards[k])
 else:assert existing[k]['name']==cards[k]['name'],'Unexpected existing card; do not overwrite'
if after!=before:request('map_list',after)
saved=request('map_list')
assert all(saved[p]==after[p] for p in ['friendly','custom','ranked','friendly_noob'])
assert all(hashlib.sha256(p.read_bytes()).hexdigest()==hashes[k] for k,p in files.items())
print(json.dumps({'backup':str(backup),'pool_revision':saved['_rev'],'names':[request(k)['name']['text'] for k in ids]}),flush=True)
