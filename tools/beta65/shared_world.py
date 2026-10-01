"""One authoritative terrain/device/objective world shared by protocol-65 LAN players."""
import copy
import math
import secrets
import struct
import threading
import time
from practice import Practice,health
from loadout import key,pack
from gameclock import millis
from wire_units import set_identity

SHARED=('definitions','blocks','block_damage','placed','next_device','statuses','buff_cache','delayed','mortar_flights','fire_cells','next_projectile','block_teams','block_contact_at','status_owner','status_next','unit_teams','collapse_removals','collapse_seeds','collapse_job','world_revision','phase_end')


class Player(Practice):
    def __getattribute__(self,name):
        if name in SHARED:
            world=object.__getattribute__(self,'__dict__').get('world')
            if world is not None:return world.state[name]
        return super().__getattribute__(name)

    def __setattr__(self,name,value):
        world=self.__dict__.get('world')
        if name in SHARED and world is not None:world.state[name]=value
        else:super().__setattr__(name,value)

    def apply_effect(self,effect,point,origin,source,target=None,owner=None):
        owner=self.unit if owner is None else owner
        previous=self.world.effect_owner;self.world.effect_owner=owner
        try:return super().apply_effect(effect,point,origin,source,target,owner)
        finally:self.world.effect_owner=previous

    def extra_effect(self,effect,point,origin,source,target,owner=None):
        recipient=self.world.players.get(target)
        if recipient and recipient is not self and effect.get('type') in ('heal','add_ammo'):
            return super(Player,recipient).extra_effect(effect,point,origin,source,target,owner)
        if effect.get('type') in ('teleport','teleport_to'):
            caster=self.world.players.get(owner,self)
            if caster is not self:return super(Player,caster).extra_effect(effect,point,origin,source,target,owner)
        return super().extra_effect(effect,point,origin,source,target,owner)

    def damage_entity(self,unit,amount,source,owner=None):
        if self.world.finished or not self.world.started or not math.isfinite(amount) or amount<=0:return
        caster=owner if owner is not None else self.world.effect_owner if self.world.effect_owner is not None else self.unit
        team=self.team(caster)
        if unit in self.world.players:
            victim=self.world.players[unit]
            if victim.player_respawn_at is not None:return
            if caster!=unit and team==self.team(unit):return
            # Enemy damage is disabled during construction; falls/lava/self damage still work.
            if caster!=unit and self.phase_end is not None:return
            victim.cancel_recall();actual=min(amount,victim.player_health);victim.player_health-=actual
            self.send(health(unit,victim.player_health))
            self.send(b'\x06\x44'+pack('I?Iff?',unit,caster>0,max(0,caster),actual,actual,False) if caster>0 else b'\x06\x44'+pack('I?ff?',unit,False,actual,actual,False))
            if victim.player_health==0:
                killer=self.world.player_owner(caster)
                self.send(b'\x06\x43'+(pack('?I',True,killer) if killer else b'\x00')+b'\x00'+pack('I',unit)+source+b'\x00')
                self.send(b'\x06\x0a'+pack('I',unit));self.statuses.pop(unit,None);self.buff_cache.pop(unit,None)
                victim.player_died();victim.deaths+=1
                if killer in self.world.players and killer!=unit:self.world.players[killer].kills+=1
                self.world.send_statistics()
            return
        entry=self.placed.get(unit)
        if not entry or not (entry['definition'].get('health') or {}).get('health'):return
        objective='objective' in entry['definition'].get('labels',[])
        if objective and (team==entry['team'] or self.phase_end is not None or not self.world.objective_exposed(unit)):return
        if not objective and team==entry['team'] and self.world.player_owner(caster)!=entry.get('owner'):return
        actual=min(amount,entry['health']);entry['health']-=actual;self.send(health(unit,entry['health']))
        if objective:self.objective_damage+=actual
        if entry['health']==0:
            final=objective and 'base' in entry['definition'].get('labels',[])
            self.remove_unit(unit,trigger=True)
            if final:self.world.finish(3-entry['team'])

    def remove_unit(self,unit,trigger=False):
        entry=self.placed.get(unit)
        if entry:self.world.owners[unit]=entry.get('owner',self.unit)
        super().remove_unit(unit,trigger)
        for player in self.world.players.values():
            if player is not self:player.send_loadout()

    def move(self,position):
        if self.player_respawn_at is not None or self.world.finished:return
        super().move(position)


