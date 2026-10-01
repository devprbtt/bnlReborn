"""Original protocol-65 solo room and loadout flow; no public matchmaking."""
import copy
import io
import struct
import time
from gameclock import millis
from loadout import key, pack
import map_voting


class Lobby:
    def __init__(self, packets, send_region, enter_instance, event):
        self.packets, self.send_region, self.enter_instance, self.event = packets, send_region, enter_instance, event
        self.player_id=1;self.nickname='BetaLocal';self.level=1;self.team=1;self.social=None;self.group=None;self.world=None;self.ready=False
        self.config = packets['lobby']
        self.hero = self.config['hero']
        self.skin = self.config['skin']
        self.heroes = {key(h['id']):h for h in self.config.get('heroes', [])}
        self.available = {key(d['_id']): d for d in self.config['devices']}
        self.defaults = {i: key(d['_id']) for i, d in enumerate(packets['practice']['loadout'], 1)}
        self.devices = self.defaults.copy()
        self.state = 'menu'
        self.name = 'Local practice'
        self.password = ''
        self.send_instance = None
        self.zone_initialized = False
        self.map_id = 'beta_practice_map'
        self.maps = {key(m['id']):m for m in packets.get('maps',[])}
        self.selection_start = 0
        self.selection_end = 0

    def allowed_devices(self):
        hero = self.heroes.get(key(self.hero), {})
        allowed = hero.get('available_devices')
        return self.available if allowed is None else {key(d):self.available[key(d)] for d in allowed if key(d) in self.available}

    def special_devices(self):
        return {key(d) for d in self.heroes.get(key(self.hero), {}).get('special_devices', [])}

    def family(self, device):
        card = self.available.get(device, {})
        return card.get('beta_base_device', card.get('_id', device))

    def valid_loadout(self):
        allowed = self.allowed_devices(); specials = self.special_devices()
        return (set(self.devices) == set(range(1,7))
                and all(d in allowed for d in self.devices.values())
                and len({self.family(d) for d in self.devices.values()}) == 6
                and (not specials or self.devices.get(6) in specials
                     and all(d not in specials for slot,d in self.devices.items() if slot != 6)))

    def player_state(self):
        from server import string, varint
        # PlayerLobbyState: all mandatory fields, absent SteamId.
        player = b'\xbf\xff\xc0' + pack('I', self.player_id) + string(self.nickname) + pack('ii', self.level, 0) + bytes([0,self.team])
        allowed = self.allowed_devices()
        player += key(self.hero) + varint(len(allowed)) + b''.join(allowed)
        skins = self.heroes.get(key(self.hero),{}).get('skins',[self.skin])
        player += varint(len(skins)) + b''.join(key(s) for s in skins) + varint(len(self.devices))
        player += b''.join(pack('i', slot) + device for slot, device in sorted(self.devices.items()))
        player += b'\x00\x00' + key(self.skin) + bytes([self.ready, 1, 1, 0])
        return player

    def update(self):
        from server import varint
        players=self.group['members'] if self.group else [self]
        player=b''.join(p.player_state() for p in players)
        timer = b'\xe0\x02' + pack('QQ', self.selection_start, self.selection_end)
        ballot=self.group.get('map_vote') if self.group else None
        candidates=ballot['candidates'] if map_voting.active(self.group) else [self.map_id]
        maps=varint(len(candidates))
        for ident in candidates:
            voters=sorted(p for p,m in ballot['votes'].items() if m==ident) if ballot else []
            maps+=b'\xc0'+key(ident)+varint(len(voters))+b''.join(pack('I',p) for p in voters)
        return b'\x09\x00\xf8' + key('beta_practice_match' if self.map_id=='beta_practice_map' else 'beta_lan_match') + maps + b'\x00' + timer + varint(len(players)) + player

    def room_update(self):
        from server import string
        settings = b'\xf8' + key(self.map_id) + pack('ff??', self.maps.get(key(self.map_id),{}).get('build_seconds',120), 1, False, False)
        from server import varint
        players=self.group['members'] if self.group else [self]
        player=b''.join(b'\xbf\x80'+pack('I',p.player_id)+string(p.nickname)+pack('ii',p.level,0)+bytes([0,int(self.group is None or self.group['owner'] is p),p.team,0]) for p in players)
        return b'\x0b\x0f\xf0' + string(self.name) + string(self.password) + settings + varint(len(players)) + player

    def open_lobby(self):
        self.state = 'lobby'
        self.ready=False
        self.zone_initialized = False
        self.selection_start = millis()
        self.selection_end = self.selection_start + 120000
        if map_voting.active(self.group):
            self.selection_start=self.group['map_vote']['start'];self.selection_end=self.group['map_vote']['end']
        self.enter_instance(self, 'lobby')
        self.event('lobby_opened')

    def tick(self, send):
        if self.social:
            with self.social.lock:return self._tick(send)
        return self._tick(send)

    def _tick(self, send):
        if self.group:map_voting.tick(self.group)
        if map_voting.active(self.group):return
        if self.state == 'lobby' and self.selection_end and millis() >= self.selection_end:
            if not self.valid_loadout():
                self.devices = self.defaults.copy()
            self.handle_instance(b'\x09\x0a',send)

    def notice(self, text):
        from server import string
        self.send_region(b'\x02\x03' + string(text) + b'\x00')

    def handle_region(self, packet):
        from server import read_string
        service, fn = packet[:2]
        if service != 11: return False
        if fn == 6:
            self.send_region(packet[:4] + b'\x00\x00')  # No other players on loopback.
        elif fn == 9 and self.state in ('menu', 'room'):
            reader = io.BytesIO(packet[2:]); name, password = read_string(reader), read_string(reader)
            if reader.read() or not name.strip() or len(name) > 80 or len(password) > 80: raise ValueError('Invalid room')
            self.name, self.password, self.state = name, password, 'room'
            self.send_region(self.room_update()); self.event('custom_room_created')
        elif fn == 10 and self.state == 'room': self.open_lobby()
        elif fn == 0 and self.state == 'menu':
            if packet[2:] == key('beta_menu_friendly'): self.open_lobby()
        elif fn == 11 and self.state == 'room':
            self.state = 'menu'; self.password = ''; self.send_region(b'\x0b\x11')
        elif fn == 12 and self.state == 'room' and len(packet)==7 and packet[2]==0x80:
            requested=packet[3:]
            if requested==key('beta_practice_map') or requested in self.maps:
                self.map_id='beta_practice_map' if requested==key('beta_practice_map') else self.maps[requested]['id']
                self.event('practice_map_selected',map=self.map_id)
            self.send_region(self.room_update())
        elif fn == 13 and self.state == 'room':
            if self.map_id=='beta_practice_map':
                self.notice('Choose a two-team map before switching sides.');return True
            self.team=3-self.team;self.send_region(self.room_update())
        elif fn == 12 and self.state == 'room':
            self.send_region(self.room_update())
            self.notice('Local practice currently uses one player and fixed match rules. Choose a map with the map arrows.')
        elif fn in (7, 19): self.send_region(packet[:4] + b'\x00\x05')
        elif fn == 8: self.send_region(packet[:4] + b'\x00\x06')
        elif fn not in (1, 2, 3, 14, 18): return False
        return True

    def handle_instance(self, packet, send):
        if self.social:
            with self.social.lock:return self._handle_instance(packet,send)
        return self._handle_instance(packet,send)

    def _handle_instance(self, packet, send):
        service, fn = packet[:2]
        if (service, fn) == (6, 5) or service == 9 and fn in (15, 16):
            self.state = 'menu'; self.password = ''
            send(b'\x09\x01'); self.send_region(b'\x0b\x11'); self.send_region(self.packets['scene'])
            return True
        if service != 9: return False
        if fn == 11: return True
        if self.state != 'lobby': return True
        if fn==9:
            if len(packet)==6:map_voting.vote(self,packet[2:])
            return True
        if self.group:map_voting.tick(self.group)
        if map_voting.active(self.group) and fn==10:return True
        if self.ready and fn!=10:return True
        data = packet[2:]
        if fn == 2:
            if data in self.heroes:
                hero = self.heroes[data]
                self.hero = hero['id']; self.skin = hero['skins'][0]
                self.defaults = {i:key(d) for i,d in enumerate(hero['defaults'],1)}
                self.devices = self.defaults.copy()
                self.event('lobby_hero_selected',hero=self.hero)
            elif data != key(self.hero): return True
        elif fn == 3 and len(data) == 8:
            device, slot = data[:4], struct.unpack('<i', data[4:])[0]
            if device not in self.allowed_devices() or slot not in range(1, 7): return True
            specials = self.special_devices()
            if specials and ((slot == 6) != (device in specials)): return True
            self.devices = {s:d for s,d in self.devices.items() if self.family(d) != self.family(device)}
            self.devices[slot] = device
        elif fn == 4 and len(data) == 4:
            slot = struct.unpack('<i', data)[0]
            if slot == 6 and self.special_devices(): return True
            self.devices.pop(slot, None)
        elif fn == 5 and len(data) == 8:
            a, b = struct.unpack('<ii', data)
            if a not in range(1, 7) or b not in range(1, 7): return True
            if self.special_devices() and 6 in (a,b): return True
            av, bv = self.devices.pop(a, None), self.devices.pop(b, None)
            if av is not None: self.devices[b] = av
            if bv is not None: self.devices[a] = bv
        elif fn == 6: self.devices = self.defaults.copy()
        elif fn == 8:
            skins = self.heroes.get(key(self.hero),{}).get('skins',[self.skin])
            skin = next((s for s in skins if key(s)==data),None)
            if skin is None:return True
            self.skin = skin;self.event('lobby_skin_selected',skin=skin)
        elif fn == 10:
            if self.group and len(self.group['members'])>1:
                if not self.valid_loadout():return True
                self.ready=True
                for member in self.group['members']:
                    if member.send_instance:member.send_instance(member.update())
                if not all(m.ready for m in self.group['members']):return True
                if self.group.get('world'):return True
                from shared_world import World
                world=World(self.group['members'],self.event,self.social.profiles if self.social else None,self.social)
                self.group['world']=world
                for member in self.group['members']:
                    member.state='zone';member.send_region(member.map_packet('terrain-scene'))
                return True
            if not self.valid_loadout(): return True
            self.state = 'zone'
            self.send_region(self.map_packet('terrain-scene'))
            self.event('lobby_ready', map=self.map_id, hero=self.hero, skin=self.skin, devices=[self.available[d]['_id'] for _,d in sorted(self.devices.items())])
            return True
        elif fn not in (7, 9): return False
        if self.group:
            for member in self.group['members']:
                if member.send_instance:member.send_instance(member.update())
        else:send(self.update())
        return True

    def lobby_scene(self):
        packet=self.packets['lobby-scene']
        if self.team==2 and len(packet)==9:
            packet=packet[:4]+bytes([self.team])+packet[5:]
        return packet

    def map_packet(self, name):
        packet=self.packets.get('map-packets',{}).get(self.map_id,{}).get(name,self.packets.get(name))
        if name=='zone-init' and self.group and self.group.get('friendly'):packet=packet[:-1]+b'\x00'
        if name=='terrain-scene' and self.team==2 and len(packet)==14:
            packet=packet[:-2]+bytes([self.team])+packet[-1:]
        return packet

    def practice_packets(self):
        packets = self.packets.copy()
        packets['practice'] = copy.deepcopy(packets['practice'])
        packets['practice']['team']=self.team
        packets['practice']['nickname']=self.nickname
        packets['practice']['player_id']=self.player_id
        packets['practice']['loadout'] = [self.available[d] for _, d in sorted(self.devices.items())]
        if self.heroes:
            hero = self.heroes[key(self.hero)]
            packets['hero-create'] = self.packets['skin-packets'][self.skin]
            packets['hero-state'] = self.packets['hero-states'][self.hero]
            packets['practice']['max_health'] = hero['health']
            packets['practice']['current_gear'] = hero['gears'][1]
            packets['practice']['weapons'] = [w for w in self.config['weapons'] if w['id'] in hero['gears']]
            packets['practice']['ability'] = next(a for a in self.config['abilities'] if a['_id']==hero['ability'])
            packets['practice']['unit_devices'] = self.config['unit_devices']
        if self.map_id != 'beta_practice_map':
            m=copy.deepcopy(self.maps[key(self.map_id)])
            if self.team==2:
                m['spawn_position']=m['team_spawns']['team2']
            packets.update(self.packets['map-packets'][self.map_id])
            packets['practice'].update({k:m[k] for k in ('spawn_position','target_position','kill_height','water_level','min_fall_height','max_fall_height','build_seconds','respawn_seconds') if k in m})
            packets['practice']['objectives']=m.get('objectives',[])
            packets['practice']['drop_points']=m.get('drop_points',[])
            for name,old,new in [('hero-create',(14.5,5,23.5),m['spawn_position']),('target-create',(18.5,4,23.5),m['target_position'])]:
                before=pack('fff',*old)
                if packets[name].count(before)!=1:raise ValueError('Spawn template mismatch: '+name)
                packets[name]=packets[name].replace(before,pack('fff',*new))
        if self.map_id == 'beta_practice_map' and 'hero-create' in packets:
            packets['hero-create']=packets['hero-create'].replace(pack('fff',14.5,5,23.5),pack('fff',*packets['practice'].get('spawn_position',(14.5,5,23.5))))
        from wire_units import set_identity
        for name,team,pid in [("hero-create",self.team,self.player_id),("target-create",3-self.team,0)]:
            if name in packets:packets[name]=set_identity(packets[name],team,pid)
        packets["device-templates"]={k:set_identity(v,self.team) for k,v in packets.get("device-templates",{}).items()}
        return packets
