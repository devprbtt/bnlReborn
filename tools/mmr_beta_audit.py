"""Read-only beta MMR monitoring: python tools/mmr_beta_audit.py DB --days 7."""
import argparse
import collections
import datetime
import json
from pathlib import Path
import sqlite3
import zlib

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('database', type=Path)
parser.add_argument('--days', type=int, default=7)
args = parser.parse_args()
if args.days < 1:
    parser.error('--days must be positive')
now = datetime.datetime.now(datetime.timezone.utc)
start = int(now.timestamp() * 1000) - args.days * 86400000
db = sqlite3.connect(args.database.resolve().as_uri() + '?mode=ro', uri=True)
db.execute('PRAGMA query_only=ON')
db.execute('BEGIN')
db.row_factory = sqlite3.Row
rows = list(db.execute('''SELECT s.*, p.hero FROM MmrSettlements s LEFT JOIN
 (SELECT match_id, player_id, MIN(hero_key) hero FROM MatchPresences
 GROUP BY match_id, player_id HAVING COUNT(DISTINCT hero_key)=1) p
 ON p.match_id=s.MatchId AND p.player_id=s.PlayerId WHERE s.EndedAt>=?''', (start,)))
classes = dict(skill=['kira','hunter','kreepy','ninja','astro'], brawn=['boxer','cogwheel','sarge_stone','abe'], brains=['trondson','engineer','djinn','doc_eliza','magnus'])
heroes = {zlib.crc32(('unit_hero_'+h).encode()): (h, c) for c, hs in classes.items() for h in hs}
groups = collections.defaultdict(list)
for row in rows:
    hero, cls = heroes.get(row['hero'], ('unknown_or_switched', 'unknown_or_switched'))
    groups['all'].append(row)
    groups['class/'+cls].append(row)
    groups['hero/'+hero].append(row)
def summarize(rs):
    ordinary = [r for r in rs if r['Reason']=='outcome']
    return dict(appearances=len(rs), players=len({r['PlayerId'] for r in rs}), matches=len({r['MatchId'] for r in rs}), abandonments=sum(bool(r['Abandoned']) for r in rs), net_mmr=sum((r['NewMean']-r['OldMean'])*100 for r in rs), ordinary_appearances=len(ordinary), nonzero_performance=sum(r['Performance']!=0 for r in ordinary), ordinary_base_mmr=sum(r['Base']*100 for r in ordinary), ordinary_adjustment_mmr=sum((r['NewMean']-r['OldMean']-r['Base'])*100 for r in ordinary), standardized_performance_uplift_mean=sum(12.5*r['Performance']+3.125*abs(r['Performance']) for r in ordinary)/len(ordinary) if ordinary else None)
print(json.dumps(dict(captured_utc=now.isoformat(), since_ms=start, groups={k:summarize(v) for k,v in sorted(groups.items())}, caveats=['Zero performance includes warm-up and exactly neutral scores, not just missing calibration.', 'Ordinary adjustment includes shortage and reconnect rules. Standardized uplift isolates performance on a +/-25 base and a 50/50 outcome mix.', 'No causal hero/class fairness conclusion follows from raw totals. Unknown/switched includes Roly, whose archived class label was not verified.']), indent=2))
db.rollback()
db.close()
