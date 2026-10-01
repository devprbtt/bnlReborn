"""Single-player practice simulation for protocol 65; not multiplayer or historical balance."""
import io
import math
import struct
import time
import zlib
from loadout import LoadoutSystems
from match_systems import MatchSystems


def pack(fmt, *values):
    return struct.pack('<' + fmt, *values)


class Reader:
    def __init__(self, data): self.data = io.BytesIO(data)
    def read(self, fmt):
        fmt = '<' + fmt
        data = self.data.read(struct.calcsize(fmt))
        if len(data) != struct.calcsize(fmt): raise ValueError('Truncated gameplay packet')
        values = struct.unpack(fmt, data)
        return values[0] if len(values) == 1 else values
    def size(self):
        n = 0
        for shift in range(0, 35, 7):
            b = self.read('B'); n |= (b & 127) << shift
            if b < 128:
                if n > 256: raise ValueError('Oversized gameplay collection')
                return n
        raise ValueError('Invalid count')
    def vector(self):
        v = self.read('fff')
        if not all(math.isfinite(x) for x in v): raise ValueError('Nonfinite vector')
        return v
    def end(self):
        if self.data.read(1): raise ValueError('Trailing gameplay bytes')


def health(unit, value):
    return b'\x06\x09' + pack('I', unit) + b'\x40\x00\x00' + pack('f', value)


