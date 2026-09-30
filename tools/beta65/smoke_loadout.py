"""Opt-in original-player smoke for loadout, F, rocket and grenade rendering."""
from pathlib import Path
import sys
import server
from practice import Practice,pack
from loadout import key

class Smoke(Practice):
    def __init__(self,*a,**kw):
        super().__init__(*a,**kw);self.started=self.clock();self.stage=0
    def tick(self):
        super().tick();elapsed=self.clock()-self.started
        if elapsed>4 and self.stage==0:
            self.stage=1
            for definition,cell in zip(self.loadout.values(),[(17,4,22),(17,4,24),(18,4,24),(14,4,21),(15,4,21),(19,4,25)]):self.complete_build(cell,definition)
            self.event('loadout_spawn_smoke')
        if elapsed>7 and self.stage==1:
            self.stage=2
            normal=self.send;self.send=lambda p: normal(p) if p[:2]!=b'\x06\x2f' else None
            try:self.cast_ability(b'\x06\x2f\x01\x00\xe0'+key(self.ability['_id'])+pack('fff',14.5,5.5,23.5)+b'\x01'+pack('fff',18.5,5,23.5)+b'\x01'+pack('Q',9001))
            finally:self.send=normal
            self.send((packet_dir/'smoke-projectile_sarge_stone_frag_grenade.bin').read_bytes())
        if elapsed>8 and self.stage==2:
            self.stage=3
            rocket=key('gear_sarge_stone_rocket_launcher');self.switch(rocket);self.send(self.packets['equipment'][rocket])
            self.handle(b'\x06\x1e\xe0\x00'+pack('fff',14.5,5.5,23.5)+b'\x01'+pack('fff',18.5,5,23.5)+b'\x01'+pack('Q',9002))
            self.send((packet_dir/'smoke-projectile_sarge_rpg_rocket.bin').read_bytes())
            self.handle(b'\x06\x1f\x01'+pack('Q',9002)+b'\xe8'+pack('fffBhhhI',18.5,5,23.5,0,0,0,0,2))
        if elapsed>10 and self.stage==3:
            self.stage=4
            self.handle(b'\x06\x1f\x01'+pack('Q',9001)+b'\xe0'+pack('fffBhhh',18.5,4.2,23.5,0,0,0,0))
        if elapsed>18 and self.stage==4:
            self.stage=5;self.move((14.5,-4,23.5))
        if elapsed>25 and self.stage==5:
            self.stage=6;self.event('loadout_smoke_completed',charges=self.charges,spawn_position=self.spawn_position)

if __name__=='__main__':
    packet_dir=Path(sys.argv[sys.argv.index('--packets')+1]);server.Practice=Smoke;server.main()
