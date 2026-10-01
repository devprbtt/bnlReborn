import copy
import struct
import unittest
from types import SimpleNamespace
import test_practice
from shared_world import World
from profiles import Profiles,Social
from practice import pack
from loadout import key

class SharedWorldTests(unittest.TestCase):
    def setUp(self):
        fixture=test_practice.PracticeTests();fixture.setUp();self.base=fixture.packets;self.clock=[0];self.store=Profiles();self.social=Social(self.store)
        self.rooms=[];self.sent={};self.events=[];group={'id':77,'friendly':True}
        for i,team in enumerate((1,1,2,2),1):
            profile=self.store.login('Player'+str(i),'test-password');p=copy.deepcopy(self.base)
            p['practice'].update(spawn_position=(1+i,1,2),team=team,player_id=profile['id'],build_seconds=1,respawn_seconds=2,kill_height=-10)
            hero=bytearray.fromhex('060801000000f6a66ec0d8ff80000068410000a0400000bc41000084030000000000000000000000000000010100000001c3c0517f')
            struct.pack_into('<fff',hero,13,*p['practice']['spawn_position']);struct.pack_into('<I',hero,44,profile['id']);hero[-5]=team
            p['hero-create']=bytes(hero);p['hero-state']=b'\x06\x09'+pack('I',1)+b'\x40\x00\x00'+pack('f',160)
            room=SimpleNamespace(player_id=profile['id'],nickname=profile['name'],team=team,group=group,hero='sarge',skin='skin',state='zone',sent=[],packets={'scene':b'\x02\x00\x01'},send_instance=None)
            room.practice_packets=lambda p=p:copy.deepcopy(p);room.send_region=room.sent.append;room.notice=lambda t,r=room:r.sent.append(t)
            self.rooms.append(room)
        self.w=World(self.rooms,lambda k,**v:self.events.append((k,v)),self.store,self.social,lambda:self.clock[0])
        for room in self.rooms:
            self.sent[room.practice.unit]=[];self.w.attach(room,self.sent[room.practice.unit].append)
        self.a=self.w.players[1];self.enemy=self.w.players[3]
    def tearDown(self):self.store.db.close()
    def assault(self):self.clock[0]=1.1;self.w.tick()
    def test_spawn_control_is_per_recipient_and_no_dummy(self):
        for recipient,packets in self.sent.items():
            spawns=[p for p in packets if p[:2]==b'\x06\x08']
            self.assertEqual(len(spawns),4)
            for p in spawns:self.assertEqual(bool(p[43]),struct.unpack_from('<I',p,2)[0]==recipient)
        self.assertIs(self.a.blocks,self.enemy.blocks);self.assertIs(self.a.placed,self.enemy.placed)
    def test_enemy_damage_death_respawn_and_friendly_fire(self):
        self.a.damage_entity(3,80,self.a.current);self.assertEqual(self.enemy.player_health,160)
        self.assault();self.a.damage_entity(2,80,self.a.current);self.assertEqual(self.w.players[2].player_health,160)
        self.a.damage_entity(3,160,self.a.current);self.assertEqual(self.enemy.player_health,0);self.assertEqual(self.a.kills,1)
        self.clock[0]=2;self.w.tick();self.assertEqual(self.enemy.player_health,0)
        self.clock[0]=3.2;self.w.tick();self.assertEqual(self.enemy.player_health,160)
        for packets in self.sent.values():self.assertTrue(any(p[:6]==b'\x06\x08'+pack('I',3) for p in packets))
    def test_respawn_restores_only_dead_players_ammo_and_replicates(self):
        self.assault()
        self.a.ammo[self.a.current]=[1,2];self.enemy.ammo[self.enemy.current]=[0,0]
        self.a.damage_entity(3,160,self.a.current)
        self.clock[0]=3.2;self.w.tick()
        self.assertEqual(self.enemy.ammo[self.enemy.current],[2,4])
        self.assertEqual(self.a.ammo[self.a.current],[1,2])
        for packets in self.sent.values():self.assertIn(self.enemy.ammo_packet(),packets)

    def test_hits_use_target_player_health_and_ammo_and_cannot_replay(self):
        self.assault();origin=self.a.position;target=self.enemy.position
        self.w.handle(self.rooms[0],b'\x06\x1e\xe0\x00'+pack('fff',*origin)+b'\x01'+pack('fff',*target)+b'\x01'+pack('Q',42))
        hit=b'\x06\x1f\x01'+pack('Q',42)+b'\xe8'+pack('fffBhhhI',*target,0,0,0,0,3)
        self.w.handle(self.rooms[0],hit);self.w.handle(self.rooms[0],hit)
        self.assertEqual(self.enemy.player_health,80);self.assertEqual(self.a.ammo[self.a.current][0],1)
        self.assertTrue(any(p[:2]==b'\x06\x11' for p in self.sent[3]))
    def test_shared_blocks_and_unique_device_allocator(self):
        self.a.set_block((2,0,2),1);self.assertEqual(self.enemy.blocks[self.enemy.cell_index((2,0,2))],1)
        n=self.a.next_device;self.a.next_device+=1;self.assertEqual(self.enemy.next_device,n+1)
        self.assertTrue(any(p[:2]==b'\x06\x06' for p in self.sent[3]))
    def test_movement_broadcast_rejects_other_unit_and_dead_movement(self):
        packet=b'\x06\x0f'+pack('I',3)+bytes(8)+b'\xff\x80'+pack('fff',5,1,2)+bytes(18)
        self.w.handle(self.rooms[2],packet);self.assertEqual(self.enemy.position,(5,1,2))
        self.assertTrue(any(p[:6]==b'\x06\x0b'+pack('I',3) for p in self.sent[1]))
        with self.assertRaises(ValueError):self.w.handle(self.rooms[0],packet)
    def test_team_and_all_chat_scope(self):
        from server import string
        self.w.handle(self.rooms[0],b'\x07\x07'+self.a.chat_room(1)+string('team'))
        self.assertTrue(self.rooms[1].sent[-1].endswith(string('team')))
        self.assertFalse(any(isinstance(p,bytes) and p.endswith(string('team')) for p in self.rooms[2].sent))
        self.w.handle(self.rooms[0],b'\x07\x07'+self.a.chat_room(0)+string('all'))
        self.assertTrue(self.rooms[2].sent[-1].endswith(string('all')))
    def objectives(self):
        for n,labels in enumerate((['objective','line_1'],['objective','line_2'],['objective','base']),100):
            self.a.placed[n]={'definition':{'labels':labels,'health':{'health':{'max_health':10}}},'health':10,'team':2,'position':(6,1,2),'owner':0}
            self.a.unit_teams[n]=2
    def test_objective_order_completion_awards_once_and_updates_level(self):
        self.assault();self.objectives()
        self.a.damage_entity(102,100,self.a.current);self.assertEqual(self.a.placed[102]['health'],10)
        self.a.damage_entity(100,100,self.a.current);self.a.damage_entity(101,100,self.a.current);self.a.damage_entity(102,100,self.a.current)
        self.assertTrue(self.w.finished);self.assertEqual(self.store.get(1)['xp'],750);self.assertEqual(self.store.get(3)['xp'],500)
        self.w.finish(1);self.assertEqual(self.store.get(1)['xp'],750)
        self.assertTrue(any(p[:2]==b'\x06\x04' for p in self.sent[1]))
    def test_custom_never_awards_xp(self):
        self.assault();self.w.friendly=False;self.w.finish(1);self.assertEqual(self.store.get(1)['xp'],0)
    def test_empty_team_aborts_without_xp(self):
        self.w.detach(self.rooms[2]);self.assertFalse(self.w.finished)
        self.w.detach(self.rooms[3]);self.assertTrue(self.w.finished);self.assertEqual(self.store.get(1)['xp'],0)
    def test_heal_and_root_expiry_on_nonleader_player(self):
        self.assault();self.a.damage_entity(3,80,self.a.current)
        self.a.apply_effect({'type':'heal','player_heal':20},self.enemy.position,self.a.position,self.a.current,3)
        self.assertEqual(self.enemy.player_health,100)
        self.a.definitions['root']={'duration':1,'effect':{'type':'buff','buffs':{'root':1}}}
        self.a.add_status(3,'root',owner=1);self.assertEqual(self.enemy.buffs_for(3)['root'],1)
        self.clock[0]=3;self.w.tick();self.assertEqual(self.enemy.buffs_for(3),{})



    def test_disconnected_leader_transfers_world_ticks_and_chat_survives_group_leave(self):
        self.w.detach(self.rooms[0]);self.rooms[0].group=None;self.rooms[0].world=None;self.rooms[0].practice=None
        self.assertFalse(self.w.finished);self.assertEqual(self.w.leader.unit,2)
        self.clock[0]=2;self.w.tick();self.assertIsNone(self.w.state['phase_end'])
        from server import string
        self.w.handle(self.rooms[1],b'\x07\x07'+self.w.players[2].chat_room(0)+string('still here'))
        self.assertTrue(self.rooms[2].sent[-1].endswith(string('still here')))
        self.w.detach(self.rooms[0]) # stale socket cleanup is idempotent

    def test_equipment_update_uses_current_gear_flag(self):
        self.w.handle(self.rooms[0],b'\x06\x20\x01\x00'+self.a.current)
        self.assertIn(b'\x06\x09'+pack('I',1)+b'\x01\x00\x00'+self.a.current,self.sent[3])

    def test_finished_world_freezes_gameplay_and_results_include_every_profile(self):
        self.assault();self.w.finish(1);hp=self.enemy.player_health
        self.a.damage_entity(3,100,self.a.current);self.assertEqual(hp,self.enemy.player_health)
        result=next(p for p in self.sent[1] if p[:2]==b'\x06\x04')
        self.assertEqual(result[8],4) # PlayersData count after header/flags/seconds
        for pid in range(1,5):self.assertIn(b'\xf8'+pack('I?',pid,False)+b'\xff',result)

    def test_consecutive_real_finishes_advance_player_level(self):
        self.w.finish(1)
        second=World(self.rooms,lambda *a,**k:None,self.store,self.social,lambda:self.clock[0])
        for room in self.rooms:second.attach(room,lambda p:None)
        second.finish(1)
        self.assertEqual(self.store.get(1)['xp'],1500);self.assertEqual(self.store.get(1)['level'],2)
        self.assertIsNot(self.w.state['blocks'],second.state['blocks'])

    def test_closed_result_socket_does_not_prevent_other_results(self):
        def closed(packet):raise OSError('closed')
        self.w.connections[1]=closed
        self.w.finish(1)
        self.assertTrue(any(p[:2]==b'\x06\x04' for p in self.sent[3]))
        self.assertEqual(self.store.get(3)['xp'],500)
