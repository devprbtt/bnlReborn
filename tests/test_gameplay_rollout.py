"""Exercise combined new/existing-card transaction without network or file writes."""
import copy
import importlib.util
import json
from pathlib import Path

spec = importlib.util.spec_from_file_location('writer', 'tools/apply_gameplay_rollout.py')
writer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(writer)
plan = json.loads(Path('catalogue/gameplay-rollout-20261122/combined.patch.json').read_text())
original = {r['id']: copy.deepcopy(r['before']) for r in plan['cards'] if r['before'] is not None}


class Fake:
    def __init__(self, failure=None):
        self.state = copy.deepcopy(original)
        self.writes = []
        self.failure = failure

    def request(self, cid, method='GET', body=None, revision=None):
        current = self.state.get(cid)
        if method == 'GET':
            return copy.deepcopy(current)
        if method == 'DELETE':
            assert revision == current['_rev']
            del self.state[cid]
        else:
            assert body.get('_rev') == (current or {}).get('_rev')
            if len(self.writes) == 2 and self.failure == 'before':
                self.failure = None
                raise TimeoutError('failed before commit')
            self.state[cid] = dict(copy.deepcopy(body), _rev='fixture-' + str(len(self.writes)))
        self.writes.append((cid, method))
        if len(self.writes) == 3 and self.failure in ('lost', 'concurrent'):
            if self.failure == 'concurrent':
                self.state[cid]['concurrent_field'] = 'preserve'
            raise TimeoutError('committed PUT response lost')
        return {}


def same(a, b):
    return {k: writer.content(v) for k, v in a.items()} == {k: writer.content(v) for k, v in b.items()}


db = Fake()
pending = writer.preflight(db, plan)
assert len(pending) == 19 and not db.writes
assert pending[0][1] is None and pending[1][1] is None and all(p[1] is not None for p in pending[2:])
print('PASS read-only preview and wrapper dependency ordering')
assert writer.apply(db, pending) == 19
assert same(db.state, {r['id']: r['after'] for r in plan['cards']})
assert not writer.preflight(db, plan)
print('PASS full apply and idempotence')
drift = Fake()
drift.state[pending[2][0]]['_rev'] = 'concurrent'
try:
    writer.preflight(drift, plan)
    raise RuntimeError('drift not rejected')
except AssertionError:
    assert not drift.writes
print('PASS revision drift rejection')
for scenario in ('before', 'lost', 'concurrent'):
    db = Fake(scenario)
    pending = writer.preflight(db, plan)
    try:
        writer.apply(db, pending)
        raise AssertionError('expected failure')
    except (TimeoutError, RuntimeError):
        pass
    if scenario != 'concurrent':
        assert same(db.state, original)
    else:
        assert db.state[pending[2][0]]['concurrent_field'] == 'preserve'
        assert all(pending[i][0] in db.state for i in (0, 1)), 'preserve referenced wrappers'
    print('PASS rollback ' + scenario)
print('GAMEPLAY_ROLLOUT PASS 6 scenarios; no network/filesystem writes')
