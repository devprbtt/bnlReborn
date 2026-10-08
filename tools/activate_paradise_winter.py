"""Dry-run by default. Activate the shipped beta.56 winter map when the server is idle."""
import argparse, base64, copy, hashlib, json, os, shutil, subprocess, time, urllib.request, urllib.error, zlib
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('payload', type=Path)
parser.add_argument('--apply', action='store_true')
args = parser.parse_args()
original_id, winter_id = 'map_sr2_paradise', 'map_sr2_paradise_winter'
image_url = 'https://blocknload.cc/images/maps/paradise-winter-v1.png'
root = Path('/opt/bnlreloaded/current')
config = json.loads((root/'Configs/configs.json').read_text())
config = {k.replace('_', '').lower(): v for k,v in config.items()}
base = config['couchdbendpoint'].rstrip('/')+'/'+config['couchdbdatabasename']
auth = 'Basic '+base64.b64encode((config['couchdbusername']+':'+config['couchdbpassword']).encode()).decode()
def request(name, data=None, method=None):
    req = urllib.request.Request(base+'/'+name, headers={'Authorization':auth,'Content-Type':'application/json'},
        data=None if data is None else json.dumps(data).encode(), method=method or ('GET' if data is None else 'PUT'))
    with urllib.request.urlopen(req, timeout=30) as response: return json.load(response)
def optional(name):
    try: return request(name)
    except urllib.error.HTTPError as error:
        if error.code != 404: raise
        return None
def panel(route):
    with urllib.request.urlopen('http://127.0.0.1:8080/api/'+route, timeout=10) as response: return json.load(response)
def version(text): return int(text.strip().split('.')[-1])
def sha(data): return hashlib.sha256(data).hexdigest()
def clean(document): return {k:v for k,v in document.items() if k != '_rev'}
def atomic(path, data, mode=0o644):
    temp = path.with_name(path.name+'.winter-new')
    with temp.open('xb') as stream: stream.write(data)
    os.chmod(temp, mode)
    if path.exists():
        stat=path.stat();os.chown(temp, stat.st_uid, stat.st_gid)
    os.replace(temp, path)

manifest = json.loads(Path('/srv/bnl-reborn-downloads/channels/beta/windows-x64/manifest.json').read_bytes())
assert version(manifest['version']) >= 56, 'Publish beta.56 first'
source_path = root/'Maps'/f'{original_id}.bnlbin'
target = root/'Maps'/f'{winter_id}.bnlbin'
assert target.parent.resolve().is_relative_to(Path('/opt/bnlreloaded')), 'Unexpected map storage'
source_raw = source_path.read_bytes()
payload = args.payload.read_bytes()
assert sha(source_raw) == '0cfaa007afc1383937eecee7cf5eee5882f08da02f057d2b5aad6c5bcda24d8a'
assert sha(payload) == 'a79b925696a3a7b97c20fc86ec3eb6d42dd6d42bb108edbaef8c87357f164f50'
original = json.loads(zlib.decompress(source_raw))
winter = json.loads(zlib.decompress(payload))
assert winter['map_id'] == winter_id and winter['map']['properties']['render'] == 'DaytimeWarm_winter'
comparison = copy.deepcopy(winter['map']);comparison['properties']['render'] = original['map']['properties']['render']
assert comparison == original['map'], 'Gameplay changed'
source_card, before = request(original_id), request('map_list')
card = copy.deepcopy(source_card)
card.pop('_rev', None);card.pop('hercules_metadata', None)
card.update(_id=winter_id, name={'text':winter['name'],'data':{}}, description={'text':winter['description'],'data':{}}, image=image_url, large_image=image_url)
existing = optional(winter_id)
if existing is not None:
    assert clean(existing) == card and target.read_bytes() == payload and winter_id in before['custom']
    assert any(m['key']==winter_id for m in panel('maps')['maps'])
    print('Paradise - Winter already active');raise SystemExit(0)
assert not target.exists(), 'Unexpected existing map file'
assert original_id in before['custom'], 'Original Paradise missing from Custom pool'
after = copy.deepcopy(before);after['custom'] = before['custom']+[winter_id]
assert winter_id not in before['custom']
activity = panel('activity')
print(json.dumps({'map':winter_id, 'name':winter['name'], 'pool':'custom', 'gameplay_unchanged':True,
    'render':winter['map']['properties']['render'], 'activity':activity, 'minimum_client':'0.2.0-beta.56'}), flush=True)
if not args.apply: raise SystemExit(0)
assert os.geteuid() == 0, 'Run apply through sudo'
assert activity['online'] == activity['in_menu'] and activity['spectating'] == 0 and not activity['by_mode'], 'Active game: wait before restarting'
assert all(q['state']=='waiting' and not q['confirm_deadline'] for q in panel('queues')['queues']), 'Match confirmation active'
with urllib.request.urlopen(image_url, timeout=20) as response: assert response.status == 200 and response.headers.get_content_type() == 'image/png'
minimum = root/'Configs/minimum_client_version.txt'
old_minimum = minimum.read_bytes()
assert version(old_minimum.decode()) <= version(manifest['version'])
backup = Path('/root/config-backups')/time.strftime('paradise-winter-%Y%m%dT%H%M%SZ', time.gmtime())
backup.mkdir(mode=0o700)
(backup/'map_list.before.json').write_text(json.dumps(before,indent=2))
(backup/'source-card.json').write_text(json.dumps(source_card,indent=2))
(backup/'minimum_client_version.before.txt').write_bytes(old_minimum)
(backup/'activation.json').write_text(json.dumps({'map_path':str(target), 'map_sha256':sha(payload), 'source_sha256':sha(source_raw), 'card':card},indent=2))
card_written = pool_written = False
try:
    subprocess.run(['systemctl','stop','bnlreloaded.service'],check=True)
    atomic(target, payload)
    if version(old_minimum.decode()) < 56: atomic(minimum,b'0.2.0-beta.56\n')
    request(winter_id,card);card_written=True
    request('map_list',after);pool_written=True
    subprocess.run(['systemctl','start','bnlreloaded.service'],check=True)
    deadline=time.monotonic()+60
    while True:
        try:
            status=panel('status')
            maps=panel('maps')
            selected=next(m for m in maps['maps'] if m['key']==winter_id)
            assert all(status[k] for k in ['master_running','region_running','match_running'])
            assert selected['name']==winter['name'] and selected['pools']==['custom']
            break
        except Exception:
            if time.monotonic() >= deadline: raise
            time.sleep(1)
    saved=request('map_list')
    assert clean(saved)==clean(after) and clean(request(winter_id))==card
    assert request(original_id)==source_card and source_path.read_bytes()==source_raw
    assert target.read_bytes()==payload
except Exception:
    subprocess.run(['systemctl','stop','bnlreloaded.service'],check=True)
    if pool_written:
        current=request('map_list');assert clean(current)==clean(after),'Concurrent pool change: use backup for manual rollback'
        restored=copy.deepcopy(before);restored['_rev']=current['_rev'];request('map_list',restored)
    if card_written:
        current=request(winter_id);assert clean(current)==card
        request(winter_id+'?rev='+current['_rev'],method='DELETE')
    if target.exists():
        assert target.read_bytes()==payload;target.unlink()
    atomic(minimum,old_minimum)
    subprocess.run(['systemctl','start','bnlreloaded.service'],check=True)
    raise
print(json.dumps({'active':True,'map':selected,'backup':str(backup),'pool_revision':saved['_rev'],'minimum_client':minimum.read_text().strip()}),flush=True)
