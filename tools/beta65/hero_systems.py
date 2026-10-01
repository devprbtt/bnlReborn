"""Local hero abilities, channels, timed effects and spawned units for protocol 65."""
import math
import struct
import zlib
from gameclock import millis

def pack(fmt,*v):return struct.pack('<'+fmt,*v)
def key(s):return pack('I',zlib.crc32(s.encode()))

class HeroSystems:
    def init_heroes(self):
        self.definitions={c['_id']:c for c in self.packets.get('definitions',[])}
        self.statuses={};self.buff_cache={};self.delayed=[];self.mortar_flights={};self.next_projectile=0x7000000000000000;self.channel=None;self.last_effect_tick=self.clock()
        self.block_teams={};self.block_contact_at={};self.status_owner={};self.status_next={};self.unit_teams={1:1,2:2,-1:1,-2:2}

    def unit_position(self,unit):
        return self.position if unit==1 else self.target_position if unit==2 else self.placed.get(unit,{}).get('position')

    def hit_near_unit(self,unit,point):
        pos=self.unit_position(unit)
        if pos is None:return False
        definition=self.placed.get(unit,{}).get('definition',{})
        size=definition.get('size')
        if size and definition.get('pivot_type')=='zero':
            return all(p-.35<=v<=p+size[a]+.35 for a,p,v in zip('xyz',pos,point))
        return math.dist(pos,point)<=3

    def team(self,unit):return 1 if unit==1 else 2 if unit==2 else self.unit_teams.get(unit,self.placed.get(unit,{}).get('team',1))

    def allowed(self,effect,target,owner=1):
        t=effect.get('targeting') or {};side=t.get('affected_team')
        if target is None:return True
        if t.get('ignore_caster') and target==owner:return False
        if side=='friendly' and self.team(target)!=self.team(owner):return False
        if side=='opponent' and self.team(target)==self.team(owner):return False
        definition=self.placed.get(target,{}).get('definition',{})
        labels=set(definition.get('labels',[]));kind='player' if target in (1,2) else (definition.get('data') or {}).get('type')
        if t.get('affected_labels') or t.get('affected_units'):
            if not (labels & set(t.get('affected_labels') or []) or kind in (t.get('affected_units') or [])):return False
        if t.get('caster_owned_only') and target!=owner and self.placed.get(target,{}).get('owner',1)!=owner:return False
        return True

    def spawn_unit(self,ident,position,team=1,device=None,owner=1):
        definition=self.definitions.get(ident);template=self.packets.get('unit-templates',{}).get(ident+'-'+str(team))
        if not definition or not template:return None
        sentinel=pack('fff',101.25,102.5,103.75)
        if template.count(sentinel)!=1:raise ValueError('Unit template position missing')
        unit=self.next_device;self.next_device+=1;self.unit_teams[unit]=team
        self.send((template[:2]+pack('I',unit)+template[6:]).replace(sentinel,pack('fff',*position)))
        hp=((definition.get('health') or {}).get('health') or {}).get('max_health',1)
        self.placed[unit]={'device':device or ident,'definition':definition,'position':tuple(position),'cell':tuple(math.floor(v) for v in position),'health':hp,'created':self.clock(),'team':team,'owner':owner}
        self.send(b'\x06\x09'+pack('I',unit)+b'\x40\x00\x00'+pack('f',hp))
        for effect in (definition.get('init_effects') or [])+(definition.get('enabled_effects') or []):self.add_status(unit,effect,definition.get('lifetime') or 3600,owner=unit,check=False)
        self.event('unit_spawned',unit=unit,key=ident)
        return unit

    def remove_unit(self,unit,trigger=False):
        entry=self.placed.pop(unit,None)
        if not entry:return
        self.statuses.pop(unit,None);self.send(b'\x06\x0a'+pack('I',unit));self.send_loadout()
        definition=entry['definition'];data=definition.get('data') or {}
        if trigger and data.get('type') in ('landmine','bomb') and data.get('trigger_effect'):
            self.apply_effect(data['trigger_effect'],entry['position'],entry['position'],key(definition['_id']),owner=unit)
        if trigger and definition.get('loot'):
            item=definition['loot']['loot_item'].get('item',{})
            if item.get('loot_unit_key'):self.spawn_unit(item['loot_unit_key'],entry['position'],0)

    def teleport(self,point):
        cell=tuple(math.floor(v) for v in point)
        for dy in (0,1,2,-1):
            c=(cell[0],cell[1]+dy,cell[2]);indices=[self.cell_index((c[0],c[1]+h,c[2])) for h in (0,1,2)]
            if all(i is not None and self.blocks[i] in self.passable for i in indices):
                self.position=(c[0]+.5,c[1]+.1,c[2]+.5)
                self.send(b'\x06\x0d'+pack('I',1)+b'\x01\x80'+pack('fff',*self.position));self.event('player_teleported',position=self.position);return True
        return False

    def add_status(self,unit,ident,duration=1,owner=1,check=True):
        if ident not in self.definitions:return
        card=self.definitions[ident]
        if check and not self.allowed(card.get('effect',{}),unit,owner):return
        end=self.clock()+(card.get('duration') or duration)
        self.statuses.setdefault(unit,{})[ident]=end;self.status_owner[unit,ident]=owner

    def buffs_for(self,unit):
        buffs={}
        for ident,end in self.statuses.get(unit,{}).items():
            if end<=self.clock():continue
            effect=self.definitions[ident].get('effect',{})
            if effect.get('type')=='buff':
                for name,value in effect.get('buffs',{}).items():buffs[name]=buffs.get(name,0)+value
        return buffs

    def extra_effect(self,effect,point,origin,source,target,owner=1):
        kind=effect.get('type')
        if kind=='unit_spawn':self.spawn_unit(effect['unit_key'],point,self.team(owner),owner=owner)
        elif kind=='teleport_to':self.teleport(point)
        elif kind=='teleport':
            anchors=[(i,d) for i,d in self.placed.items() if effect['anchor'] in d['definition'].get('labels',[]) and d.get('team',1)==1]
            if anchors:
                unit,d=anchors[-1]
                if self.teleport(tuple(a+b for a,b in zip(d['position'],(0,1,0)))) and effect.get('destroy_anchor'):self.remove_unit(unit)
        elif kind=='heal':
            if target==1:
                self.player_health=min(self.max_health,self.player_health+effect.get('player_heal',0));self.send(b'\x06\x09'+pack('I',1)+b'\x40\x00\x00'+pack('f',self.player_health))
            elif target in self.placed:
                d=self.placed[target];cap=((d['definition'].get('health') or {}).get('health') or {}).get('max_health',1)
                d['health']=min(cap,d['health']+effect.get('world_heal',0));self.send(b'\x06\x09'+pack('I',target)+b'\x40\x00\x00'+pack('f',d['health']))
        elif kind=='heal_blocks':
            cell=tuple(math.floor(v) for v in point)
            if cell in self.block_damage:
                self.block_damage[cell]=max(0,self.block_damage[cell]-effect['heal_amount']);i=self.cell_index(cell);card=self.block_cards.get(self.blocks[i],{})
                self.set_block(cell,self.blocks[i],int(255*self.block_damage[cell]/card['health']['max_health']))
        elif kind=='add_ammo':
            if target in (None,1):
                for gear,(mag,pool) in self.ammo.items():self.ammo[gear][1]=min(self.weapons[gear]['ammo'][0]['pool']['pool_size'],pool+effect['amount'])
                self.send(self.ammo_packet())
        elif kind=='purge':
            self.statuses[target]={i:e for i,e in self.statuses.get(target,{}).items() if not effect.get('positive' if self.definitions[i].get('positive') else 'negative')}
        elif kind=='all_units_bunch':
            for unit in [1,2]+list(self.placed):
                p=self.unit_position(unit)
                if p and math.dist(point,p)<=effect['range'] and self.allowed(effect,unit,owner):
                    child=dict(effect,type='bunch');self.apply_effect(child,p,origin,source,unit,owner)
        elif kind=='fire_mortars':
            mortars=[d for d in self.placed.values() if d['device']=='device_cogwheel_mortar']
            for n,d in enumerate(mortars):
                duration=max(.6,effect.get('base_fire_delay',.5))+n*.2
                self.delayed.append((self.clock()+duration,effect['hit_effect'],point,source))
                shot=self.next_projectile;self.next_projectile+=1
                start=tuple(a+b for a,b in zip(d['position'],(0,1,0)))
                self.mortar_flights[shot]=(self.clock(),duration,start,point)
                transform=b'\xc0\x00'+pack('fffhhh',*start,0,0,0)
                self.send(b'\x06\x34'+pack('Q',shot)+b'\xf0'+key(d['definition']['data']['projectile_key'])+transform+pack('fI',0,1))
                self.impact(d['position'],d['position'],'impact_explosion_mortar_primary')
            self.event('mortars_fired',count=len(mortars))
        else:return False
        return True

    def channel_request(self,packet):
        from practice import Reader
        r=Reader(packet[4:]);flags=r.read('B')
        if flags&192!=192:raise ValueError('Missing channel fields')
        index=r.read('B');point=r.vector()
        block=r.read('hhh') if flags&32 else None;unit=r.read('I') if flags&16 else None;r.end()
        tools=self.weapons[self.current]['tools'];tool=tools[index] if index<len(tools) else {}
        valid=tool.get('type')=='channel' and self.player_respawn_at is None and math.dist(self.position,point)<=tool.get('range',0)+2 and self.clear_line(tuple(a+b for a,b in zip(self.position,(0,1,0))),point,ignore_end=True)
        if unit is not None:valid=valid and self.unit_position(unit) is not None and math.dist(self.unit_position(unit),point)<3
        if block is not None:valid=valid and self.cell_index(block) is not None and math.dist(tuple(v+.5 for v in block),point)<2
        if valid and block is not None:point=tuple(v+.5 for v in block)
        self.send(packet[:4]+bytes([0,valid]))
        if not valid:self.channel=None
        if valid:self.channel={'tool':tool,'gear':self.current,'point':point,'target':unit,'next':self.clock()}
        return True

    def tick_heroes(self):
        now=self.clock();dt=min(.25,max(0,now-self.last_effect_tick));self.last_effect_tick=now
        for shot,(start_time,duration,start,end) in list(self.mortar_flights.items()):
            t=min(1,(now-start_time)/duration);pos=[a+(b-a)*t for a,b in zip(start,end)];pos[1]+=4*4*t*(1-t)
            self.send(b'\x06\x35'+pack('Q',shot)+b'\xc0\x00'+pack('fffhhh',*pos,0,0,0))
            if t>=1:self.send(b'\x06\x36'+pack('Q',shot));self.mortar_flights.pop(shot)
        for due,effect,point,source in list(self.delayed):
            if now>=due:self.delayed.remove((due,effect,point,source));self.apply_effect(effect,point,point,source)
        if self.channel:
            c=self.channel;t=c['tool'];point=c['point']
            if c['gear']!=self.current or self.player_respawn_at is not None or math.dist(self.position,point)>t['range']+2 or not self.clear_line(self.position,point,ignore_end=True):self.channel=None
            elif now>=c['next']:
                cost=(t.get('ammo') or {}).get('rate',0);index=0 if self.current in self.ammo and self.ammo[self.current][0] is not None else 1
                if self.current in self.ammo and self.ammo[self.current][index]<cost:self.channel=None
                else:
                    if self.current in self.ammo:self.ammo[self.current][index]-=cost;self.send(self.ammo_packet())
                    for effect in t.get('interval_effects',[]):self.apply_effect(effect,point,self.position,self.current,c['target'])
                    for ident in t.get('constant_effects') or []:
                        if c['target'] is not None:self.add_status(c['target'],ident,.5)
                    c['next']=now+t.get('interval',.3)
        # Expand auras before computing recipient buffs; preserve the original caster team.
        for unit,statuses in list(self.statuses.items()):
            for ident,end in list(statuses.items()):
                if end<=now:
                    statuses.pop(ident,None);self.status_next.pop((unit,ident),None);continue
                effect=self.definitions[ident].get('effect',{});kind=effect.get('type');origin=self.unit_position(unit)
                if origin is None:continue
                owner=self.status_owner.get((unit,ident),1)
                targets=[unit] if kind in ('self','interval') else [1,2]+list(self.placed) if kind=='aura' else []
                due=now>=self.status_next.get((unit,ident),0)
                for target in targets:
                    pos=self.unit_position(target)
                    if pos is None or kind=='aura' and (not self.allowed(effect,target,owner) or math.dist(origin,pos)>effect.get('outer_radius',0)):continue
                    for child in effect.get('constant_effects') or []:self.add_status(target,child,min(.4,end-now),owner=owner)
                    if due:
                        for child in effect.get('interval_effects') or []:self.apply_effect(child,pos,origin,key(ident),target,owner)
                if due:self.status_next[unit,ident]=now+max(.1,effect.get('interval') or .1)
        for unit in list(self.statuses):
            if unit==1 and self.player_respawn_at is not None or unit==2 and self.target_health<=0:continue
            buffs=self.buffs_for(unit)
            encoded={self.packets.get('buff-ids',{}).get(k):v for k,v in buffs.items() if k in self.packets.get('buff-ids',{})}
            # Send removed buffs as zero values: client updates its existing dictionary.
            previous=self.buff_cache.get(unit,{})
            if encoded!=previous:
                values={k:encoded.get(k,0) for k in previous.keys()|encoded.keys()};self.buff_cache[unit]=encoded
                self.send(b'\x06\x09'+pack('I',unit)+b'\x00\x02\x00'+bytes([len(values)])+b''.join(pack('Bf',k,v) for k,v in values.items()))
            dot=sum(max(0,buffs.get(k,0)) for k in ('bleeding','burning','poisoned','decay'))
            if dot:self.damage_entity(unit,dot*dt,key('effect_status_bleed'))
            if unit==1 and buffs.get('ammo_regen',0)>0:
                for gear,ammo in self.ammo.items():ammo[1]=min(self.weapons[gear]['ammo'][0]['pool']['pool_size'],ammo[1]+buffs['ammo_regen']*dt)
                self.send(self.ammo_packet())
            if unit==1 and self.player_respawn_at is None and buffs.get('health_regen',0)>0:
                self.player_health=min(self.max_health,self.player_health+buffs['health_regen']*dt);self.send(b'\x06\x09'+pack('I',1)+b'\x40\x00\x00'+pack('f',self.player_health))
        for unit,entry in list(self.placed.items()):
            d=entry['definition'];data=d.get('data') or {};age=now-entry['created']
            lifetime=d.get('lifetime') or data.get('timeout')
            if data.get('type')!='bomb' and lifetime and age>=lifetime:self.remove_unit(unit);continue
            if data.get('type')=='pickup' and self.player_respawn_at is None and math.dist(entry['position'],self.position)<1.8:
                self.apply_effect(data['take_effect'],self.position,self.position,key(d['_id']),1);self.remove_unit(unit)

        for target in (1,2):
            if target==1 and self.player_respawn_at is not None or target==2 and self.target_health<=0:continue
            pos=self.unit_position(target);base=tuple(math.floor(v) for v in pos)
            for dy in (0,-1):
                cell=(base[0],base[1]+dy,base[2]);index=self.cell_index(cell)
                if index is None:continue
                card=self.block_cards.get(self.blocks[index],{});special=card.get('special') or {}
                if special.get('type')!='inside_effect':continue
                team=self.blocks[index+3] or self.block_teams.get(cell,0)
                if special.get('trigger_team')=='opponent' and team==self.team(target):continue
                if special.get('trigger_team')=='friendly' and team!=self.team(target):continue
                stamp=(target,cell)
                if now<self.block_contact_at.get(stamp,0):continue
                self.block_contact_at[stamp]=now+max(.1,special.get('interval') or .1)
                wrapped=special.get('interval_effect') or special.get('enter_effect')
                if wrapped:
                    self.apply_effect(wrapped['effect'],pos,pos,key(card['_id']),target,-team if team else -2)
                    if wrapped.get('target_self') and special.get('enter_effect')==wrapped:self.set_block(cell,0)
                for ident in special.get('inside_effects') or []:self.add_status(target,ident,.4,owner=-team if team else -2)
