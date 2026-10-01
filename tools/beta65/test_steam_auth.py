import base64,json,tempfile,unittest
from pathlib import Path
from cryptography.hazmat.primitives import hashes,serialization
from cryptography.hazmat.primitives.asymmetric import rsa,padding
from steam_auth import SteamTickets
from profiles import Profiles

class SteamAuthTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):cls.private=rsa.generate_private_key(public_exponent=65537,key_size=2048)
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();p=Path(self.temp.name)/'public.pem'
        p.write_bytes(self.private.public_key().public_bytes(serialization.Encoding.PEM,serialization.PublicFormat.SubjectPublicKeyInfo))
        self.auth=SteamTickets(p,lambda:1000)
    def tearDown(self):self.temp.cleanup()
    def ticket(self,**changes):
        c=dict(iss='https://auth.blocknload.cc',aud='bnl-beta65-game',sub='76561198000000001',name='Player',jti='a'*32,iat=990,nbf=985,exp=1050);c.update(changes)
        b64=lambda b:base64.urlsafe_b64encode(b).rstrip(b'=').decode()
        raw=b64(b'{"alg":"RS256","typ":"JWT"}')+'.'+b64(json.dumps(c).encode())
        return raw+'.'+b64(self.private.sign(raw.encode(),padding.PKCS1v15(),hashes.SHA256()))
    def test_valid_identity_once_only(self):
        ticket=self.ticket();self.assertEqual(self.auth.verify(ticket)['sub'],'76561198000000001');self.assertIsNone(self.auth.verify(ticket))
    def test_reject_wrong_game_expired_future_and_bad_signature(self):
        for c in ({'aud':'bnl-reborn-game'},{'exp':999},{'nbf':1001},{'iat':1006},{'exp':2000},{'sub':'arbitrary'},{'iss':'https://evil.test'}):
            self.assertIsNone(self.auth.verify(self.ticket(**c)))
        self.assertIsNone(self.auth.verify(self.ticket()[:-10]+'AAAAAAAAAA'))
        self.assertIsNone(self.auth.verify('not-a-ticket'))
    def test_steam_identity_cannot_claim_existing_local_name(self):
        store=Profiles();local=store.login('Player','password123')
        steam=store.steam_login('76561198000000001','Player')
        self.assertNotEqual(local['id'],steam['id'])
        self.assertEqual(store.steam_login('76561198000000001','Different')['id'],steam['id'])
        self.assertIsNone(store.login(steam['name'],'password123'))
        store.db.close()

if __name__=='__main__':unittest.main()
