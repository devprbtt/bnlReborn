"""Activate beta86 skins only after both public feeds are live and the server is empty."""
import json,os,subprocess,time,urllib.request,shutil
from pathlib import Path
assert os.geteuid()==0,"Run through sudo"
version="0.2.0-beta.86"
for platform in ["windows-x64","linux-x64"]:
 with urllib.request.urlopen("https://downloads.blocknload.cc/channels/beta/"+platform+"/manifest.json",timeout=30) as response:feed=json.load(response)
 assert feed["version"]==version and any(f["path"]=="Game/assetbundles/custom_skins_beta86" for f in feed["game"]["files"]),platform
with urllib.request.urlopen("http://127.0.0.1:8080/api/activity",timeout=10) as response:activity=json.load(response)
assert activity["online"]==0 and activity["spectating"]==0 and not activity["by_mode"],"Server is occupied; wait before activation"
minimum=Path("/opt/bnlreloaded/current/Configs/minimum_client_version.txt")
old=minimum.read_text().strip();assert int(old.split(".")[-1])<=86
backup=Path("/root/config-backups")/("beta86-activation-"+time.strftime("%Y%m%dT%H%M%SZ",time.gmtime()));backup.mkdir(mode=0o700)
shutil.copy2(minimum,backup/"minimum_client_version.txt")
subprocess.run(["python3",str(Path(__file__).with_name("publish_beta86_skins.py")),"--apply"],check=True)
stat=minimum.stat();temp=minimum.with_name(minimum.name+".beta86-new");temp.write_text(version+"\n");os.chown(temp,stat.st_uid,stat.st_gid);os.chmod(temp,stat.st_mode & 0o777);os.replace(temp,minimum)
subprocess.run(["systemctl","restart","bnlreloaded.service"],check=True)
for _ in range(60):
 time.sleep(1)
 try:
  with urllib.request.urlopen("http://127.0.0.1:8080/api/activity",timeout=2) as response:json.load(response)
  break
 except Exception:pass
else:raise RuntimeError("Service health did not recover; rollback snapshots in "+str(backup))
subprocess.run(["systemctl","is-active","bnlreloaded.service"],check=True)
print(json.dumps(dict(activated=version,minimum_client=minimum.read_text().strip(),backup=str(backup))))
