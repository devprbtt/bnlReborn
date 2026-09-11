"""Persist the Conquest map card and replace classic Don Edit in casual rotation.
Run on the production host; default is read-only. --apply writes revision-checked
CouchDB documents and keeps a private rollback snapshot. No service restart.
"""
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
source=request(original);before=request('map_list')
try:existing=request(conquest)
except urllib.error.HTTPError as e:
 if e.code!=404:raise
 existing=None
assert source['category']=='map' and before['category']=='map_list'
files={k:Path('/opt/bnlreloaded/current/Maps',k+'.bnlbin') for k in [original,conquest]}
assert all(p.is_file() for p in files.values())
hashes={k:hashlib.sha256(p.read_bytes()).hexdigest() for k,p in files.items()}
new=copy.deepcopy(source);new.pop('_rev',None);new.pop('hercules_metadata',None);new['_id']=conquest
new['name']={'text':'Sky Bridge Don Edit - Conquest','data':{}}
new['description']={'text':'Capture zones to earn a Block Buster attack.','data':{}}
after=copy.deepcopy(before)
assert original in before['friendly'] or conquest in before['friendly']
after['friendly']=list(dict.fromkeys(conquest if x==original else x for x in before['friendly']))
after['custom']=list(dict.fromkeys(before['custom']+[conquest]))
print(json.dumps({'card_exists':existing is not None,'casual_swap':original+' -> '+conquest,'custom_keeps_both':original in after['custom'] and conquest in after['custom'],'ranked_unchanged':after['ranked']==before['ranked'],'map_hashes':hashes}),flush=True)
if '--apply' not in sys.argv:sys.exit()
backup=Path('/root/config-backups')/('conquest-map-pool-'+time.strftime('%Y%m%dT%H%M%SZ',time.gmtime()))
backup.mkdir(mode=0o700)
for name,value in [('map_list.before.json',before),('original-map.json',source),('conquest.before.json',existing)]:
 (backup/name).write_text(json.dumps(value,indent=2))
if existing is None:request(conquest,new)
if after!=before:request('map_list',after) # _rev rejects concurrent edits instead of overwriting them.
saved=request('map_list');card=request(conquest)
assert saved['friendly']==after['friendly'] and saved['custom']==after['custom']
assert saved['ranked']==before['ranked'] and saved.get('friendly_noob')==before.get('friendly_noob')
assert request(original)==source
assert all(hashlib.sha256(p.read_bytes()).hexdigest()==hashes[k] for k,p in files.items())
print(json.dumps({'backup':str(backup),'map_revision':card['_rev'],'pool_revision':saved['_rev'],'friendly_count':len(saved['friendly']),'custom_count':len(saved['custom'])}),flush=True)
