"""Recovered-loadout adapters for the isolated beta practice simulator."""
import math
import struct
import time
import zlib


def pack(fmt,*values): return struct.pack('<'+fmt,*values)
def key(name): return pack('I',zlib.crc32(name.encode('utf-8')))
def health(unit,value): return b'\x06\x09'+pack('I',unit)+b'\x40\x00\x00'+pack('f',value)


class LoadoutSystems:
    def init_systems(self):
        config=self.packets['practice']
        self.loadout={key(d['_id']):d for d in config.get('loadout',[])}
        self.device_definitions={d['_id']:d for d in config.get('unit_devices',[])}
        self.placed={};self.next_device=100;self.pending_device=None
        self.spawn_position=(14.5,5,23.5)
        self.ability=config.get('ability');self.charges=self.ability['charges']['max_charges'] if self.ability else 0
        self.charge_at=None;self.last_ability=-100
        self.player_health=160
        self.radar_marked=False

    def ability_update(self):
        if self.player_respawn_at is not None:return
        end=0 if self.charge_at is None else int(time.time()*1000+max(0,self.charge_at-self.clock())*1000)
        self.send(b'\x06\x09'+pack('I',1)+b'\x00\xe0\x00'+key(self.ability['_id'])+pack('iQ',self.charges,end))

    def cast_ability(self,packet):
        from practice import Reader
        r=Reader(packet[4:]);flags=r.read('B')
        if flags!=0xe0: raise ValueError('Ability requires origin and shot list')
        ability_key=pack('I',r.read('I'));origin=r.vector();shots=[]
        for _ in range(r.size()):
            aim=r.vector();shot=r.read('Q') if r.read('?') else None;shots.append((aim,shot))
        r.end();now=self.clock()
        accepted=bool(self.ability and ability_key==key(self.ability['_id']) and self.charges>0 and self.player_respawn_at is None and now-self.last_ability>.35 and math.dist(origin,self.position)<4 and len(shots)==1 and shots[0][1] is not None and shots[0][1] not in self.shots)
        self.send(packet[:4]+bytes([0,accepted]))
        if accepted:
            aim,shot=shots[0];self.charges-=1;self.last_ability=now
            if self.charge_at is None:self.charge_at=now+self.ability['charges']['charge_cooldown']
            self.ability_update()
            tool={'type':'shot','range':100,'grenade':True,'speed':self.ability['application']['speed'],'hit_effect':self.ability['hit_effect']}
            self.shots[shot]=(now,origin,tool,ability_key,aim)
            self.event('ability_cast',charges=self.charges)
        return True

    def impact(self,point,origin,impact,hit_units=()):
        if not impact:return
        # Original protocol ImpactData: position, normal, caster, impact key, hit units, shot origin, crit.
        self.send(b'\x06\x12\xfe'+pack('fffhhhI',*point,0,10,0,1)+key(impact)+bytes([len(hit_units)])+b''.join(pack('I',i) for i in hit_units)+pack('fff?',*origin,False))

    def damage_entity(self,unit,amount,source):
        if amount<=0:return
        if unit==2:
            if self.target_health<=0:return
            self.target_health=max(0,self.target_health-amount);remaining=self.target_health
        elif unit==1:
            if self.player_respawn_at is not None:return
            self.player_health=max(0,self.player_health-amount);remaining=self.player_health
        elif unit in self.placed:
            self.placed[unit]['health']=max(0,self.placed[unit]['health']-amount);remaining=self.placed[unit]['health']
        else:return
        self.send(health(unit,remaining));self.send(b'\x06\x44'+pack('I?Iff?',unit,True,1,amount,amount,False))
        self.event('explosive_damage',unit=unit,health=remaining,damage=amount)
        if remaining==0:
            self.send(b'\x06\x43'+pack('?I',True,1)+b'\x00'+pack('I',unit)+source+b'\x01\x00')
            self.send(b'\x06\x0a'+pack('I',unit))
            if unit==2:
                self.kills+=1;self.respawn_at=self.clock()+3;self.event('practice_kill',kills=self.kills)
            elif unit==1:
                self.player_respawn_at=self.clock()+3;self.reload_at=None;self.build_at=None;self.shots.clear()
            else:
                self.placed.pop(unit,None);self.send_loadout()

    def apply_effect(self,effect,point,origin,source,target=None):
        kind=effect.get('type')
        self.impact(point,origin,effect.get('impact'))
        if kind=='bunch':
            for child in effect.get('instant',[]):self.apply_effect(child,point,origin,source,target)
        elif kind=='splash_damage':
            radius=min(8,float(effect['radius']));damage=effect['damage']
            entities={2:tuple(a+b for a,b in zip(self.target_position,(0,1,0))),1:tuple(a+b for a,b in zip(self.position,(0,1,0)))}
            entities.update({i:d['position'] for i,d in self.placed.items()})
            # Snapshot visibility before mutating any blocks in this explosion.
            visible=[i for i,p in entities.items() if math.dist(p,point)<=radius and self.clear_line(point,p,ignore_start=True)]
            cells=[]
            for x in range(math.floor(point[0]-radius),math.ceil(point[0]+radius)+1):
                for y in range(math.floor(point[1]-radius),math.ceil(point[1]+radius)+1):
                    for z in range(math.floor(point[2]-radius),math.ceil(point[2]+radius)+1):
                        cell=(x,y,z);index=self.cell_index(cell);center=(x+.5,y+.5,z+.5)
                        if index is not None and self.blocks[index] and math.dist(center,point)<=radius and self.clear_line(point,center,ignore_start=True,ignore_end=True):cells.append(cell)
            for i in visible:self.damage_entity(i,damage['player_damage'] if i<3 else damage['world_damage'],source)
            for cell in cells:self.blast_block(cell,damage)
            self.event('explosion',radius=radius,affected_units=visible,affected_cells=len(cells))
        elif kind=='knockback':
            distance=math.dist(tuple(a+b for a,b in zip(self.position,(0,1,0))),point)
            radius=effect['effect_range']
            if effect.get('affect_caster') and self.player_respawn_at is None and distance<radius and self.clear_line(point,self.position,ignore_start=True):
                scale=max(0,1-distance/radius) if effect.get('linear_falloff') else 1
                self.send(b'\x06\x0d'+pack('I',1)+b'\x02\xe0'+pack('fffff',*point,effect['force']*scale,effect['midair_force']*scale))
        elif kind=='damage':
            if target is not None:self.damage_entity(target,effect['damage']['player_damage'],source)
            else:self.blast_block(tuple(math.floor(v) for v in point),effect['damage'])

    def blast_block(self,cell,damage):
        index=self.cell_index(cell)
        if index is None:return
        card=self.block_cards.get(self.blocks[index]);hp=card.get('health') if card else None
        if not card or not card.get('destructible') or not hp:return
        if hp.get('mining_only') and not damage.get('mining'):return
        if hp.get('melee_only') and not damage.get('melee'):return
        total=self.block_damage.get(cell,0)+max(0,damage['world_damage']-hp.get('toughness',0))
        if total>=hp['max_health']:
            self.set_block(cell,0);self.block_damage.pop(cell,None);self.event('block_destroyed',cell=cell)
        else:
            self.block_damage[cell]=total;self.set_block(cell,card['block_id'],min(254,int(255*total/hp['max_health'])))

    def build_definition(self,device):
        if self.loadout:return self.loadout.get(device)
        if device==self.packets.get('brick-key'):return {'_id':'device_block_brick','device_key':'block_brick','base_cost':5,'build_time':.2,'legacy':True}
        return None

    def build_cost(self,definition):
        count=sum(d['device']==definition['_id'] for d in self.placed.values())
        return definition['base_cost']+count*(definition.get('cost_inc_per_unit') or 0)

    def send_loadout(self):
        if not self.loadout or self.player_respawn_at is not None:return
        packet=b'\x06\x09'+pack('I',1)+b'\x00\x01\x00'+bytes([len(self.loadout)])
        for slot,(device,definition) in enumerate(self.loadout.items(),1):
            packet+=pack('i',slot)+b'\xe0'+device+pack('ff',self.build_cost(definition),definition.get('cost_inc_per_unit') or 0)
        self.send(packet)

    def complete_build(self,cell,definition):
        cost=self.build_cost(definition)
        if self.resources<cost:return
        built=self.device_definitions.get(definition['device_key'])
        if definition.get('legacy'):self.set_block(cell,7)
        elif built is None:return
        elif built['category']=='block':self.set_block(cell,built['block_id'])
        else:
            template=self.packets['device-templates'][definition['_id']]
            sentinel=pack('fff',101.25,102.5,103.75)
            if template.count(sentinel)!=1:raise ValueError('Invalid device transform template')
            position=tuple(v+.5 for v in cell);unit=self.next_device;self.next_device+=1
            packet=template[:2]+pack('I',unit)+template[6:]
            packet=packet.replace(sentinel,pack('fff',*position))
            hp=built['health']['health']['max_health']
            self.placed[unit]={'device':definition['_id'],'definition':built,'position':position,'cell':cell,'health':hp,'created':self.clock()}
            self.send(packet);self.send(health(unit,hp))
            if built.get('data',{}).get('type')=='bomb':
                deadline=int(time.time()*1000+built['data']['timeout']*1000)
                self.send(b'\x06\x09'+pack('I',unit)+b'\x00\x00\x10'+pack('Q',deadline))
            if built.get('spawn_point') is not None:self.spawn_position=(cell[0]+.5,cell[1]+1.2,cell[2]+.5)
            self.event('device_built',device=definition['_id'],unit=unit)
        self.resources-=cost;self.send_resource();self.send_loadout();self.event('block_built',cell=cell,device=definition['_id'])

    def tick_systems(self):
        now=self.clock()
        if self.charge_at is not None and now>=self.charge_at:
            self.charges=min(self.ability['charges']['max_charges'],self.charges+1)
            self.charge_at=now+self.ability['charges']['charge_cooldown'] if self.charges<self.ability['charges']['max_charges'] else None
            self.ability_update();self.event('ability_recharged',charges=self.charges)
        spawns=[d for d in self.placed.values() if d['definition'].get('spawn_point') is not None]
        self.spawn_position=tuple(a+b for a,b in zip(spawns[-1]['position'],(0,.7,0))) if spawns else (14.5,5,23.5)
        radars=[d for d in self.placed.values() if d['device']=='device_generic_radar']
        marked=self.target_health>0 and any(math.dist(d['position'],self.target_position)<=8 for d in radars)
        if marked!=self.radar_marked:
            self.radar_marked=marked
            if self.target_health>0:self.send(b'\x06\x09'+pack('I',2)+b'\x00\x02\x00\x01\x12'+pack('f',float(marked)))
            self.event('radar_detection',detected=marked)
        for unit,entry in list(self.placed.items()):
            data=entry['definition'].get('data',{})
            mine=data.get('type')=='landmine' and self.target_health>0 and math.dist(entry['position'],self.target_position)<data['trigger_radius']
            bomb=data.get('type')=='bomb' and now>=entry['created']+data['timeout']
            if mine or bomb:
                self.placed.pop(unit,None);self.send(b'\x06\x0a'+pack('I',unit));self.send_loadout()
                self.apply_effect(data['trigger_effect'],entry['position'],entry['position'],key(entry['definition']['_id']))
                self.event('device_detonated',device=entry['definition']['_id'])
