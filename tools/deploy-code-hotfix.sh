#!/usr/bin/env bash
set -Eeuo pipefail

release_id=${1:?Usage: deploy-code-hotfix.sh RELEASE_ID UPLOAD_DIR DLL_SHA256 PDB_SHA256}
upload=${2:?Usage: deploy-code-hotfix.sh RELEASE_ID UPLOAD_DIR DLL_SHA256 PDB_SHA256}
dll_sha256=${3:?Usage: deploy-code-hotfix.sh RELEASE_ID UPLOAD_DIR DLL_SHA256 PDB_SHA256}
pdb_sha256=${4:?Usage: deploy-code-hotfix.sh RELEASE_ID UPLOAD_DIR DLL_SHA256 PDB_SHA256}
root=/opt/bnlreloaded
current=$root/current
release=$root/releases/$release_id
backup=/root/config-backups/bnl-server-hotfix-$(date -u +%Y%m%dT%H%M%SZ)
changed=0

rollback() {
  local status=$?
  if [[ $changed -eq 1 && -f $backup/previous-release ]]; then
    local previous
    previous=$(<"$backup/previous-release")
    ln -sfn "$previous" "$root/.current-hotfix-rollback"
    mv -Tf "$root/.current-hotfix-rollback" "$current"
    systemctl restart bnlreloaded.service || true
  fi
  exit "$status"
}
trap rollback EXIT

[[ $(id -u) -eq 0 ]] || { echo 'Run through sudo.' >&2; exit 1; }
[[ $release_id =~ ^[0-9a-f]{12}$ ]] || { echo 'Invalid release identifier.' >&2; exit 1; }
[[ $upload == /home/bnladmin/bnl-server-hotfix-"$release_id" ]] || {
  echo 'Refusing unexpected upload directory.' >&2
  exit 1
}
[[ -L $current ]] || { echo 'Current release pointer is not a symlink.' >&2; exit 1; }
previous=$(readlink -f "$current")
[[ $previous == "$root"/releases/* ]] || { echo 'Current release target is invalid.' >&2; exit 1; }
[[ -f $upload/BNLReloadedServer.dll && -f $upload/BNLReloadedServer.pdb && -f $upload/REVISION ]] || {
  echo 'Hotfix upload is incomplete.' >&2
  exit 1
}
echo "$dll_sha256  $upload/BNLReloadedServer.dll" | sha256sum -c -
echo "$pdb_sha256  $upload/BNLReloadedServer.pdb" | sha256sum -c -
[[ $(<"$upload/REVISION") == "$release_id"* ]] || { echo 'Revision marker mismatch.' >&2; exit 1; }
systemctl is-active --quiet bnlreloaded.service

install -d -o root -g root -m 0700 "$backup"
printf '%s\n' "$previous" >"$backup/previous-release"
sha256sum "$previous/BNLReloadedServer.dll" >"$backup/previous-dll.sha256"

if [[ -e $release ]]; then
  [[ -d $release ]] || { echo 'Existing release path is not a directory.' >&2; exit 1; }
  echo "$dll_sha256  $release/BNLReloadedServer.dll" | sha256sum -c -
  echo "$pdb_sha256  $release/BNLReloadedServer.pdb" | sha256sum -c -
  cmp -s "$upload/REVISION" "$release/REVISION"
else
  cp -a --reflink=auto "$previous" "$release"
  install -o root -g root -m 0644 "$upload/BNLReloadedServer.dll" "$release/BNLReloadedServer.dll"
  install -o root -g root -m 0644 "$upload/BNLReloadedServer.pdb" "$release/BNLReloadedServer.pdb"
  install -o root -g root -m 0644 "$upload/REVISION" "$release/REVISION"
fi

started_at=$(date --iso-8601=seconds)
ln -sfn "$release" "$root/.current-hotfix"
mv -Tf "$root/.current-hotfix" "$current"
changed=1
systemctl restart bnlreloaded.service

healthy=0
for _ in $(seq 1 45); do
  journal=$(journalctl -q -u bnlreloaded.service --since "$started_at" --no-pager)
  if systemctl is-active --quiet bnlreloaded.service &&
     ss -ltn | awk '{print $4}' | grep -Fx '127.0.0.1:28100' >/dev/null &&
     ss -ltn | awk '{print $4}' | grep -Fx '127.0.0.1:28101' >/dev/null &&
     ss -ltn | awk '{print $4}' | grep -Fx '127.0.0.1:28102' >/dev/null &&
     grep -F 'BNL Reborn signed game-ticket authentication is enabled.' <<<"$journal" >/dev/null &&
     grep -F 'Server running; press Ctrl+C to stop.' <<<"$journal" >/dev/null; then
    healthy=1
    break
  fi
  sleep 1
done
[[ $healthy -eq 1 ]] || { echo 'Game listeners did not become healthy.' >&2; exit 1; }

if journalctl -q -u bnlreloaded.service --since "$started_at" --no-pager -p err..alert | grep -q .; then
  journalctl -q -u bnlreloaded.service --since "$started_at" --no-pager -p err..alert >&2
  exit 1
fi

changed=0
trap - EXIT
printf 'ACTIVE_RELEASE=%s\nROLLBACK_SNAPSHOT=%s\n' "$release" "$backup"
