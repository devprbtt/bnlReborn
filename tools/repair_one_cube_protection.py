"""Reproduce/verify the four beta100 map repairs against immutable Git inputs.

Run from the server repository: python tools/repair_one_cube_protection.py [--check]
Recovered inputs are copied and hashed under ignored extracted/ before use.
"""
import argparse
import base64
from collections import deque
import hashlib
import json
from pathlib import Path
import subprocess
import zlib

BASE = 'ed896f6bd255c3ce4a5e9e700135b487ab335f0d'
MAPS = {
    'aztec_temple': ('773948431cb096a04880900fb7faa708191f44bee2efddf087dd899e6e8d2b25', 4, 108, 200),
    'citadel': ('a5e868fccc50984dc38b5a18e86deea17e49d2b094a862a31bef1cb7c2f49afd', 2, 1102, 532),
    'insandity': ('e5d4cd0fd925833103224f238d23015728f918398108a747dcdc1a3e84f5762d', 2, 276, 232),
    'parapet': ('f52e39c4f892e05ee75d80a54ba7d9015d7333b2d69940b5ba392d94136f7d50', 2, 90, 6),
}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def repair(source, rooms, volume, expected_air):
    doc = json.loads(zlib.decompress(source))
    data = doc['map']
    size = tuple(data['size'][axis] for axis in 'xyz')
    blocks = bytearray(zlib.decompress(base64.b64decode(data['blocks_data'])))
    # These recovered maps use the original four-byte cell representation.
    assert len(blocks) == size[0] * size[1] * size[2] * 4
    original = bytes(blocks)

    def offset(pos):
        x, y, z = pos
        return ((x * size[1] + y) * size[2] + z) * 4

    def block(pos):
        return blocks[offset(pos)]

    def inside(pos):
        return all(0 <= pos[a] < size[a] for a in range(3))

    spawns = data['spawn_points']
    assert len(spawns) == rooms
    seen_rooms = []
    locked = set()
    for spawn in spawns:
        seed = tuple(int(spawn['position'][axis]) for axis in 'xyz')
        assert block(seed) in (0, 59)
        visited = {seed}
        queue = deque([seed])
        while queue:
            pos = queue.popleft()
            for axis in range(3):
                for step in (-1, 1):
                    neighbor = list(pos)
                    neighbor[axis] += step
                    neighbor = tuple(neighbor)
                    assert inside(neighbor), 'Spawn room leaks to map boundary'
                    if neighbor in visited or block(neighbor) not in (0, 59):
                        continue
                    visited.add(neighbor)
                    assert len(visited) <= volume, 'Spawn room leaks into playable map'
                    queue.append(neighbor)
        assert len(visited) == volume
        assert not any(visited & room for room in seen_rooms)
        seen_rooms.append(visited)
        locked.update(pos for pos in visited if block(pos) == 0)
    assert len(locked) == expected_air
    # The existing room walls, gates and locked cells define the boundary.
    # Only air becomes the native non-solid, non-replaceable no-build block.
    for pos in locked:
        at = offset(pos)
        blocks[at:at + 4] = bytes((59, 0, 0, 0))
    metal = set()
    cubes = [u for u in data['units'] if u['unit_key'] == 'unit_base_shield_rush']
    assert len(cubes) == 2 and {u['team'] for u in cubes} == {'team1', 'team2'}
    for cube in cubes:
        x, y, z = (int(cube['position'][axis]) for axis in 'xyz')
        for xx in range(x, x + 4):
            for zz in range(z, z + 4):
                pos = (xx, y - 1, zz)
                assert inside(pos)
                if block(pos) != 10:
                    assert block(pos) in (0, 2), 'Unexpected cube support material'
                    metal.add(pos)
                    at = offset(pos)
                    blocks[at:at + 4] = bytes((10, 0, 0, 0))
    # Independent full-array check: no unrelated cells or cell flags change.
    changed = {i for i in range(0, len(blocks), 4) if blocks[i:i+4] != original[i:i+4]}
    assert changed == {offset(pos) for pos in locked | metal}
    assert all(block(pos) == 59 for room in seen_rooms for pos in room)
    assert all(block(pos) == 10 for pos in metal)
    data['blocks_data'] = base64.b64encode(zlib.compress(blocks, 9)).decode('ascii')
    output = zlib.compress(json.dumps(doc, separators=(',', ':'), ensure_ascii=False).encode(), 9)
    decoded = json.loads(zlib.decompress(output))
    before = json.loads(zlib.decompress(source))
    decoded['map']['blocks_data'] = before['map']['blocks_data']
    assert decoded == before, 'Non-terrain metadata changed'
    return output, {'spawn_rooms': rooms, 'room_volume_each': volume,
                    'air_to_no_build': len(locked), 'missing_metal_added': len(metal)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    root = Path.cwd()
    assert (root / 'BNLReloadedServer').is_dir(), 'Run from the server repository'
    snapshots = root / 'extracted/one-cube-protection-inputs'
    snapshots.mkdir(parents=True, exist_ok=True)
    report = {'baseline_revision': BASE, 'maps': [], 'live_modified': False}
    provenance = []
    for name, (expected, rooms, volume, air) in MAPS.items():
        key = 'map_sr2_' + name
        relative = 'Maps/' + key + '.bnlbin'
        source = subprocess.run(['git', 'show', BASE + ':' + relative],
                                check=True, capture_output=True).stdout
        assert sha(source) == expected
        snapshot = snapshots / (key + '.bnlbin')
        snapshot.write_bytes(source)
        provenance.append({'source_path': str(root / relative), 'git_revision': BASE,
                           'sha256': expected, 'copy': str(snapshot)})
    (snapshots / 'provenance.json').write_text(json.dumps(provenance, indent=2) + '\n')
    for item, (name, (_, rooms, volume, air)) in zip(provenance, MAPS.items()):
        source = Path(item['copy']).read_bytes()
        assert sha(source) == item['sha256']
        output, result = repair(source, rooms, volume, air)
        assert result['missing_metal_added'] == (16 if name == 'insandity' else 0)
        target = Path(item['source_path'])
        if args.check:
            assert target.read_bytes() == output, str(target) + ' differs from reproducible repair'
        else:
            assert target.read_bytes() in (source, output), 'Refusing to overwrite unrelated map edits'
            target.write_bytes(output)
        result.update(map_id='map_sr2_' + name, before_sha256=sha(source), after_sha256=sha(output))
        report['maps'].append(result)
    destination = root / 'docs/one-cube-protection-validation.json'
    encoded = json.dumps(report, indent=2) + '\n'
    if args.check:
        assert destination.read_text() == encoded
    else:
        destination.write_text(encoded)
    print(encoded)


if __name__ == '__main__':
    main()
