"""Solo beta match lifecycle and native protocol-65 world feedback."""
import math
import struct
from collections import deque
from gameclock import millis

def pack(fmt,*v):return struct.pack('<'+fmt,*v)

class MatchSystems:
    def init_match(self):
        self.chat_send=self.send;self.match_started=False;self.phase_end=None;self.recall_at=None;self.recall_origin=None
        self.drown_at=None;self.drown_tick=0;self.selected_spawn=0;self.spawn_cache=None
        self.collapse_removals=deque();self.collapse_seeds=set();self.collapse_job=None;self.world_revision=0

    def start_match(self):
        if self.match_started:return
        self.match_started=True
        duration=self.packets['practice'].get('build_seconds',120)
        self.phase_end=self.clock()+duration
        self.send_phase(2,duration);self.send(b"\x06\x4a\x02\x01\x02");self.update_spawns()
        for team in (0,self.team(self.unit)):
            self.chat_send(b'\x07\x03'+self.chat_room(team))
            self.chat_send(b'\x07\x02'+self.chat_room(team))

    def send_phase(self,phase,duration=None):
        body=bytes([0xc0 if duration is None else 0xe0,phase])+pack('q',millis())
        if duration is not None:body+=pack('q',millis()+int(duration*1000))
        self.send(b'\x06\x07\x80'+body)

    def chat_room(self,team):return b'\x01\xe0'+pack('Bii',team,self.world.group_id if self.world else 1,1)

    def chat(self,packet):
        from server import read_string,string
        import io
        room=packet[2:13]
        if room not in (self.chat_room(0),self.chat_room(self.team(self.unit))):return
        r=io.BytesIO(packet[13:]);message=read_string(r)
        if r.read() or not message.strip() or len(message)>512:return
        self.chat_send(b'\x07\x08'+room+b'\xc0'+pack('I',self.packets['practice'].get('player_id',1))+string(self.packets['practice'].get('nickname','BetaLocal'))+string(message))
        self.event('chat_message',channel='all' if room[2]==0 else 'team')

    def spawn_choices(self):
        points={0:self.base_spawn}
        for u,d in self.placed.items():
            if d['definition'].get('spawn_point') is not None and self.team(u)==self.team(self.unit):
                pos=tuple(a+b for a,b in zip(d['position'],(0,1,0)))
                if all(self.point_passable(tuple(a+b for a,b in zip(pos,(0,h,0)))) for h in (0,1)):
                    points[u]=pos
        return points

    def update_spawns(self):
        points=self.spawn_choices()
        if self.selected_spawn not in points:self.selected_spawn=0
        self.spawn_position=points.get(self.selected_spawn,self.base_spawn)
        state=(tuple(points.items()),self.selected_spawn)
        if state==self.spawn_cache:return
        self.spawn_cache=state
        body=bytes([len(points)])
        for u,p in points.items():body+=b'\xf0'+pack('IBfffB',u,self.team(self.unit),*p,1)
        body+=b'\x01'+pack('I?',self.packets['practice'].get('player_id',1),self.selected_spawn is not None)
        if self.selected_spawn is not None:body+=pack('I',self.selected_spawn)
        self.send(b'\x06\x07\x30'+body)

    def respawn_timer(self):
        body=b'\x00' if self.player_respawn_at is None else b'\x01'+pack('IQ',self.packets['practice'].get('player_id',1),millis()+int(max(0,self.player_respawn_at-self.clock())*1000))
        self.send(b'\x06\x07\x08'+body)

    def player_died(self):
        self.cancel_recall();self.drown_at=None
        self.player_respawn_at=self.clock()+self.packets['practice'].get('respawn_seconds',5)
        self.reload_at=None;self.build_at=None;self.channel=None;self.shots.clear()
        self.respawn_timer()

    def cancel_recall(self):
        if self.recall_at is not None:self.send(b'\x06\x4f'+pack('I',self.unit))
        self.recall_at=None

    def match_request(self,packet):
        from practice import Reader
        fn=packet[1];r=Reader(packet[2:])
        if fn==60:
            drowning=r.read('?');r.end()
            if self.player_respawn_at is None:self.drown_at=(self.clock()+5 if self.drown_at is None else self.drown_at) if drowning else None
        elif fn==61:
            unit,height,force=r.read('If?');r.end()
            if unit!=self.unit or not math.isfinite(height) or self.player_respawn_at is not None:return True
            low=self.packets['practice'].get('min_fall_height',5);high=self.packets['practice'].get('max_fall_height',25)
            damage=self.max_health*max(0,min(1,(height-low)/max(1,high-low)))
            self.damage_entity(self.unit,damage,self.current)
            self.event('fall_damage',height=height,damage=damage)
        elif fn==64:
            selected=r.read('I') if r.read('?') else None;r.end()
            if selected is None or selected in self.spawn_choices():self.selected_spawn=selected;self.update_spawns()
        elif fn==77:
            r.end()
            if self.player_respawn_at is None and self.recall_at is None:
                duration=self.packets['practice'].get('recall_seconds',10)
                self.recall_at=self.clock()+duration;self.recall_origin=self.position
                self.send(b'\x06\x4e'+pack('IfQ',self.unit,duration,millis()+int(duration*1000)))
        else:return False
        return True

    def tick_match(self):
        now=self.clock()
        if self.phase_end is not None and now>=self.phase_end:self.phase_end=None;self.send_phase(3);self.send(b'\x06\x4a\x00');self.event('assault_started')
        if self.recall_at is not None:
            if math.dist(self.position,self.recall_origin)>.5:self.cancel_recall()
            elif now>=self.recall_at:
                self.recall_at=None;self.send(b'\x06\x50'+pack('I',self.unit));self.teleport(self.base_spawn);self.event('recall_completed')
        if self.drown_at is not None and now>=self.drown_at and now>=self.drown_tick:
            self.drown_tick=now+1;self.damage_entity(self.unit,self.max_health*.1,self.current)
        if self.match_started or self.spawn_cache is not None:self.update_spawns()
        if not self.world or self.world.leader is self:self.tick_collapse()

    def replaceable(self,index):
        return index is not None and (self.blocks[index]==0 or self.block_cards.get(self.blocks[index],{}).get('replaceable',False))

    def point_passable(self,point):
        cell=tuple(math.floor(v) for v in point);i=self.cell_index(cell)
        if i is None:return False
        return self.blocks[i] in self.passable

    def neighbors(self,c):
        for d in ((1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1)):
            yield tuple(a+b for a,b in zip(c,d))

    def structural(self,c):
        i=self.cell_index(c)
        return i is not None and self.blocks[i]!=0 and self.block_cards.get(self.blocks[i],{}).get('solid',self.blocks[i] not in self.passable)

    def tick_collapse(self):
        # Incremental connectivity search: never destroy an incompletely searched component.
        if self.collapse_removals:
            for _ in range(min(128,len(self.collapse_removals))):
                c=self.collapse_removals.popleft();self.set_block(c,0,vdata=2);self.block_damage.pop(c,None)
            return
        budget=4000
        while budget>0:
            if self.collapse_job is None:
                if not self.collapse_seeds:return
                c=self.collapse_seeds.pop()
                if not self.structural(c):continue
                self.collapse_job=(deque([c]),{c},self.world_revision)
            queue,seen,revision=self.collapse_job
            if revision!=self.world_revision:
                self.collapse_seeds.update(seen);self.collapse_job=None;continue
            if not queue:
                self.collapse_seeds.difference_update(seen);self.collapse_job=None
                self.collapse_removals.extend(seen)
                self.event('structure_collapsed',blocks=len(seen));self.tick_collapse();return
            c=queue.popleft();budget-=1;i=self.cell_index(c);card=self.block_cards.get(self.blocks[i],{})
            if c[1]==0 or card.get('can_stay_in_air') or card.get('destructible') is False:
                self.collapse_seeds.difference_update(seen);self.collapse_job=None;continue
            for n in self.neighbors(c):
                if n not in seen and self.structural(n):seen.add(n);queue.append(n)
