#!/usr/bin/env bash
set -Eeuo pipefail
# Managed code plus the whole ControlPanel folder; preserves deployed data and dependencies.
# Same guard rails as deploy-code-hotfix.sh, plus the panel archive and a check that production did
# not move since the release was staged. Any failure, including the health gate, restores the previous
# release (EXIT trap: the gate's explicit `exit 1` does not fire an ERR trap).

usage='Usage: deploy-server-panel-release.sh RELEASE_ID UPLOAD_DIR DLL_SHA256 PDB_SHA256 PANEL_TAR_SHA256 BASE_RELEASE'
release_id=${1:?$usage}
upload=${2:?$usage}
dll_sha256=${3:?$usage}
pdb_sha256=${4:?$usage}
panel_sha256=${5:?$usage}
base_release=${6:?$usage}
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
[[ $base_release =~ ^[0-9a-f]{12}$ && $previous == "$root/releases/$base_release" ]] || {
  echo "Production changed since staging (live is $previous)." >&2
  exit 1
}
[[ -f $upload/BNLReloadedServer.dll && -f $upload/BNLReloadedServer.pdb && -f $upload/REVISION && -f $upload/panel.tar ]] || {
  echo 'Release upload is incomplete.' >&2
  exit 1
}
echo "$dll_sha256  $upload/BNLReloadedServer.dll" | sha256sum -c -
echo "$pdb_sha256  $upload/BNLReloadedServer.pdb" | sha256sum -c -
echo "$panel_sha256  $upload/panel.tar" | sha256sum -c -
[[ $(<"$upload/REVISION") == "$release_id"* ]] || { echo 'Revision marker mismatch.' >&2; exit 1; }
# The archive must hold exactly one top-level ControlPanel folder and nothing that escapes it.
if tar -tf "$upload/panel.tar" | grep -Ev '^ControlPanel(/|$)' | grep -q .; then
  echo 'Panel archive contains paths outside ControlPanel/.' >&2
  exit 1
fi
tar -tf "$upload/panel.tar" | grep -Fxq 'ControlPanel/index.html' || { echo 'Panel archive has no index.html.' >&2; exit 1; }
systemctl is-active --quiet bnlreloaded.service

install -d -o root -g root -m 0700 "$backup"
printf '%s\n' "$previous" >"$backup/previous-release"
sha256sum "$previous/BNLReloadedServer.dll" >"$backup/previous-dll.sha256"

if [[ -e $release ]]; then
  [[ -d $release ]] || { echo 'Existing release path is not a directory.' >&2; exit 1; }
  echo "$dll_sha256  $release/BNLReloadedServer.dll" | sha256sum -c -
  echo "$pdb_sha256  $release/BNLReloadedServer.pdb" | sha256sum -c -
  [[ $(<"$release/PANEL_SHA256") == "$panel_sha256" ]] || { echo 'Existing release has a different panel.' >&2; exit 1; }
  cmp -s "$upload/REVISION" "$release/REVISION"
else
  staging=$(mktemp -d "$root/.panel-staging-XXXXXX")
  tar -xf "$upload/panel.tar" -C "$staging" --no-same-owner --no-same-permissions
  chown -R root:root "$staging/ControlPanel"
  find "$staging/ControlPanel" -type d -exec chmod 0755 {} +
  find "$staging/ControlPanel" -type f -exec chmod 0644 {} +
  cp -a --reflink=auto "$previous" "$release"
  install -o root -g root -m 0644 "$upload/BNLReloadedServer.dll" "$release/BNLReloadedServer.dll"
  install -o root -g root -m 0644 "$upload/BNLReloadedServer.pdb" "$release/BNLReloadedServer.pdb"
  install -o root -g root -m 0644 "$upload/REVISION" "$release/REVISION"
  rm -rf "$release/ControlPanel"
  mv "$staging/ControlPanel" "$release/ControlPanel"
  rmdir "$staging"
  printf '%s\n' "$panel_sha256" >"$release/PANEL_SHA256"
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
