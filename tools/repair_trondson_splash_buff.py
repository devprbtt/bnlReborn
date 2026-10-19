import json,base64,urllib.request
from pathlib import Path
c={k.replace('_','').lower():v for k,v in json.loads(Path('/opt/bnlreloaded/current/Configs/configs.json').read_text()).items()}
base=c['couchdbendpoint'].rstrip('/')+'/'+c['couchdbdatabasename']
auth='Basic '+base64.b64encode((c['couchdbusername']+':'+c['couchdbpassword']).encode()).decode()
import sys,time,copy,urllib.parse
key='effect_hero_trondson_heigh_ho_splash_damage_buff'
def request(body=None):
 req=urllib.request.Request(base+'/'+urllib.parse.quote(key,safe=''),headers={'Authorization':auth,'Content-Type':'application/json'},data=None if body is None else json.dumps(body).encode(),method='GET' if body is None else 'PUT')
 with urllib.request.urlopen(req,timeout=30) as r:return json.load(r)
d=request(); after=copy.deepcopy(d)
assert d['category']=='effect' and d['effect']['type']=='buff'
buffs=after['effect']['buffs']
if 'splash_block_damage_bonus' not in buffs:
 print('No malformed splash buff remains.');sys.exit(0)
value=buffs.pop('splash_block_damage_bonus')
assert value==0.5 and after.get('splash_block_damage_bonus',value)==value
after['splash_block_damage_bonus']=value
print(json.dumps({'repair':key,'move':'effect.buffs.splash_block_damage_bonus -> splash_block_damage_bonus','value':value}),flush=True)
if '--apply' in sys.argv:
 backup=Path('/root/config-backups')/('trondson-buff-repair-'+time.strftime('%Y%m%dT%H%M%SZ',time.gmtime()))
 backup.mkdir(mode=0o700)
 (backup/'before.json').write_text(json.dumps(d,indent=2))
 request(after)
 saved=request()
 assert {k:v for k,v in saved.items() if k!='_rev'}=={k:v for k,v in after.items() if k!='_rev'}
 print(json.dumps({'verified':True,'backup':str(backup)}))
