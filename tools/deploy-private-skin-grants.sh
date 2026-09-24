#!/usr/bin/env bash
set -Eeuo pipefail

stage=${1:?Usage: deploy-private-skin-grants.sh STAGE GRANTS_SHA256}
grants_sha256=${2:?Usage: deploy-private-skin-grants.sh STAGE GRANTS_SHA256}
candidate=$stage/private_skin_grants.json
live=/opt/bnlreloaded/shared/Configs/private_skin_grants.json
backup=/root/config-backups/bnl-private-skin-grants-$(date -u +%Y%m%dT%H%M%SZ)
changed=0

rollback() {
    local status=$?
    if [[ $changed -eq 1 ]]; then
        if [[ -f $backup/private_skin_grants.json ]]; then
            install -o root -g bnlserver -m 0640 "$backup/private_skin_grants.json" "$live.new"
            mv -f "$live.new" "$live"
        else
            rm -f "$live"
        fi
    fi
    exit "$status"
}
trap rollback EXIT

[[ $(id -u) -eq 0 ]] || { echo 'Run through sudo.' >&2; exit 1; }
[[ $stage == /home/bnladmin/bnl-private-skin-grants-* ]] || {
    echo 'Refusing unexpected staging directory.' >&2
    exit 1
}
[[ -f $candidate && ! -L $candidate ]] || { echo 'Candidate grants file is missing or unsafe.' >&2; exit 1; }
echo "$grants_sha256  $candidate" | sha256sum -c -

python3 - "$candidate" <<'PY'
import json
import re
import sys

allowed = {"skin_hunter_arctic_wolf_private", "skin_boxer_demon_private"}
with open(sys.argv[1], encoding="utf-8") as handle:
    document = json.load(handle)
if not isinstance(document, dict) or not set(document).issubset(allowed):
    raise SystemExit("grant document contains an unknown skin or is not an object")
for skin, owners in document.items():
    if not isinstance(owners, list) or len(owners) != len(set(owners)):
        raise SystemExit(f"{skin} owners must be a unique list")
    if any(not isinstance(owner, str) or re.fullmatch(r"[0-9]{17}", owner) is None for owner in owners):
        raise SystemExit(f"{skin} contains an invalid SteamID64")
print("validated private skin grants: " + ", ".join(f"{skin}={len(owners)}" for skin, owners in document.items()))
PY

systemctl is-active --quiet bnlreloaded.service
pid_before=$(systemctl show bnlreloaded.service -p MainPID --value)
install -d -o root -g root -m 0700 "$backup"
if [[ -f $live ]]; then
    cp -a "$live" "$backup/private_skin_grants.json"
else
    : >"$backup/file-did-not-exist"
fi

install -o root -g bnlserver -m 0640 "$candidate" "$live.new"
mv -f "$live.new" "$live"
changed=1

python3 -m json.tool "$live" >/dev/null
systemctl is-active --quiet bnlreloaded.service
pid_after=$(systemctl show bnlreloaded.service -p MainPID --value)
[[ $pid_after == "$pid_before" ]] || { echo 'Game server PID changed unexpectedly.' >&2; exit 1; }
changed=0
trap - EXIT
printf 'PRIVATE_SKIN_GRANTS=%s\nSERVER_PID=%s\nROLLBACK_SNAPSHOT=%s\n' "$live" "$pid_after" "$backup"
