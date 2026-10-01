"""Recovered supply schedule and team rewards for the isolated protocol-65 server."""
import random
import struct
import zlib
from gameclock import millis

def key(name):return struct.pack('<I',zlib.crc32(name.encode()))
def supply_packet(ident=None,deadline=None):
    return b'\x06\x07\x02'+(b'\xc0'+key(ident)+struct.pack('<Q',deadline) if ident is not None else b'\x00')

def supply_effects_packet(unit,effects):
    from server import varint
    return b'\x06\x09'+struct.pack('<I',unit)+b'\x00\x04\x00'+varint(len(effects))+b''.join(key(i)+b'\x01'+struct.pack('<Q',end) for i,end in effects.items())

class SupplySystems:
    def init_supplies(self):
        self.supply_state={'started':False,'index':0,'next':None,'zones':{},'effect_cache':{}}

    def schedule_supply(self,base):
        logic=self.packets['practice'].get('supply_logic') or {}
        first=logic.get('sequence') or [];repeat=logic.get('repeat_sequence') or []
        index=self.supply_state['index']
        item=first[index] if index<len(first) else repeat[(index-len(first))%len(repeat)] if repeat else None
        self.supply_state['next']=None
        if item:
            candidates=[p for p in self.packets['practice'].get('drop_points',[]) if item['drop_point_label'] in self.definitions.get(p['unit_key'],{}).get('labels',[])]
            if candidates:
                marker=random.choice(candidates);pos=tuple(marker['position'][a] for a in 'xyz')
                offset=max(0,logic.get('random_pos_offset',0));pos=(pos[0]+random.uniform(-offset,offset),pos[1],pos[2]+random.uniform(-offset,offset))
                self.supply_state['next']={'item':item,'at':base+max(.1,item['seconds']),'position':pos}
                self.send(supply_packet(item['supply_unit_key'],millis()+int(max(0,base+item['seconds']-self.clock())*1000)))
                return
        self.send(supply_packet())
        if item:
            self.supply_state['next']={'item':item,'at':base+max(.1,item['seconds']),'position':None}
            self.event('supply_unavailable',label=item['drop_point_label'])

    def tick_supplies(self):
        if self.world and (self.world.leader is not self or self.world.finished):return
        if not self.match_started or self.phase_end is not None:return
        now=self.clock();state=self.supply_state
        if not state['started']:
            state['started']=True;self.schedule_supply(now)
        scheduled=state['next']
        if scheduled and now>=scheduled['at']:
            logic=self.packets['practice']['supply_logic'];p=scheduled['position']
            ident=scheduled['item']['supply_unit_key']
            if p is not None:
                unit=self.spawn_unit(ident,(p[0],p[1]+logic.get('spawn_height',25),p[2]),0,owner=0)
                self.event('supply_dropped',key=ident,unit=unit,position=p)
            state['index']+=1;self.schedule_supply(now)
        for team,(end,effects) in list(state['zones'].items()):
            if now>=end:state['zones'].pop(team);continue
            for unit in self.player_units():
                if self.team(unit)==team and self.alive(unit):
                    for ident in effects:
                        self.add_status(unit,ident,end-now,owner=-team)
                        self.statuses[unit][ident]=end

        for unit in self.player_units():
            effects={i:e for i,e in self.statuses.get(unit,{}).items() if e>now and (i.startswith('effect_blockbuster_') or i=='effect_status_shielded_bb')}
            if effects!=state['effect_cache'].get(unit,{}):
                state['effect_cache'][unit]=effects.copy()
                self.send(supply_effects_packet(unit,{i:millis()+int((e-now)*1000) for i,e in effects.items()}))

    def collect_pickup(self,unit,entry,target):
        if unit not in self.placed:return
        # Remove before applying so a second player cannot claim the same pickup.
        self.remove_unit(unit)
        effect=entry['definition'].get('beta_supply_effect') or entry['definition']['data']['take_effect'];pos=self.unit_position(target)
        labels=entry['definition'].get('labels',[])
        if 'supply_blockbuster' in labels:
            team=self.team(target)
            self.supply_state['zones'].pop(team,None)
            for u in self.player_units():
                if self.team(u)==team:
                    for ident in list(self.statuses.get(u,{})):
                        if ident.startswith('effect_blockbuster_') or ident=='effect_status_shielded_bb':self.statuses[u].pop(ident)
        self.apply_effect(effect,pos,pos,key(entry['definition']['_id']),target,owner=target)
        if 'supply_resource' in labels or 'supply_blockbuster' in labels:
            self.event('supply_collected',key=entry['definition']['_id'],player=target,team=self.team(target))

    def supply_effect(self,effect,target,owner):
        kind=effect.get('type')
        if kind=='resource_all':
            players=list(self.world.players.values()) if self.world else [self]
            for p in players:
                side=effect.get('affected_team')
                if side=='friendly' and self.team(p.unit)!=self.team(owner):continue
                if side=='opponent' and self.team(p.unit)==self.team(owner):continue
                if effect.get('ignore_caster_player') and p.unit==owner:continue
                if not effect.get('include_dead_players') and not self.alive(p.unit):continue
                bonus=p.buffs_for(p.unit);amount=effect['resource']*max(0,1+bonus.get('resource_bonus',0)+(bonus.get('supply_resource_bonus',0) if effect.get('supply') else 0))
                p.resources+=amount
                cap=p.packets['practice'].get('resource_cap')
                if cap is not None:p.resources=min(cap,p.resources)
                p.send_resource()
            return True
        if kind=='zone_effect':
            team=self.team(owner);end=self.clock()+effect['duration'];effects=effect.get('effects',[])
            self.supply_state['zones'][team]=(end,effects)
            for u in self.player_units():
                if self.alive(u) and self.allowed(effect,u,owner):
                    for ident in effects:
                        self.add_status(u,ident,effect['duration'],owner=owner)
                        self.statuses[u][ident]=end
            return True
        return False

    def damage_bonus(self,amount,kind,owner=None):
        owner=self.unit if owner is None else owner
        if self.world:owner=self.world.player_owner(owner) or owner
        return amount*max(0,1+self.buffs_for(owner).get(kind,0))
