"""Temporarily unlock every character/weapon skin for beta. Dry-run by default."""
import base64, json, os, subprocess, sys, time, urllib.request
from pathlib import Path

c = json.loads(Path('/opt/bnlreloaded/current/Configs/configs.json').read_text())
c = {k.replace('_', '').lower(): v for k, v in c.items()}
base = c['couchdbendpoint'].rstrip('/') + '/' + c['couchdbdatabasename']
auth = 'Basic ' + base64.b64encode((c['couchdbusername'] + ':' + c['couchdbpassword']).encode()).decode()
def request(name, data=None):
    req = urllib.request.Request(base+'/'+name, headers={'Authorization':auth,'Content-Type':'application/json'}, data=None if data is None else json.dumps(data).encode(), method='GET' if data is None else 'PUT')
    with urllib.request.urlopen(req, timeout=30) as r: return json.load(r)
def skins():
    return {d['_id']:d for row in request('_all_docs?include_docs=true')['rows'] if (d:=row.get('doc',{})).get('category')=='skin'}
before = skins()
assert before, 'No skin cards found'
changes = {k:d for k,d in before.items() if d.get('scope') != 'public'}
print(json.dumps({'skin_count':len(before),'unlock_count':len(changes),'unlock_ids':sorted(changes)}), flush=True)
if '--apply' not in sys.argv: sys.exit()
assert os.geteuid()==0
with urllib.request.urlopen('http://127.0.0.1:8080/api/activity',timeout=10) as r: activity=json.load(r)
assert not activity['by_mode'] and activity['spectating']==0, 'Active gameplay: wait before restarting'
backup = Path('/root/config-backups')/('skins-public-beta-'+time.strftime('%Y%m%dT%H%M%SZ',time.gmtime()))
backup.mkdir(mode=0o700)
(backup/'before.json').write_text(json.dumps(changes,indent=2))
for name,d in changes.items(): request(name,{**d,'scope':'public'})
after=skins()
assert set(after)==set(before)
for name,d in after.items():
    assert d.get('scope')=='public'
    assert {k:v for k,v in d.items() if k not in ('scope','_rev')} == {k:v for k,v in before[name].items() if k not in ('scope','_rev')}
subprocess.run(['systemctl','restart','bnlreloaded.service'],check=True)
for _ in range(60):
    time.sleep(1)
    try:
        with urllib.request.urlopen('http://127.0.0.1:8080/api/activity',timeout=2) as r: json.load(r)
        break
    except Exception: pass
else: raise RuntimeError('Service health did not recover; backup '+str(backup))
subprocess.run(['systemctl','is-active','bnlreloaded.service'],check=True)
print(json.dumps({'status':'all_skins_public','skin_count':len(after),'changed':len(changes),'backup':str(backup),'prior_online':activity['online']}))
