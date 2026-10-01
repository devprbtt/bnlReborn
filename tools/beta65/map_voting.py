"""Server-owned two-map Friendly ballot using the beta's native lobby UI."""
import secrets
from gameclock import millis

VOTE_MILLISECONDS = 30000
SELECTION_MILLISECONDS = 120000


def begin(group):
    members = group['members']
    common = set(m['id'] for m in members[0].maps.values())
    for member in members[1:]:
        common.intersection_update(m['id'] for m in member.maps.values())
    common.discard('beta_practice_map')
    if not common:
        raise ValueError('Friendly requires a common two-team map')
    candidates = secrets.SystemRandom().sample(sorted(common), min(2, len(common)))
    now = millis()
    group['map_vote'] = {'candidates': candidates, 'votes': {}, 'start': now,
                         'end': now + VOTE_MILLISECONDS, 'winner': None}
    for member in members:
        member.map_id = candidates[0]
    if len(candidates) == 1:
        finish(group)


def active(group):
    return bool(group and group.get('map_vote') and group['map_vote']['winner'] is None)


def broadcast(group):
    for member in group['members']:
        if member.state == 'lobby' and member.send_instance:
            try: member.send_instance(member.update())
            except OSError: pass


def finish(group):
    if not active(group): return
    ballot = group['map_vote']
    present = {m.player_id for m in group['members']}
    ballot['votes'] = {p:m for p,m in ballot['votes'].items() if p in present}
    counts = {m:sum(v == m for v in ballot['votes'].values()) for m in ballot['candidates']}
    highest = max(counts.values())
    winner = secrets.choice([m for m,n in counts.items() if n == highest])
    ballot['winner'] = winner
    now = millis()
    for member in group['members']:
        member.map_id = winner
        member.ready = False
        member.selection_start = now
        member.selection_end = now + SELECTION_MILLISECONDS
    group['members'][0].event('lobby_map_vote_finished', winner=winner, counts=counts)
    broadcast(group)


def tick(group):
    if active(group) and millis() >= group['map_vote']['end']:
        finish(group)


def vote(room, map_key):
    from loadout import key
    group = room.group
    if not active(group) or not group.get('friendly') or room not in group['members']:
        return False
    tick(group)
    if not active(group): return False
    ballot = group['map_vote']
    selected = next((m for m in ballot['candidates'] if key(m) == map_key), None)
    # Native UI locks voting after one vote. Replays or changes cannot add votes.
    if selected is None or room.player_id in ballot['votes']: return False
    ballot['votes'][room.player_id] = selected
    room.event('lobby_map_vote', player=room.player_id, map=selected)
    if len(ballot['votes']) == len(group['members']): finish(group)
    else: broadcast(group)
    return True
