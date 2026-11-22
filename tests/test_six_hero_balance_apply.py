"""Exercise the deployment writer against a fake CouchDB; no network or writes."""
import contextlib
import copy
import io
import json
import runpy
import sys
import urllib.request
from pathlib import Path
from unittest.mock import patch

original = {c['_id']: c for c in json.loads(Path(sys.argv[1]).read_text(encoding='utf-8'))}
plan_path = Path(sys.argv[2]) if len(sys.argv) > 2 else Path('catalogue/balance-20261122/six-hero-balance.patch.json')
plan = json.loads(plan_path.read_text())
candidate = copy.deepcopy(original)
for row in plan['cards']:
    for c in row['changes']:
        p = candidate[row['id']]
        for k in c['path'][:-1]:
            p = p[k]
        p[c['path'][-1]] = c['after']

class Response(io.BytesIO):
    def __enter__(self):
        return self
    def __exit__(self, *args):
        self.close()

def clean(doc):
    return {k: v for k, v in doc.items() if k != '_rev'}

for scenario in ['dry run', 'already applied', 'revision drift', 'apply', 'lost response rollback', 'concurrent rollback protection']:
    state = copy.deepcopy(candidate if scenario == 'already applied' else original)
    first = plan['cards'][0]['id']
    if scenario == 'revision drift':
        state[first]['_rev'] = 'changed-by-other-author'
    puts, files = [], {}
    def read(path, *args, **kwargs):
        if str(path).replace('\\', '/').endswith('Configs/configs.json'):
            return json.dumps({'couch_db_endpoint': 'http://fake.invalid', 'couch_db_database_name': 'catalogue',
                               'couch_db_username': 'fixture', 'couch_db_password': 'fixture'})
        return json.dumps(plan)
    def request(req, **kwargs):
        cid = req.full_url.rsplit('/', 1)[-1]
        if req.get_method() == 'PUT':
            data = json.loads(req.data)
            assert data['_rev'] == state[cid]['_rev'], 'mock enforces Couch revision conflicts'
            state[cid] = dict(data, _rev=str(len(puts) + 100))
            puts.append(cid)
            if len(puts) == 1 and scenario in ['lost response rollback', 'concurrent rollback protection']:
                if scenario == 'concurrent rollback protection':
                    state[cid]['concurrent_author_field'] = 'keep me'
                raise TimeoutError('simulated lost committed PUT response')
        return Response(json.dumps(state[cid]).encode())
    args = ['apply_six_hero_balance.py', 'fixture.patch.json', '--expected-card-count', str(len(plan['cards']))]
    if scenario not in ['dry run', 'already applied', 'revision drift']:
        args.append('--apply')
    failure = None
    with patch.object(sys, 'argv', args), patch.object(Path, 'read_text', read), \
         patch.object(Path, 'mkdir', lambda *a, **k: None), \
         patch.object(Path, 'write_text', lambda p, text, *a, **k: files.update({str(p): text})), \
         patch.object(urllib.request, 'urlopen', request), contextlib.redirect_stdout(io.StringIO()):
        try:
            runpy.run_path('tools/apply_six_hero_balance.py', run_name='__main__')
        except BaseException as error:
            failure = error
    if scenario in ['dry run', 'already applied']:
        assert not puts and isinstance(failure, SystemExit) and failure.code == 0
    elif scenario == 'revision drift':
        assert not puts and isinstance(failure, AssertionError)
    elif scenario == 'apply':
        assert failure is None and len(puts) == len(plan['cards']) and files
        assert all(clean(state[k]) == clean(candidate[k]) for k in state)
    elif scenario == 'lost response rollback':
        assert isinstance(failure, TimeoutError) and len(puts) == 2
        assert all(clean(state[k]) == clean(original[k]) for k in state)
    else:
        assert isinstance(failure, RuntimeError) and len(puts) == 1
        assert state[first]['concurrent_author_field'] == 'keep me'
    print('PASS', scenario)
print('SIX_HERO_APPLY PASS 6 scenarios; no real network/filesystem changes')
