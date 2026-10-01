"""Recovered-loadout adapters for the isolated beta practice simulator."""
import math
import struct
import time
from gameclock import millis
import zlib


def pack(fmt,*values): return struct.pack('<'+fmt,*values)
def key(name): return pack('I',zlib.crc32(name.encode('utf-8')))
def health(unit,value): return b'\x06\x09'+pack('I',unit)+b'\x40\x00\x00'+pack('f',value)


from hero_systems import HeroSystems

class LoadoutSystems(HeroSystems):
    def init_systems(self):
        config=self.packets['practice']
        self.loadout={key(d['_id']):d for d in config.get('loadout',[])}
        self.device_definitions={d['_id']:d for d in config.get('unit_devices',[])}
        self.placed={};self.next_device=100;self.pending_device=None
        self.base_spawn=tuple(config.get('spawn_position',(14.5,5,23.5)))
        self.spawn_position=self.base_spawn
        self.ability=config.get('ability');self.charges=(self.ability.get('charges') or {}).get('max_charges',1) if self.ability else 0
        self.charge_at=None;self.last_ability=-100
        self.max_health=config.get('max_health',160)
        self.player_health=self.max_health
        self.radar_marked=False
        self.init_heroes()

    def ability_update(self):
        if self.player_respawn_at is not None or not self.ability:return
        end=0 if self.charge_at is None else int(millis()+max(0,self.charge_at-self.clock())*1000)
        self.send(b'\x06\x09'+pack('I',1)+b'\x00\xe0\x00'+key(self.ability['_id'])+pack('iQ',self.charges,end))

    def cast_ability(self,packet):
        from practice import Reader
        r=Reader(packet[4:]);flags=r.read('B')
        ability_key=pack('I',r.read('I'));origin=r.vector() if flags&64 else self.position;shots=[]
        if flags&32:
            for _ in range(r.size()):
                aim=r.vector();shot=r.read('Q') if r.read('?') else None;shots.append((aim,shot))
        r.end();now=self.clock();app=(self.ability or {}).get('application',{})
        accepted=bool(flags&128 and self.ability and ability_key==key(self.ability['_id']) and self.charges>0 and self.player_respawn_at is None and now-self.last_ability>.35 and math.dist(origin,self.position)<4)
        validation=(self.ability or {}).get('validate') or {}
        if validation.get('type')=='devices':
            target=validation.get('device_targeting') or {};labels=target.get('affected_labels') or [];types=target.get('affected_units') or []
            count=sum(self.allowed({'targeting':target},unit) for unit in self.placed)
            accepted=accepted and count>=validation.get('min_count',1)
        if app.get('type')=='projectile':accepted=accepted and len(shots)==1 and shots[0][1] is not None and shots[0][1] not in self.shots
        elif app.get('type')=='hitscan':accepted=accepted and len(shots)==1 and math.dist(origin,shots[0][0])<=app.get('range',100)+1
        elif app.get('type')!='self':accepted=False
        self.send(packet[:4]+bytes([0,bool(accepted)]))
        if accepted:
            self.charges-=1;self.last_ability=now
            if self.charge_at is None:self.charge_at=now+self.ability['charges']['charge_cooldown']
            self.ability_update()
            if app['type']=='projectile':
                aim,shot=shots[0];tool={'type':'shot','range':100,'grenade':True,'speed':app['speed'],'hit_effect':self.ability['hit_effect']}
                self.shots[shot]=(now,origin,tool,ability_key,aim)
            else:self.apply_effect(self.ability['hit_effect'],shots[0][0] if shots else self.position,origin,ability_key,1 if app['type']=='self' else None)
            self.event('ability_cast',ability=self.ability['_id'],charges=self.charges)
        else:self.ability_update()

        return True

    def impact(self,point,origin,impact,hit_units=()):
        if not impact:return
        # Original protocol ImpactData: position, normal, caster, impact key, hit units, shot origin, crit.
        self.send(b'\x06\x12\xfe'+pack('fffhhhI',*point,0,10,0,1)+key(impact)+bytes([len(hit_units)])+b''.join(pack('I',i) for i in hit_units)+pack('fff?',*origin,False))

    def damage_entity(self,unit,amount,source):
        if amount<=0:return
        if unit in self.placed and 'objective' in self.placed[unit]['definition'].get('labels',[]):
            if self.team(unit)==1 or self.phase_end is not None:return
        if unit==1:self.cancel_recall()
        if unit==2:
            if self.target_health<=0:return
            self.target_health=max(0,self.target_health-amount);remaining=self.target_health
        elif unit==1:
            if self.player_respawn_at is not None:return
            self.player_health=max(0,self.player_health-amount);remaining=self.player_health
        elif unit in self.placed:
            if not (self.placed[unit]['definition'].get('health') or {}).get('health'):return
            self.placed[unit]['health']=max(0,self.placed[unit]['health']-amount);remaining=self.placed[unit]['health']
        else:return
        self.send(health(unit,remaining));self.send(b'\x06\x44'+pack('I?Iff?',unit,True,1,amount,amount,False))
        self.event('explosive_damage',unit=unit,health=remaining,damage=amount)
        if remaining==0:
            self.send(b'\x06\x43'+(b'\x00' if unit==1 else pack('?I',True,1))+b'\x00'+pack('I',unit)+source+b'\x00')
            if unit in (1,2):self.send(b'\x06\x0a'+pack('I',unit))
            self.statuses.pop(unit,None);self.buff_cache.pop(unit,None)
            if unit==2:
                self.kills+=1;self.respawn_at=self.clock()+3;self.event('practice_kill',kills=self.kills)
            elif unit==1:
                self.player_died()
            else:
                self.remove_unit(unit,trigger=True)

    def apply_effect(self,effect,point,origin,source,target=None,owner=1):
        if not effect or not self.allowed(effect,target,owner):return
        kind=effect.get('type')
        if kind!='splash_damage':self.impact(point,origin,effect.get('impact'),(target,) if target else ())
        if self.extra_effect(effect,point,origin,source,target,owner):return
        if kind=='bunch':
            for status in effect.get('constant',[]):
                if target is not None:self.add_status(target,status,owner=owner)
            for child in effect.get('instant',[]):self.apply_effect(child,point,origin,source,target,owner)
        elif kind=='splash_damage':
            radius=min(8,float(effect['radius']));damage=effect['damage']
            entities={2:tuple(a+b for a,b in zip(self.target_position,(0,1,0))),1:tuple(a+b for a,b in zip(self.position,(0,1,0)))}
            entities.update({i:d['position'] for i,d in self.placed.items()})
            # Snapshot visibility before mutating any blocks in this explosion.
            visible=[i for i,p in entities.items() if self.allowed(effect,i,owner) and math.dist(p,point)<=radius and self.clear_line(point,p,ignore_start=True)]
            self.impact(point,origin,effect.get('impact'),visible)
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
            if target is not None:self.damage_entity(target,effect['damage'].get('player_damage' if target<3 else 'objective_damage' if 'objective' in self.placed.get(target,{}).get('definition',{}).get('labels',[]) else 'world_damage',0),source)
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
            self.set_block(cell,0,vdata=1);self.block_damage.pop(cell,None);self.event('block_destroyed',cell=cell)
        else:
            self.event('block_damaged',cell=cell,damage=total)
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
        elif built['category']=='block':
            visual=built.get('visual',{})
            self.set_block(cell,built['block_id'],vdata=getattr(self,'build_face',1) if visual.get('face_align') else 0,ldata=1 if built.get('has_team') else 0)
            self.block_teams[cell]=1
        else:
            template=self.packets['device-templates'][definition['_id']]
            sentinel=pack('fff',101.25,102.5,103.75)
            if template.count(sentinel)!=1:raise ValueError('Invalid device transform template')
            face=getattr(self,'build_face',1)
            normals={1:(0,1,0),0:(0,-1,0),2:(-1,0,0),3:(1,0,0),4:(0,0,-1),5:(0,0,1)}
            rotations={1:(0,0,0),0:(1800,0,0),2:(0,0,900),3:(0,0,-900),4:(-900,0,0),5:(900,0,0)}
            mounted=face!=1 and not definition.get('ground_only',True)
            normal=normals[face] if mounted else (0,1,0)
            offset=built.get('beta_falling_bottom_offset',.5)
            position=tuple(v+.5+n*(offset-.5+.02) for v,n in zip(cell,normal));unit=self.next_device;self.next_device+=1
            packet=template[:2]+pack('I',unit)+template[6:]
            at=packet.index(sentinel)
            packet=packet[:at]+pack('fffhhh',*position,*(rotations[face] if mounted else (0,0,0)))+packet[at+18:]
            hp=built['health']['health']['max_health']
            self.placed[unit]={'device':definition['_id'],'definition':built,'position':position,'cell':cell,'health':hp,'created':self.clock(),'team':1}
            if mounted:self.placed[unit]['support']=tuple(c-n for c,n in zip(cell,normal));self.placed[unit]['rotation']=rotations[face]
            self.send(packet);self.send(health(unit,hp));self.unit_teams[unit]=1
            for effect in (built.get('init_effects') or [])+(built.get('enabled_effects') or []):self.add_status(unit,effect,built.get('lifetime') or 3600,owner=unit,check=False)
            if built.get('data',{}).get('type')=='bomb':
                deadline=int(millis()+built['data']['timeout']*1000)
                self.send(b'\x06\x09'+pack('I',unit)+b'\x00\x00\x10'+pack('Q',deadline))
            if built.get('spawn_point') is not None:self.update_spawns()
            self.event('device_built',device=definition['_id'],unit=unit)
        self.resources-=cost;self.send_resource();self.send_loadout();self.event('block_built',cell=cell,device=definition['_id'])

    def tick_systems(self):
        now=self.clock()
        self.tick_heroes()
        if self.charge_at is not None and now>=self.charge_at:
            self.charges=min(self.ability['charges']['max_charges'],self.charges+1)
            self.charge_at=now+self.ability['charges']['charge_cooldown'] if self.charges<self.ability['charges']['max_charges'] else None
            self.ability_update();self.event('ability_recharged',charges=self.charges)
        radars=[d for d in self.placed.values() if d['device']=='device_generic_radar']
        marked=self.target_health>0 and any(math.dist(d['position'],self.target_position)<=8 for d in radars)
        if marked!=self.radar_marked:
            self.radar_marked=marked
            if self.target_health>0:self.publish_buffs(2)
            self.event('radar_detection',detected=marked)
        for unit,entry in list(self.placed.items()):
            data=entry['definition'].get('data',{})
            targets=[i for i in (1,2) if (self.player_respawn_at is None if i==1 else self.target_health>0) and self.team(i)!=self.team(unit) and math.dist(entry['position'],self.unit_position(i))<data.get('trigger_radius',0)]
            mine=data.get('type')=='landmine' and bool(targets)
            bomb=data.get('type')=='bomb' and now>=entry['created']+data['timeout']
            if mine or bomb:
                self.remove_unit(unit)
                self.apply_effect(data['trigger_effect'],entry['position'],entry['position'],key(entry['definition']['_id']),targets[0] if mine else None,unit)
                self.event('device_detonated',device=entry['definition']['_id'])