class World:
    def __init__(self,rooms,event,profiles=None,social=None,clock=time.monotonic):
        self.lock=social.lock if social else threading.RLock();self.rooms=list(rooms);self.event=event;self.profiles=profiles;self.social=social;self.clock=clock
        self.players={};self.connections={};self.state={};self.owners={};self.loaded=set();self.radar_units=set();self.started=False;self.finished=False
        self.match_id=secrets.token_hex(16);self.created=clock();self.started_at=None;self.last_tick=-100;self.effect_owner=None;self.leader=None
        self.group_id=rooms[0].group['id'] if rooms[0].group else 1
        self.friendly=bool(rooms[0].group and rooms[0].group.get('friendly'))
        for unit,room in enumerate(rooms,1):
            packets=room.practice_packets();packets['unit_id']=unit
            for name in ('hero-create','hero-state'):
                raw=packets[name];packets[name]=raw[:2]+pack('I',unit)+raw[6:]
            # All deployables belong to the player's unit, not profile id or unit 1.
            packets['device-templates']={k:self.owner_packet(v,unit) for k,v in packets.get('device-templates',{}).items()}
            player=Player(packets,lambda p,u=unit:self.send(u,p),event,clock)
            player.target_health=0;player.target_position=(-1000,-1000,-1000);player.deaths=0;player.objective_damage=0
            player.room=room;room.world=self;room.practice=player;self.players[unit]=player
            if not self.state:self.state={name:getattr(player,name) for name in SHARED}
            player.world=self
        self.state['unit_teams']={u:p.room.team for u,p in self.players.items()}|{-1:1,-2:2}
        self.leader=next(iter(self.players.values()))

    @staticmethod
    def owner_packet(packet,owner):
        out=bytearray(packet)
        if len(out)>48 and out[6]&8:
            at=44+(4 if out[6]&16 else 0);struct.pack_into('<I',out,at,owner)
        return bytes(out)

    def player_owner(self,unit):
        if unit in self.players:return unit
        return self.placed_owner(unit)

    def placed_owner(self,unit):return self.state['placed'].get(unit,{}).get('owner',self.owners.get(unit))

    def broadcast(self,packet,exclude=None):
        for unit,send in list(self.connections.items()):
            if unit==exclude:continue
            try:
                out=packet
                if packet[:2]==b'\x06\x08':
                    subject=struct.unpack_from('<I',packet,2)[0]
                    if subject in self.players:
                        out=bytearray(packet);out[43]=subject==unit;out=bytes(out)
                send(out)
            except OSError:
                # Socket thread performs lifecycle cleanup. One closed peer cannot stop everyone.
                pass

    def send(self,origin,packet):
        if packet[0]==6 and packet[1] in (32,33,36,47,56):
            if origin in self.connections:self.connections[origin](packet)
        elif packet[:2]==b'\x06\x07' and packet[2] in (0x30,0x08):
            # Spawn menu and respawn info are per profile; don't replace another player's choices.
            if origin in self.connections:self.connections[origin](packet)
        else:self.broadcast(packet)

    def attach(self,room,send):
        from server import string,varint
        with self.lock:
            player=room.practice
            if player.unit in self.loaded:raise ValueError('Duplicate world ready')
            if self.started or self.finished:raise ValueError('Match already started')
            self.connections[player.unit]=send;self.loaded.add(player.unit)
            if len(self.loaded)!=len(self.players):
                player.send_phase(1);return player
            info=b'\x06\x07\x04'+varint(len(self.players))
            for p in self.players.values():info+=pack('I',p.room.player_id)+b'\xa0'+string(p.room.nickname)+b'\x00'
            self.broadcast(info);self.send_statistics()
            self.started=True;self.started_at=self.clock()
            for p in self.players.values():
                self.broadcast(p.packets['hero-create']);self.broadcast(p.packets['hero-state']);p.send_loadout()
                p.match_started=True;p.chat_send=lambda packet,room=p.room:room.send_region(packet)
                for team in (0,p.room.team):
                    room_id=p.chat_room(team);p.chat_send(b'\x07\x03'+room_id);p.chat_send(b'\x07\x02'+room_id)
            for objective in self.leader.packets['practice'].get('objectives',[]):
                self.leader.spawn_unit(objective['unit_key'],tuple(objective['position'][a] for a in 'xyz'),1 if objective['team']=='team1' else 2,owner=0)
            duration=self.leader.packets['practice'].get('build_seconds',120)
            self.state['phase_end']=self.clock()+duration
            self.leader.send_phase(2,duration);self.broadcast(b'\x06\x4a\x02\x01\x02')
            for p in self.players.values():p.update_spawns()
            self.event('shared_match_started',players=len(self.players),mode='friendly' if self.friendly else 'custom')
            return player

    def tick(self):
        with self.lock:
            now=self.clock()
            if self.finished or now-self.last_tick<.025:return
            self.last_tick=now
            if not self.started:
                if now-self.created>90:self.abort('A player did not finish loading.')
                return
            for unit,p in list(self.players.items()):
                if unit in self.connections:p.tick()
            # Finite matches: a tie produces no rewards; remaining objective HP decides timeout.
            if now-self.started_at>=1800:
                totals={team:sum(d['health'] for d in self.state['placed'].values() if d['team']==team and 'objective' in d['definition'].get('labels',[])) for team in (1,2)}
                self.finish(1 if totals[1]>totals[2] else 2 if totals[2]>totals[1] else 0)

    def handle(self,room,packet):
        from server import read_string,string
        import io
        with self.lock:
            p=room.practice
            if packet[:2]==b'\x07\x07':
                target=packet[2:13]
                if target not in (p.chat_room(0),p.chat_room(room.team)):return True
                reader=io.BytesIO(packet[13:]);message=read_string(reader)
                if reader.read() or not message.strip() or len(message)>512:return True
                out=b'\x07\x08'+target+b'\xc0'+pack('I',room.player_id)+string(room.nickname)+string(message)
                for other in self.rooms:
                    if other.world is self and other.practice and other.practice.unit in self.connections and (target==p.chat_room(0) or other.team==room.team):other.send_region(out)
                return True
            if packet[0]!=6:return False
            if not self.started or self.finished:return True
            fn=packet[1]
            if fn==15:
                if len(packet)!=46 or struct.unpack_from('<I',packet,2)[0]!=p.unit or packet[14:16]!=b'\xff\x80':raise ValueError('Wrong player movement')
                position=struct.unpack_from('<fff',packet,16)
                if not all(math.isfinite(v) for v in position):raise ValueError('Nonfinite movement')
                if p.player_respawn_at is not None:return True
                p.move(position);self.broadcast(b'\x06\x0b'+pack('I',p.unit)+packet[14:],exclude=p.unit);return True
            if fn==32:
                gear=packet[4:];accepted=gear in p.weapons and p.switch(gear)
                self.connections[p.unit](packet[:4]+bytes([0,bool(accepted)]))
                if accepted:self.broadcast(b'\x06\x09'+pack('I',p.unit)+b'\x01\x00\x00'+gear)
                return True
            return p.handle(packet)

    def objective_exposed(self,unit):
        entry=self.state['placed'][unit];labels=entry['definition'].get('labels',[])
        rank=lambda d:0 if 'line_1' in d['definition'].get('labels',[]) else 1 if 'line_2' in d['definition'].get('labels',[]) else 2
        return not any(d['team']==entry['team'] and 'objective' in d['definition'].get('labels',[]) and rank(d)<rank(entry) for d in self.state['placed'].values())

    def send_statistics(self):
        from server import varint
        rows=b''.join(pack('I',p.room.player_id)+b'\xf0'+pack('Biii',p.room.team,p.kills,p.deaths,0) for p in self.players.values())
        self.broadcast(b'\x06\x07\x40\xe0'+varint(len(self.players))+rows+(b'\xf0'+pack('iiii',0,0,0,0))*2)

    def finish(self,winner):
        if self.finished:return
        self.finished=True;self.broadcast(b'\x06\x03'+bytes([winner]))
        participants=[{'id':p.room.player_id,'team':p.room.team} for u,p in self.players.items() if u in self.connections]
        old={p.room.player_id:self.profiles.get(p.room.player_id) for p in self.players.values()} if self.profiles else {}
        if self.profiles:self.profiles.award(self.match_id,participants,winner,'friendly' if self.friendly else 'custom',True)
        from server import varint
        rows=[]
        for entry in self.players.values():
            rows.append(b'\xf8'+pack('I?',entry.room.player_id,False)+b'\xff'+pack('iiiiiiii',0,0,0,0,int(entry.objective_damage),entry.kills,entry.deaths,0)+key('beta_lan_participant')*2)
        results=varint(len(rows))+b''.join(rows)
        for p in self.players.values():
            if p.unit not in self.connections:continue
            before=old.get(p.room.player_id) or {'xp':0,'level':1}
            after=self.profiles.get(p.room.player_id) if self.profiles else before
            xp=lambda v:b'\xe0'+pack('iff',v['level'],v['xp']%1000,1000)
            result=b'\x06\x04\xff\xf0'+pack('f',self.clock()-self.started_at)+results+pack('??',p.room.team==winner,False)+key(p.room.hero)+key(p.room.skin)+xp(before)+xp(after)+pack('ffii',before['xp'],after['xp'],before['xp'],after['xp'])
            try:self.connections[p.unit](result)
            except OSError:pass
        if self.social:self.social.refresh()
        self.event('shared_match_finished',winner=winner,mode='friendly' if self.friendly else 'custom')

    def abort(self,reason):
        if self.finished:return
        self.finished=True
        for room in self.rooms:
            room.state='menu'
            try:room.notice(reason);room.send_region(room.packets['scene'])
            except OSError:pass
            try:
                if room.send_instance:room.send_instance(b'\x09\x01')
            except OSError:pass
        self.event('shared_match_aborted')

    def detach(self,room):
        with self.lock:
            unit=next((u for u,p in self.players.items() if p.room is room),None)
            if unit is None or unit not in self.loaded:return
            self.connections.pop(unit,None);self.loaded.discard(unit)
            if self.finished:return
            if not self.started:self.abort('A player left while the map was loading.');return
            self.broadcast(b'\x06\x0a'+pack('I',unit));self.players[unit].player_respawn_at=float('inf')
            self.players[unit].channel=None;self.players[unit].build_at=None
            if self.connections:self.leader=self.players[next(iter(self.connections))]
            remaining={self.players[u].room.team for u in self.connections}
            if remaining!={1,2}:self.abort('Match ended because a team has no connected players. No XP awarded.')
