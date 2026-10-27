"""Upgrade only graph companion + its website paths; preserve game/presence."""
import json
import os
from pathlib import Path
import pwd
import shutil
import subprocess
import sys
import time


def run(*args, **kwargs):
    return subprocess.run(args, check=True, **kwargs)


def main():
    if os.geteuid() != 0:
        raise SystemExit("Run as root")
    source = Path(__file__).resolve().parent
    root = Path('/opt/bnl-discord-graph')
    previous = (root/'current').resolve(strict=True)
    release = root/'releases'/('interactive-'+time.strftime('%Y%m%dT%H%M%SZ',time.gmtime()))
    release.mkdir()
    for name in ['player_graph.py','graph_http.py','test_graph.py','test_graph_http.py','run-bot.py','test_preflight.py']:
        shutil.copyfile(source/name,release/name)
    shutil.copytree(previous/'vendor',release/'vendor',copy_function=os.link)
    env=dict(os.environ,PYTHONPATH=str(release/'vendor'),PYTHONDONTWRITEBYTECODE='1')
    run(sys.executable,'-m','unittest','discover','-s',str(release),'-p','test_*.py',env=env)
    sys.path[:0]=[str(release),str(release/'vendor')]
    import player_graph as g
    config=dict(line.split('=',1) for line in Path('/etc/bnl/discordbot/main.env').read_text().splitlines() if '=' in line)
    api=g.Discord(config['BOT_TOKEN'])
    app=api.request('GET','applications/@me')
    endpoint='https://blocknload.cc/discord/player-graph-interactions'
    if app['id']!=g.BOT or app.get('interactions_endpoint_url') not in (None,'',endpoint):
        raise SystemExit('Unexpected application or existing interaction receiver')
    owner=pwd.getpwnam('bnldiscord')
    settings=Path('/etc/bnl/discordbot/graph.env')
    settings.write_text('DISCORD_VERIFY_KEY='+app['verify_key']+'\n')
    os.chown(settings,0,owner.pw_gid);os.chmod(settings,0o640)
    nginx=Path('/etc/nginx/sites-available/10-bnl-production')
    site=Path('/srv/bnl-reborn-site/player-history')
    backup=release/'rollback';backup.mkdir(mode=0o700)
    shutil.copy2(nginx,backup/'nginx.conf')
    unit=Path('/etc/systemd/system/bnl-discord-graph.service')
    shutil.copy2(unit,backup/'graph.service')
    (backup/'previous-release.txt').write_text(str(previous))
    (backup/'previous-endpoint.json').write_text(json.dumps(app.get('interactions_endpoint_url')))
    if site.exists():shutil.copytree(site,backup/'site')
    site.mkdir(exist_ok=True)
    for name in ['index.html','history.css','history.js']:
        shutil.copyfile(source/'site'/name,site/name);os.chmod(site/name,0o644)
    snippet=Path('/etc/nginx/snippets/bnl-player-history.conf')
    snippet.write_text('''location = /player-history/data {
    limit_except GET { deny all; }
    limit_req zone=bnl_graph_api burst=20 nodelay;
    proxy_pass http://127.0.0.1:8097;
    proxy_connect_timeout 2s;
    proxy_read_timeout 10s;
}
location = /discord/player-graph-interactions {
    limit_except POST { deny all; }
    client_max_body_size 64k;
    access_log off;
    limit_req zone=bnl_graph_api burst=20 nodelay;
    proxy_pass http://127.0.0.1:8097;
    proxy_connect_timeout 2s;
    proxy_read_timeout 10s;
}
''')
    Path('/etc/nginx/conf.d/bnl-player-history-limit.conf').write_text('limit_req_zone $binary_remote_addr zone=bnl_graph_api:1m rate=5r/s;\n')
    text=nginx.read_text();anchor='server_name blocknload.cc;'
    include='include /etc/nginx/snippets/bnl-player-history.conf;'
    if include not in text:
        if text.count(anchor)!=1:raise SystemExit('Unexpected nginx virtual host')
        nginx.write_text(text.replace(anchor,anchor+'\n    '+include))
    run('nginx','-t')
    text=unit.read_text()
    if 'graph.env' not in text:text=text.replace('EnvironmentFile=/etc/bnl/discordbot/main.env','EnvironmentFile=/etc/bnl/discordbot/main.env\nEnvironmentFile=/etc/bnl/discordbot/graph.env')
    unit.write_text(text)
    run('systemctl','stop','bnl-discord-graph')
    db=g.database('/var/lib/bnl-discord-graph/history.sqlite')
    with subprocess.Popen(['journalctl','-u','bnl-discordbot.service','--since','30 days ago','-o','json','--output-fields=MESSAGE,__REALTIME_TIMESTAMP','--no-pager'],stdout=subprocess.PIPE,text=True) as proc:
        minutes=g.import_journal(db,proc.stdout)
        if proc.wait()!=0:raise SystemExit('History export failed')
    db.close()
    link=root/'.interactive-new';link.symlink_to(release);os.replace(link,root/'current')
    run('systemctl','daemon-reload');run('systemctl','start','bnl-discord-graph')
    run('systemctl','reload','nginx')
    # Discord validates signed PING and invalid-signature rejection before accepting.
    for attempt in range(6):
        try:
            result=api.request('PATCH','applications/@me',{'interactions_endpoint_url':endpoint})
            if result.get('interactions_endpoint_url')!=endpoint:raise ValueError('Endpoint not confirmed')
            break
        except Exception:
            if attempt==5:raise
            time.sleep(3)
    print(json.dumps({'release':str(release),'imported_minutes':minutes,'interactive_page':'https://blocknload.cc/player-history/','interaction_receiver':endpoint}))


if __name__=='__main__':main()
