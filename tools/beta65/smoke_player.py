"""Opt-in scripted protocol smoke for the recovered player; no real mouse input claimed.
Run with server.py's --packets/--events/--terrain-test arguments. Never used by launcher.
"""
import server
from practice import Practice, pack

class ScriptedPractice(Practice):
    def __init__(self,*a,**kw):
        super().__init__(*a,**kw)
        self.smoke_start=self.clock(); self.smoke_shot=0;self.smoke_reload=False;self.smoke_stage=0
    def tick(self):
        super().tick()
        elapsed=self.clock()-self.smoke_start
        if elapsed>4 and self.smoke_shot<30 and self.clock()-self.last_cast>.16:
            self.smoke_shot+=1
            origin=tuple(a+b for a,b in zip(self.position,(0,1.4,0)))
            target=tuple(a+b for a,b in zip(self.target_position,(0,1,0)))
            self.handle(b'\x06\x1e\xe0\x00'+pack('fff',*origin)+b'\x01'+pack('fff',*target)+b'\x01'+pack('Q',self.smoke_shot))
            self.handle(b'\x06\x1f\x01'+pack('Q',self.smoke_shot)+b'\xe8'+pack('fffBhhhI',*target,0,0,0,0,2))
        if self.smoke_shot==30 and not self.smoke_reload:
            self.smoke_reload=True
            normal_send=self.send
            self.send=lambda packet: normal_send(packet) if packet[:2]!=b'\x06\x21' else None
            try: self.handle(b'\x06\x21\xfe\xff')
            finally: self.send=normal_send
        if elapsed>16 and self.smoke_stage==0:
            self.smoke_stage=1
            key=self.packets['keys']['gear_sarge_stone_shovel'];self.switch(key)
            self.send(self.packets['equipment'][key])
            request=b'\x06\x38\xfd\xff\xf8\x01'+self.packets['brick-key']+pack('ffffff?',17.5,3.999,23.5,17.5,4.001,23.5,True)
            normal_send=self.send;self.send=lambda p:normal_send(p) if p[:2]!=b'\x06\x38' else None
            try:self.handle(request)
            finally:self.send=normal_send
        if elapsed>20 and self.smoke_stage==1:
            self.smoke_stage=2
            tool=self.weapons[self.current]['tools'][0]
            for _ in range(8):self.damage_block((17.001,4.5,23.5),tool,(14.5,5.5,23.5))
        if elapsed>22 and self.smoke_stage==2:
            self.smoke_stage=3;self.move((14.5,-4,23.5))
        if elapsed>27 and self.smoke_stage==3:
            self.smoke_stage=4;self.smoke_shot=31;self.event('scripted_smoke_completed',kills=self.kills)

if __name__=='__main__':
    server.Practice=ScriptedPractice
    server.main()
