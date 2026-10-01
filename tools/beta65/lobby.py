"""Original protocol-65 solo room and loadout flow; no public matchmaking."""
import copy
import io
import struct
import time
from gameclock import millis
from loadout import key, pack


class Lobby:
    def __init__(self, packets, send_region, enter_instance, event):
        self.packets, self.send_region, self.enter_instance, self.event = packets, send_region, enter_instance, event
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

    def update(self):
        from server import string, varint
        # PlayerLobbyState: all mandatory fields, absent SteamId.
        player = b'\xbf\xff\xc0' + pack('I', 1) + string('BetaLocal') + pack('ii', 1, 0) + b'\x00\x01'
        player += key(self.hero) + varint(len(self.available)) + b''.join(self.available)
        skins = self.heroes.get(key(self.hero),{}).get('skins',[self.skin])
        player += varint(len(skins)) + b''.join(key(s) for s in skins) + varint(len(self.devices))
        player += b''.join(pack('i', slot) + device for slot, device in sorted(self.devices.items()))
        player += b'\x00\x00' + key(self.skin) + bytes([0, 1, 1, 0])
        timer = b'\xe0\x02' + pack('QQ', self.selection_start, self.selection_end)
        return b'\x09\x00\xf8' + key('beta_practice_match') + b'\x01\xc0' + key(self.map_id) + b'\x00\x00' + timer + b'\x01' + player

    def room_update(self):
        from server import string
        settings = b'\xf8' + key(self.map_id) + pack('ff??', self.maps.get(key(self.map_id),{}).get('build_seconds',120), 1, False, False)
        player = b'\xbf\x80' + pack('I', 1) + string('BetaLocal') + pack('ii', 1, 0) + bytes([0, 1, 1, 0])
        return b'\x0b\x0f\xf0' + string(self.name) + string(self.password) + settings + b'\x01' + player

    def open_lobby(self):
        self.state = 'lobby'
        self.zone_initialized = False
        self.selection_start = millis()
        self.selection_end = self.selection_start + 120000
        self.enter_instance(self, 'lobby')
        self.event('lobby_opened')

    def tick(self, send):
        if self.state == 'lobby' and self.selection_end and millis() >= self.selection_end:
            if set(self.devices) != set(range(1,7)):
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
        elif fn in (12, 13) and self.state == 'room':
            self.send_region(self.room_update())
            self.notice('Local practice currently uses one player and fixed match rules. Choose a map with the map arrows.')
        elif fn in (7, 19): self.send_region(packet[:4] + b'\x00\x05')
        elif fn == 8: self.send_region(packet[:4] + b'\x00\x06')
        elif fn not in (1, 2, 3, 14, 18): return False
        return True

    def handle_instance(self, packet, send):
        service, fn = packet[:2]
        if (service, fn) == (6, 5) or service == 9 and fn in (15, 16):
            self.state = 'menu'; self.password = ''
            send(b'\x09\x01'); self.send_region(b'\x0b\x11'); self.send_region(self.packets['scene'])
            return True
        if service != 9: return False
        if fn == 11: return True
        if self.state != 'lobby': return True
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
            if device not in self.available or slot not in range(1, 7): return True
            self.devices = {s:d for s,d in self.devices.items() if d != device}
            self.devices[slot] = device
        elif fn == 4 and len(data) == 4:
            self.devices.pop(struct.unpack('<i', data)[0], None)
        elif fn == 5 and len(data) == 8:
            a, b = struct.unpack('<ii', data)
            if a not in range(1, 7) or b not in range(1, 7): return True
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
            if set(self.devices) != set(range(1, 7)): return True
            self.state = 'zone'
            self.send_region(self.map_packet('terrain-scene'))
            self.event('lobby_ready', map=self.map_id, hero=self.hero, skin=self.skin, devices=[self.available[d]['_id'] for _,d in sorted(self.devices.items())])
            return True
        elif fn not in (7, 9): return False
        send(self.update())
        return True

    def map_packet(self, name):
        return self.packets.get('map-packets',{}).get(self.map_id,{}).get(name,self.packets.get(name))

    def practice_packets(self):
        packets = self.packets.copy()
        packets['practice'] = copy.deepcopy(packets['practice'])
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
            m=self.maps[key(self.map_id)]
            packets.update(self.packets['map-packets'][self.map_id])
            packets['practice'].update({k:m[k] for k in ('spawn_position','target_position','kill_height','water_level','min_fall_height','max_fall_height','build_seconds','respawn_seconds') if k in m})
            packets['practice']['objectives']=m.get('objectives',[])
            for name,old,new in [('hero-create',(14.5,5,23.5),m['spawn_position']),('target-create',(18.5,4,23.5),m['target_position'])]:
                before=pack('fff',*old)
                if packets[name].count(before)!=1:raise ValueError('Spawn template mismatch: '+name)
                packets[name]=packets[name].replace(before,pack('fff',*new))
        if self.map_id == 'beta_practice_map' and 'hero-create' in packets:
            packets['hero-create']=packets['hero-create'].replace(pack('fff',14.5,5,23.5),pack('fff',*packets['practice'].get('spawn_position',(14.5,5,23.5))))
        return packets
