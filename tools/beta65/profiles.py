"""Private LAN accounts and protocol-65 social/profile state. No production accounts."""
import hashlib
import hmac
import io
import re
import secrets
import sqlite3
import struct
import threading
import time
from pathlib import Path
from loadout import key


def pack(fmt,*v):return struct.pack('<'+fmt,*v)
def progression(level):return b'\xe0'+pack('i',level)+b'\x00\x00'
def league():return b'\xfc'+pack('iiiiii',-1,-1,0,10,0,0)


class Profiles:
    def __init__(self,path=':memory:'):
        if str(path)!=':memory:':Path(path).parent.mkdir(parents=True,exist_ok=True)
        self.lock=threading.RLock()
        self.db=sqlite3.connect(str(path),check_same_thread=False)
        self.db.row_factory=sqlite3.Row
        self.db.executescript('''
        PRAGMA foreign_keys=ON;
        CREATE TABLE IF NOT EXISTS players(id INTEGER PRIMARY KEY,name TEXT NOT NULL UNIQUE COLLATE NOCASE,salt BLOB NOT NULL,password BLOB NOT NULL,xp INTEGER NOT NULL DEFAULT 0,looking INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS friends(a INTEGER REFERENCES players(id),b INTEGER REFERENCES players(id),PRIMARY KEY(a,b));
        CREATE TABLE IF NOT EXISTS requests(a INTEGER REFERENCES players(id),b INTEGER REFERENCES players(id),PRIMARY KEY(a,b));
        CREATE TABLE IF NOT EXISTS rewards(match_id TEXT,player INTEGER REFERENCES players(id),xp INTEGER NOT NULL,PRIMARY KEY(match_id,player));
        ''')

    def login(self,name,password):
        if not re.fullmatch(r'[A-Za-z0-9_ -]{3,24}',name) or name.strip()!=name or not 8<=len(password)<=128:return None
        with self.lock,self.db:
            row=self.db.execute('SELECT * FROM players WHERE name=?',(name,)).fetchone()
            salt=row['salt'] if row else secrets.token_bytes(16)
            digest=hashlib.scrypt(password.encode(),salt=salt,n=16384,r=8,p=1)
            if row:
                return self.get(row['id']) if hmac.compare_digest(row['password'],digest) else None
            cursor=self.db.execute('INSERT INTO players(name,salt,password) VALUES(?,?,?)',(name,salt,digest))
            return self.get(cursor.lastrowid)

    def get(self,pid):
        with self.lock:
            r=self.db.execute('SELECT id,name,xp,looking FROM players WHERE id=?',(pid,)).fetchone()
            return None if r is None else dict(r,level=1+r['xp']//1000)

    def search(self,pattern):
        with self.lock:
            # Literal substring search; wildcard characters have no special meaning.
            return [dict(r) for r in self.db.execute('SELECT id,name FROM players WHERE instr(lower(name),lower(?))>0 ORDER BY name LIMIT 30',(pattern[:24],))]

    def social(self,pid):
        with self.lock:
            return tuple([dict(r) for r in self.db.execute(sql,(pid,))] for sql in (
                'SELECT p.id,p.name FROM friends f JOIN players p ON p.id=f.b WHERE f.a=? ORDER BY p.id',
                'SELECT p.id,p.name FROM requests r JOIN players p ON p.id=r.a WHERE r.b=? ORDER BY p.id',
                'SELECT p.id,p.name FROM requests r JOIN players p ON p.id=r.b WHERE r.a=? ORDER BY p.id'))

    def friend(self,pid,other,action,accept=False):
        with self.lock,self.db:
            if pid==other or not self.get(other):return
            if action=='request':
                if not self.db.execute('SELECT 1 FROM friends WHERE a=? AND b=?',(pid,other)).fetchone():self.db.execute('INSERT OR IGNORE INTO requests VALUES(?,?)',(pid,other))
            elif action=='accept':
                requested=self.db.execute('DELETE FROM requests WHERE a=? AND b=?',(other,pid)).rowcount
                if requested and accept:
                    self.db.executemany('INSERT OR IGNORE INTO friends VALUES(?,?)',[(pid,other),(other,pid)])
                    self.db.execute('DELETE FROM requests WHERE a=? AND b=?',(pid,other))
            elif action=='remove':
                self.db.executemany('DELETE FROM friends WHERE a=? AND b=?',[(pid,other),(other,pid)])
                self.db.executemany('DELETE FROM requests WHERE a=? AND b=?',[(pid,other),(other,pid)])

    def award(self,match_id,participants,winner,mode,completed):
        """Server-only idempotent rewards. Never call from a client XP/result message.
        Local recovery rule: 500 XP completed Friendly match, +250 for a win.
        Custom/practice/aborted or one-sided matches never award progression.
        """
        if mode!='friendly' or not completed or winner not in (1,2) or {p['team'] for p in participants}!={1,2}:return []
        if len({p['id'] for p in participants})!=len(participants):raise ValueError('Duplicate match participant')
        changed=[]
        with self.lock,self.db:
            for p in participants:
                xp=500+250*(p['team']==winner)
                if self.db.execute('INSERT OR IGNORE INTO rewards VALUES(?,?,?)',(match_id,p['id'],xp)).rowcount:
                    self.db.execute('UPDATE players SET xp=xp+? WHERE id=?',(xp,p['id']));changed.append(p['id'])
        return changed


class Social:
    def __init__(self,profiles):
        self.profiles=profiles;self.online={};self.squads={};self.invites={};self.lock=threading.RLock()

    def player_packet(self,pid):
        from server import string,varint
        p=self.profiles.get(pid);friends,incoming,outgoing=self.profiles.social(pid)
        info=lambda r:b'\xb8'+pack('I',r['id'])+string(r['name'])+bytes([r['id'] in self.online])+string('LAN')
        request=lambda r:b'\xc0'+pack('I',r['id'])+string(r['name'])
        lists=b''.join(varint(len(rows))+b''.join(encode(r) for r in rows) for rows,encode in [(friends,info),(incoming,request),(outgoing,request)])
        return b'\x05\x05\xfc\x01\x80'+string(p['name'])+league()+progression(p['level'])+lists+pack('i?',p['xp'],p['looking'])

    def profile_packet(self,pid):
        from server import string
        p=self.profiles.get(pid)
        if not p:return None
        # Full ProfileData with empty history/hero stats until genuine match completion.
        return b'\xbf\xe0'+string(p['name'])+league()+progression(p['level'])+pack('i',p['xp'])+b'\x00\x00\xe0'+pack('III',0,0,0)+b'\x00'+pack('?i',p['looking'],len(self.profiles.social(pid)[0]))

    def refresh(self):
        for pid,room in list(self.online.items()):
            try:room.send_region(self.player_packet(pid))
            except OSError:pass

    def connect(self,room):
        with self.lock:
            if room.player_id in self.online:raise ValueError('Profile already connected')
            self.online[room.player_id]=room;self.refresh()

    def disconnect(self,room):
        with self.lock:
            if self.online.get(room.player_id) is not room:return
            self.leave_squad(room.player_id)
            self.online.pop(room.player_id,None)
            self.invites={k:v for k,v in self.invites.items() if room.player_id not in k}
            self.refresh()

    def squad_owner(self,pid):return next((owner for owner,members in self.squads.items() if pid in members),None)

    def update_squad(self,owner):
        from server import string,varint
        members=self.squads.get(owner,[]);body=varint(len(members))
        for pid in members:
            p=self.profiles.get(pid)
            body+=b'\xdf\x80'+pack('I?',pid,pid==owner)+string(p['name'])+pack('ii',p['level'],p['xp'])+b'\x00\x00'+pack('Q',0)
        for pid in members:
            if pid in self.online:self.online[pid].send_region(b'\x05\x0b'+body)

    def leave_squad(self,pid):
        owner=self.squad_owner(pid)
        if owner is None:return
        members=self.squads.pop(owner);members.remove(pid)
        if pid in self.online:self.online[pid].send_region(b'\x05\x0d')
        self.invites={k:v for k,v in self.invites.items() if k[0]!=owner}
        if members:
            owner=members[0];self.squads[owner]=members;self.update_squad(owner)

    def handle(self,room,packet):
        from server import read_string,string,varint
        if packet[0]!=5:return False
        fn=packet[1];pid=room.player_id;reply=lambda b:room.send_region(packet[:4]+b'\x00'+b)
        with self.lock:
            if fn==23:
                r=io.BytesIO(packet[4:]);pattern=read_string(r)
                if r.read():raise ValueError('Search trailing data')
                rows=self.profiles.search(pattern);reply(varint(len(rows))+b''.join(b'\xa0'+pack('I',p['id'])+string(p['name']) for p in rows))
            elif fn in (24,25,26):
                if len(packet)!=(7 if fn==25 else 6):raise ValueError('Friend request size')
                self.profiles.friend(pid,struct.unpack_from('<I',packet,2)[0],{24:'request',25:'accept',26:'remove'}[fn],fn==25 and packet[6]==1);self.refresh()
            elif fn==27 and len(packet)==5:
                with self.profiles.lock,self.profiles.db:self.profiles.db.execute('UPDATE players SET looking=? WHERE id=?',(packet[4]==1,pid))
                reply(b'');self.refresh()
            elif fn==31 and len(packet)==8:
                body=self.profile_packet(struct.unpack_from('<I',packet,4)[0])
                if body is None:room.send_region(packet[:4]+b'\xff'+string('No such LAN profile.'))
                else:reply(body)
            elif fn==9:
                if room.state!='menu' or packet[4:]!=key('beta_menu_friendly'):room.send_region(packet[:4]+b'\xff'+string('Return to the menu and select Friendly.'));return True
                owner=self.squad_owner(pid)
                if owner is None:self.squads[pid]=[pid];owner=pid
                reply(b'');self.update_squad(owner)
            elif fn==12:
                self.leave_squad(pid);reply(b'')
            elif fn==15 and len(packet)==6:
                other=struct.unpack_from('<I',packet,2)[0];owner=self.squad_owner(pid)
                if owner is None:self.squads[pid]=[pid];owner=pid
                target=self.online.get(other)
                if owner!=pid or not target or target.state!='menu' or room.state!='menu' or self.squad_owner(other) is not None or len(self.squads[owner])>=5:return True
                self.invites[(owner,other)]=time.monotonic()+60
                target.send_region(b'\x05\x10\x01\x80'+pack('Q',owner)+b'\xc0\x01'+string(str(pid))+string(room.nickname))
                self.update_squad(owner)
            elif fn==10:
                if len(packet)!=14 or packet[4:6]!=b'\x01\x80':raise ValueError('Invalid squad alias')
                owner=struct.unpack_from('<Q',packet,6)[0];expiry=self.invites.pop((owner,pid),0)
                if expiry<time.monotonic() or owner not in self.squads or self.squad_owner(pid) is not None or room.state!='menu' or self.online[owner].state!='menu' or len(self.squads[owner])>=5:
                    room.send_region(packet[:4]+b'\xff'+string('Invitation expired or squad unavailable.'));return True
                self.squads[owner].append(pid);reply(b'');self.update_squad(owner)
            elif fn==21:
                if packet[2:]==key('beta_menu_friendly'):room.send_region(b'\x05\x16'+packet[2:])
            elif fn==19 and len(packet)==6:
                other=struct.unpack_from('<I',packet,2)[0]
                if self.squad_owner(pid)==pid and self.squad_owner(other)==pid and other!=pid:self.leave_squad(other)
            elif fn in (14,18):pass
            else:return False
        return True