class Practice(LoadoutSystems, MatchSystems):
    def __init__(self, packets, send, event, clock=time.monotonic):
        self.packets, self.send, self.event, self.clock = packets, send, event, clock
        self.position = tuple(packets['practice'].get('spawn_position',(14.5,5,23.5)))
        self.target_position = tuple(packets['practice']['target_position'])
        self.weapons = {packets['keys'][w['id']]: w['data'] for w in packets['practice']['weapons']}
        self.current = packets['keys'][packets['practice'].get('current_gear','gear_sarge_stone_m60')]
        self.ammo = {k: [w['ammo'][0].get('mag_size'), w['ammo'][0]['pool']['pool_size']] for k,w in self.weapons.items() if w.get('ammo')}
        self.target_health = 160
        self.respawn_at = None
        self.reload_at = None
        self.last_cast = -100
        self.shots = {}
        self.kills = 0
        self.player_respawn_at = None
        raw = zlib.decompress(packets['terrain'])
        self.size = struct.unpack_from('<HHH', raw)
        self.blocks = bytearray(raw[6:])
        self.block_cards = {b["block_id"]:b for b in packets["practice"].get("blocks",[])}
        self.block_damage = {}
        self.resources = packets['practice'].get('initial_resources',500)
        self.build_at = None
        self.build_cell = None
        if len(self.blocks) != math.prod(self.size)*4: raise ValueError('Terrain size mismatch')
        self.passable = set(packets['practice']['passable'])
        self.init_systems()
        self.init_match()

    def start(self):
        self.send(self.packets['target-create']); self.send(self.packets['target-state'])
        self.event('practice_target_spawned', unit=2)
        for objective in ([] if getattr(self,'objectives_started',False) else self.packets['practice'].get('objectives',[])):
            self.spawn_unit(objective['unit_key'],tuple(objective['position'][a] for a in 'xyz'),1 if objective['team']=='team1' else 2)

        self.objectives_started=True

    def ammo_packet(self):
        # Only the two firearms have ammo. Melee has an empty list.
        result = b'\x06\x09' + pack('I',1) + b'\x02\x00\x00' + bytes([len(self.weapons)])
        for key in self.weapons:
            result += key
            if key in self.ammo:
                mag,pool=self.ammo[key]
                result += b'\x01' + bytes([0xe0 if mag is not None else 0xa0]) + pack('i',0)
                if mag is not None: result += pack('f',mag)
                result += pack('f',pool)
            else: result += b'\x00'
        return result

    def tick(self):
        now = self.clock()
        self.tick_systems()
        self.tick_match()
        self.shots = {k:v for k,v in self.shots.items() if now-v[0] < 5}
        if self.player_respawn_at is not None and now>=self.player_respawn_at:
            self.player_respawn_at=None;self.position=self.spawn_position;self.player_health=self.max_health
            self.current=self.packets['keys'][self.packets['practice'].get('current_gear','gear_sarge_stone_m60')]
            hero=self.packets['hero-create']
            if self.spawn_position!=self.base_spawn:hero=hero.replace(pack('fff',*self.base_spawn),pack('fff',*self.spawn_position))
            self.send(hero);self.send(self.packets['hero-state']);self.send_loadout()
            self.send(self.ammo_packet());self.send_resource()
            if self.ability:self.ability_update()
            self.drown_at=None;self.respawn_timer();self.event('player_respawned')
        if self.build_at is not None and now>=self.build_at:
            cell=self.build_cell; self.build_at=None; self.build_cell=None
            if math.dist(self.position,tuple(v+.5 for v in cell))<4 and self.replaceable(self.cell_index(cell)) and self.pending_device:
                self.complete_build(cell,self.pending_device)
            self.pending_device=None
            self.send(b'\x06\x3b'+pack('I',1))
        if self.reload_at is not None and now >= self.reload_at:
            mag,pool = self.ammo[self.current]
            amount = min(self.weapons[self.current]['ammo'][0]['mag_size']-mag,pool)
            self.ammo[self.current] = [mag+amount,pool-amount]
            self.reload_at = None
            self.send(self.ammo_packet()); self.send(b'\x06\x16'+pack('I?',1,False))
            self.event('reload_completed', magazine=mag+amount, reserve=pool-amount)
        if self.respawn_at is not None and now >= self.respawn_at:
            self.respawn_at = None; self.target_health = 160
            self.start()

    def switch(self, key):
        if key not in self.weapons or self.player_respawn_at is not None: return False
        self.current = key; self.reload_at = None; self.build_at = None; self.build_cell = None
        self.send(b'\x06\x16'+pack('I?',1,False))
        return True

    def move(self, position):
        self.position=position
        if self.recall_at is not None and math.dist(position,self.recall_origin)>.5:self.cancel_recall()
        if position[1]<self.packets['practice'].get('kill_height',-3) and self.player_respawn_at is None:
            self.damage_entity(1,self.max_health,self.current);self.event('player_void_death')

    def cell_index(self, cell):
        x,y,z=cell
        if not all(0<=a<b for a,b in zip(cell,self.size)): return None
        return ((x*self.size[1]+y)*self.size[2]+z)*4

    def set_block(self, cell, block_id, damage=0, vdata=None, ldata=None):
        index=self.cell_index(cell)
        if self.blocks[index]!=block_id:
            old=self.blocks[index];self.world_revision+=1
            if old and block_id==0 and vdata!=2:self.collapse_seeds.update(self.neighbors(cell))
        oldv,oldl=self.blocks[index+2:index+4] if self.blocks[index]==block_id else (0,0)
        vdata=oldv if vdata is None else vdata;ldata=oldl if ldata is None else ldata
        self.blocks[index:index+4]=bytes([block_id,damage,vdata,ldata])
        self.send(b'\x06\x06\x01'+pack('hhh',*cell)+bytes([0xf0,block_id,damage,vdata,ldata]))

    def send_resource(self):
        self.send(b'\x06\x09'+pack('I',1)+b'\x00\x10\x00'+pack('f',self.resources))

    def damage_block(self, point, tool, origin):
        cell=tuple(math.floor(x) for x in point);index=self.cell_index(cell)
        if index is None: return
        card=self.block_cards.get(self.blocks[index])
        if not card or not card.get('destructible') or not card.get('health'): return
        effect=tool.get('hit_effect',{})
        if effect.get('type')!='damage': return
        damage=effect['damage']; hp=card['health']
        if hp.get('mining_only') and not damage.get('mining'): return
        if hp.get('melee_only') and not damage.get('melee'): return
        # Exclude the struck voxel from the occlusion trace.
        if not self.clear_line(origin,point,ignore_end=True): return
        self.impact(point,origin,effect.get('impact') or 'impact_melee_common')
        total=self.block_damage.get(cell,0)+max(0,damage['world_damage']-hp.get('toughness',0))
        self.block_damage[cell]=total
        if total>=hp['max_health']:
            self.set_block(cell,0,vdata=1);self.block_damage.pop(cell,None)
            if damage.get('mining'):
                self.resources+=int((card.get('reward') or {}).get('player_reward',0));self.send_resource()
            self.event('block_destroyed', cell=cell)
        else:
            self.set_block(cell,card['block_id'],min(254,int(255*total/hp['max_health'])))

    def clear_line(self, start, end, ignore_start=False, ignore_end=False):
        distance = math.dist(start,end)
        for i in range(1,max(2,math.ceil(distance*8))):
            t = i/max(2,math.ceil(distance*8))
            x,y,z = (math.floor(a+(b-a)*t) for a,b in zip(start,end))
            if not (0<=x<self.size[0] and 0<=y<self.size[1] and 0<=z<self.size[2]): return False
            if ignore_start and (x,y,z)==tuple(math.floor(v) for v in start):continue
            if ignore_end and (x,y,z)==tuple(math.floor(v) for v in end):continue
            if self.blocks[((x*self.size[1]+y)*self.size[2]+z)*4] not in self.passable: return False
        return True

    def handle(self, packet):
        fn = packet[1]; now = self.clock()
        if self.match_request(packet):return True
        if fn in (30,36,47,56):self.cancel_recall()
        if self.player_respawn_at is not None and fn in (30,31,33,56):
            if fn in (33,56): self.send(packet[:4]+b'\x00\x00')
            return True
        if fn == 36:return self.channel_request(packet)
        if fn == 37:self.channel=None;return True
        if fn == 47:return self.cast_ability(packet)
        if fn == 56:
            r=Reader(packet[4:])
            if r.read('B')!=0xf8: raise ValueError('Invalid build fields')
            tool_index=r.read('B'); device=pack('I',r.read('I'));inside=r.vector();outside=r.vector();r.read('?');r.end()
            cell=tuple(math.floor(v) for v in outside);base=tuple(math.floor(v) for v in inside)
            index=self.cell_index(cell);base_index=self.cell_index(base)
            tools=self.weapons[self.current]['tools']
            occupied=any(abs(cell[0]+.5-p[0])<.8 and abs(cell[2]+.5-p[2])<.8 and p[1]-1<cell[1]<p[1]+2 for p in (self.position,self.target_position))
            definition=self.build_definition(device)
            occupied=occupied or any(d['cell']==cell for d in self.placed.values())
            accepted=(definition is not None and (not definition.get('ground_only') or base[1]==cell[1]-1) and self.build_at is None and tool_index<len(tools) and tools[tool_index]['type']=='build' and self.resources>=self.build_cost(definition) and index is not None and base_index is not None and self.replaceable(index) and self.blocks[base_index] not in self.passable and sum(abs(a-b) for a,b in zip(cell,base))==1 and math.dist(self.position,outside)<4 and not occupied and self.clear_line(tuple(a+b for a,b in zip(self.position,(0,1.5,0))),outside))
            self.send(packet[:4]+bytes([0,accepted]))
            if accepted:
                self.build_face={(0,-1,0):1,(0,1,0):0,(-1,0,0):3,(1,0,0):2,(0,0,-1):5,(0,0,1):4}[tuple(a-b for a,b in zip(base,cell))]
                self.build_at=now+(definition.get('build_time') or 0)/(1+max(0,self.buffs_for(1).get('build_speed',0)));self.build_cell=cell;self.pending_device=definition
                self.send(b'\x06\x3a'+pack('I',1)+packet[4:])
            return True
        if fn == 57:
            self.build_at=None;self.build_cell=None;self.send(b'\x06\x3b'+pack('I',1));return True
        if fn == 33:  # Reload RPC
            accepted = self.current in self.ammo and self.reload_at is None
            if accepted:
                mag,pool = self.ammo[self.current]
                accepted = mag is not None and pool>0 and mag<self.weapons[self.current]['ammo'][0]['mag_size']
            self.send(packet[:4]+bytes([0,accepted]))
            if accepted:
                self.reload_at = now+self.weapons[self.current]['reload']['reload_time']
                self.send(b'\x06\x13'+pack('I',1))
                self.send(b'\x06\x16'+pack('I?',1,True))
                self.event('reload_started')
            return True
        if fn == 34:
            self.reload_at = None; self.send(b'\x06\x16'+pack('I?',1,False)); return True
        if fn == 30:  # Cast; consume server-tracked ammunition and remember shot IDs
            r=Reader(packet[2:]); flags=r.read('B')
            if flags & 0xe0 != 0xe0: raise ValueError('Missing cast fields')
            tool_index=r.read('B'); origin=r.vector(); shots=[]
            for _ in range(r.size()):
                direction=r.vector(); shot=r.read('Q') if r.read('?') else None
                shots.append((direction,shot))
            if flags & 16: r.read('f')
            r.end()
            tools=self.weapons[self.current]['tools']
            if tool_index>=len(tools): return True
            tool=tools[tool_index].copy()
            bullet=tool.get('bullet') or {}
            if bullet.get('type')=='projectile':tool.update(grenade=True,speed=bullet.get('max_speed') or bullet.get('min_speed',50))
            if tool.get('type')=='throw':tool.setdefault('range',100)
            interval=tool.get('timing',{}).get('attack_time',0.125)
            count=(tool.get('bullets') or {}).get('count',1)
            cost=(tool.get('ammo') or {}).get('rate',0)
            valid=tool['type'] in ('shot','spinup','throw','melee') and self.reload_at is None and now-self.last_cast>=interval*0.85 and math.dist(origin,self.position)<4 and (len(shots)<=32 if tool['type']=='melee' else len(shots)==count)
            ammo_index=0 if self.current in self.ammo and self.ammo[self.current][0] is not None else 1
            if self.current in self.ammo: valid=valid and self.ammo[self.current][ammo_index]>=cost
            if not valid: self.send(self.ammo_packet()); return True
            if any(shot is None or shot in self.shots for _,shot in shots) or len({shot for _,shot in shots})!=len(shots): return True
            self.last_cast=now
            for direction,shot in shots:self.shots[shot]=(now,origin,tool,self.current,direction)
            if self.current in self.ammo and cost:
                self.ammo[self.current][ammo_index]-=cost; self.send(self.ammo_packet())
            self.event('shot_accepted', tool=tool_index)
            return True
        if fn == 31:
            r=Reader(packet[2:]); hits=[]
            for _ in range(r.size()):
                shot=r.read('Q'); flags=r.read('B')
                if flags & 0xe0 != 0xe0: raise ValueError('Missing hit fields')
                point=r.vector(); r.read('B'); r.read('hhh')
                if flags & 16: r.read('B')
                target=r.read('I') if flags & 8 else None
                if flags & 4: r.read('?')
                hits.append((shot,point,target))
            r.end()
            for shot,point,target in hits:
                record=self.shots.pop(shot,None)
                if not record: continue
                shot_time,origin,tool,key,aim=record
                if math.dist(origin,point)>tool.get('range',0)+.1: continue
                vector=tuple(b-a for a,b in zip(origin,aim));length=sum(v*v for v in vector)
                if length<.001: continue
                projection=sum((b-a)*v for a,b,v in zip(origin,point,vector))/length
                closest=tuple(a+max(0,projection)*v for a,v in zip(origin,vector))
                if tool.get('grenade'):
                    if math.dist(origin,point)>tool['speed']*(now-shot_time+.5)+4:continue
                elif math.dist(closest,point)>1: continue
                if target is not None and not self.hit_near_unit(target,point):continue
                effect=tool.get('hit_effect',{})
                if effect.get('type') in ('bunch','splash_damage','unit_spawn','teleport_to'):
                    if not tool.get('grenade') and not self.clear_line(origin,point,ignore_end=True):continue
                    self.apply_effect(effect,point,origin,key,target);continue
                if target is None:
                    self.damage_block(point,tool,origin);continue
                if target in self.placed:
                    if self.hit_near_unit(target,point) and self.clear_line(origin,point):
                        self.apply_effect(effect,point,origin,key,target)
                    continue
                if target!=2 or self.target_health<=0: continue
                if math.dist(point,self.target_position)>2.5 or math.dist(origin,point)>tool.get('range',0) or not self.clear_line(origin,point): continue
                effect=tool.get('hit_effect',{})
                if effect.get('type')!='damage': continue
                damage=float(effect['damage']['player_damage'])
                falloff=effect.get('falloff')
                if falloff:
                    span=falloff['min_damage_range']-falloff['max_damage_range']
                    fraction=max(0,min(1,(math.dist(origin,point)-falloff['max_damage_range'])/span)) if span else 0
                    damage*=1-fraction*falloff['reduction_coeff']
                self.impact(point,origin,effect.get('impact') or 'impact_bullet_common',(2,))
                self.target_health=max(0,self.target_health-damage)
                self.send(health(2,self.target_health))
                self.send(b'\x06\x44'+pack('I?Iff?',2,True,1,damage,damage,False))
                self.event('target_damaged', health=self.target_health, damage=damage)
                if self.target_health==0:
                    self.kills+=1;self.statuses.pop(2,None);self.buff_cache.pop(2,None)
                    self.send(b'\x06\x43'+pack('?I',True,1)+b'\x00'+pack('I',2)+key+b'\x01\x00')
                    self.send(b'\x06\x0a'+pack('I',2)); self.respawn_at=now+3
                    self.event('practice_kill', kills=self.kills)
            return True
        # Local visual projectiles and attack-loop effects are predicted by this client.
        if fn in (29,35,39,40,49,50,51): return True
        return False
