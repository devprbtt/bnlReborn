"""Revision-checked live Paradise thumbnail repair; --apply writes, default audits."""
import base64,copy,hashlib,json,sys,time,urllib.request,urllib.error
from pathlib import Path
original='map_sr2_sky_bridge_don_edit'
conquest=original+'_conquest'
c=json.loads(Path('/opt/bnlreloaded/current/Configs/configs.json').read_text())
c={k.replace('_','').lower():v for k,v in c.items()}
base=c['couchdbendpoint'].rstrip('/')+'/'+c['couchdbdatabasename']
auth='Basic '+base64.b64encode((c['couchdbusername']+':'+c['couchdbpassword']).encode()).decode()
def request(name,data=None):
 req=urllib.request.Request(base+'/'+name,headers={'Authorization':auth,'Content-Type':'application/json'},data=None if data is None else json.dumps(data).encode(),method='GET' if data is None else 'PUT')
 with urllib.request.urlopen(req,timeout=30) as r:return json.load(r)


url='https://blocknload.cc/images/maps/paradise-v1.jpg'
ids=['map_sr2_paradise','map_sr2_paradise_weather_test']
before={k:request(k) for k in ids};pool=request('map_list')
with urllib.request.urlopen(url,timeout=30) as r:
 data=r.read();assert r.status==200 and r.headers.get_content_type()=='image/jpeg'
assert hashlib.sha256(data).hexdigest()=='bb86fe563e94c4116149dcaf8e58a2db8279af79a9604e1bbfcfb10112cb5f6e'
print(json.dumps({'before':{k:{f:v.get(f) for f in ['image','large_image']} for k,v in before.items()},'after':url}))
if '--apply' not in sys.argv:sys.exit()
backup=Path('/root/config-backups')/('paradise-thumbnail-'+time.strftime('%Y%m%dT%H%M%SZ',time.gmtime()))
backup.mkdir(mode=0o700);(backup/'cards.before.json').write_text(json.dumps(before,indent=2))
for k,card in before.items():
 after=copy.deepcopy(card);after['image']=after['large_image']=url
 if after!=card:request(k,after)
 saved=request(k)
 for field in set(card)|set(saved):
  if field not in ['_rev','image','large_image']:assert saved.get(field)==card.get(field),field
 assert saved['image']==url and saved['large_image']==url
assert request('map_list')==pool
print(json.dumps({'updated':ids,'backup':str(backup),'rotation_unchanged':True}))
