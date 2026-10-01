"""LAN queue confirmation and shared custom rooms. Shared gameplay starts after every player finishes loading."""
import io
import struct
import threading
from gameclock import millis
from loadout import key


def pack(fmt,*v):return struct.pack('<'+fmt,*v)


class Matchmaking:
    def __init__(self,social):
        self.social=social;self.lock=social.lock;self.queue=[];self.pending={};self.groups={};self.next_id=1

    def queue_update(self,room,state=2,deadline=None,confirmed=0):
        body=bytes([0xe8 if deadline else 0x88,state])
        if deadline:body+=pack('iQ',confirmed,deadline)
        body+=key('beta_menu_friendly')
        room.send_region(b'\x0b\x04\xe0'+body+pack('if',sum(len(g) for g in self.queue),0))

    def cancel(self,room):
        batch=next((b for b in self.queue if room in b),None)
        if batch:self.queue.remove(batch)
        ticket=self.pending.get(room.player_id)
        if ticket:
            batch=ticket['members']
            for m in batch:self.pending.pop(m.player_id,None)
        for m in batch or [room]:
            if m.state in ('queued','confirming'):
                m.state='menu'
                try:self.queue_update(m,1);m.send_region(b'\x0b\x05'+pack('I',room.player_id))
                except OSError:pass

    def tick(self):
        with self.lock:
            for ticket in list(self.pending.values()):
                if millis()>=ticket['deadline'] and ticket['members'][0].player_id in self.pending:self.cancel(ticket['members'][0])

    def group(self,room):return next((g for g in self.groups.values() if room in g['members']),None)

    def broadcast_room(self,g):
        for m in g['members']:
            try:
                if m.state=='lobby':
                    if m.send_instance:m.send_instance(m.update())
                elif m.state=='room':m.send_region(m.room_update())
            except OSError:pass

    def leave(self,room):
        with self.lock:
            self.cancel(room);g=self.group(room)
            if room.world:
                room.world.detach(room);room.world=None;room.practice=None
            if not g:return
            g['members'].remove(room);room.group=None;room.state='menu'
            if not g['members']:self.groups.pop(g['id']);return
            if g['owner'] is room:g['owner']=g['members'][0]
            self.broadcast_room(g)

    def create_group(self,members,name,password='',friendly=False):
        g={'id':self.next_id,'members':members,'owner':members[0],'friendly':friendly};self.next_id+=1;self.groups[g['id']]=g
        for i,m in enumerate(members):
            m.group=g;m.name=name;m.password=password;m.state='room';m.team=1 if i<len(members)/2 else 2
        return g

    def handle(self,room,packet):
        from server import read_string,string,varint
        if packet[0]!=11:return False
        fn=packet[1]
        with self.lock:
            self.tick();g=self.group(room)
            if fn==0:
                if room.state!='menu' or packet[2:]!=key('beta_menu_friendly'):return True
                owner=self.social.squad_owner(room.player_id)
                if owner is not None and owner!=room.player_id:return True
                batch=[self.social.online[p] for p in self.social.squads.get(owner,[room.player_id])]
                if any(m.state!='menu' for m in batch):return True
                for member in batch:
                    if self.group(member):self.leave(member)
                self.queue.append(batch)
                for m in batch:m.state='queued';self.queue_update(m)
                # LAN starts at equal-sized opposing parties (1v1 minimum); never splits a squad.
                pair=next(((a,b) for i,a in enumerate(self.queue) for b in self.queue[i+1:] if len(a)==len(b)),None)
                if pair:
                    for b in pair:self.queue.remove(b)
                    members=pair[0]+pair[1];ticket={'members':members,'deadline':millis()+30000,'confirmed':set()}
                    for m in members:self.pending[m.player_id]=ticket;m.state='confirming';self.queue_update(m,3,ticket['deadline'])
            elif fn==1:self.cancel(room)
            elif fn==3:
                ticket=self.pending.get(room.player_id)
                if not ticket:return True
                if len(packet)!=3 or packet[2]!=1:self.cancel(room);return True
                ticket['confirmed'].add(room.player_id)
                for m in ticket['members']:self.queue_update(m,3,ticket['deadline'],len(ticket['confirmed']))
                if len(ticket['confirmed'])==len(ticket['members']):
                    members=ticket['members']
                    for m in members:self.pending.pop(m.player_id,None);self.queue_update(m,1)
                    g=self.create_group(members,'LAN Friendly',friendly=True)
                    for m in members:
                        if m.maps:m.map_id=next(iter(m.maps.values()))['id']
                        m.open_lobby()
            elif fn==6:
                rows=[]
                for group in self.groups.values():
                    owner=group['owner']
                    if group['friendly']:continue
                    rows.append(b'\xff\xf0'+pack('Q',group['id'])+string(owner.name)+string(owner.nickname)+pack('ii?',len(group['members']),10,bool(owner.password))+key(owner.map_id)+pack('ff??B',owner.maps.get(key(owner.map_id),{}).get('build_seconds',120),1,False,False,1 if owner.state=='room' else 2))
                room.send_region(packet[:4]+b'\x00'+varint(len(rows))+b''.join(rows))
            elif fn==9:
                if room.state!='menu':return True
                r=io.BytesIO(packet[2:]);name,password=read_string(r),read_string(r)
                if r.read() or not name.strip() or len(name)>80 or len(password)>80:raise ValueError('Invalid room')
                if g:self.leave(room)
                g=self.create_group([room],name,password);room.team=1;self.broadcast_room(g)
            elif fn==7:
                r=io.BytesIO(packet[4:]);raw=r.read(8)
                if len(raw)!=8:raise ValueError('Missing room id')
                gid=struct.unpack('<Q',raw)[0];password=read_string(r)
                if r.read():raise ValueError('Trailing room join')
                target=self.groups.get(gid);result=5
                if target:
                    owner=target['owner']
                    result=4 if owner.state!='room' or room.state!='menu' else 2 if password!=owner.password else 3 if len(target['members'])>=10 else 1
                room.send_region(packet[:4]+b'\x00'+bytes([result]))
                if result==1:
                    room.group=target;room.name=owner.name;room.password=owner.password;room.map_id=owner.map_id;room.state='room'
                    room.team=1 if sum(m.team==1 for m in target['members'])<=sum(m.team==2 for m in target['members']) else 2
                    target['members'].append(room);self.broadcast_room(target)
            elif fn==11:
                self.leave(room);room.send_region(b'\x0b\x11')
            elif fn==10:
                if g and g['owner'] is room and room.state=='room':
                    if len(g['members'])>1 and (room.map_id=='beta_practice_map' or {m.team for m in g['members']}!={1,2}):
                        room.notice('Select a two-team map and put at least one player on each side.');return True
                    for m in g['members']:m.open_lobby()
            elif fn==13:
                if g and room.state=='room':
                    if room.map_id=='beta_practice_map':room.notice('Choose a two-team map before switching sides.')
                    elif sum(m.team==3-room.team for m in g['members'])<5:room.team=3-room.team;self.broadcast_room(g)
            elif fn==12:
                if g and g['owner'] is room and room.state=='room' and len(packet)==7 and packet[2]==0x80:
                    requested=packet[3:]
                    if requested==key('beta_practice_map') or requested in room.maps:
                        for m in g['members']:
                            m.map_id='beta_practice_map' if requested==key('beta_practice_map') else room.maps[requested]['id']
                            if m.map_id=='beta_practice_map':m.team=1
                    self.broadcast_room(g)
                elif g:room.notice('Only the host can change the map. Other match settings are not yet configurable.')
            elif fn==14 and len(packet)==6:
                if g and g['owner'] is room:
                    victim=next((m for m in g['members'] if m.player_id==struct.unpack_from('<I',packet,2)[0]),None)
                    if victim and victim is not room:self.leave(victim);victim.send_region(b'\x0b\x11')
            elif fn in (2,18):pass
            else:return False
        return True
