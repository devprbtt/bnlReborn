"""Create the separate Conquest map from a hashed copy; never edit the source map."""
from pathlib import Path
import hashlib,json,shutil,zlib
root=Path(__file__).resolve().parents[1]
source=root/'Maps/map_sr2_beach_base_new.bnlbin'
expected="8386d18d42c035bf5303a4e9f7ad4a6371166a8057ef08c2d1d784c2b447e902"
assert hashlib.sha256(source.read_bytes()).hexdigest()==expected,"Source map changed; review its capture markers before regenerating."
# Snapshot recovered input outside the tracked map collection before transforming it.
copy=root/'extracted/conquest-inputs'/source.name
copy.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,copy)
data=json.loads(zlib.decompress(copy.read_bytes()))
data.update(name='Beach Base Mid Edit - Conquest',description='Capture and hold three zones to earn Block Buster attacks.',map_id='map_sr2_beach_base_new_conquest')
output=root/'Maps/map_sr2_beach_base_new_conquest.bnlbin'
encoded=zlib.compress(json.dumps(data,separators=(',',':'),ensure_ascii=False).encode(),9)
if output.exists():assert output.read_bytes()==encoded,"Existing variant differs; review rather than overwrite."
else:output.write_bytes(encoded)
assert hashlib.sha256(source.read_bytes()).hexdigest()==expected
print(json.dumps({'source_sha256':expected,'output_sha256':hashlib.sha256(encoded).hexdigest(),'map_data_unchanged':True}))
