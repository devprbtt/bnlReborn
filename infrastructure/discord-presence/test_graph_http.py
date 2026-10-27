import json
from pathlib import Path
import tempfile
import threading
import time
import unittest
import urllib.error
import urllib.request
from http.server import ThreadingHTTPServer
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
import graph_http as h
import player_graph as g


class Api:
    def __init__(self):
        self.done = threading.Event()
        self.call = None
    def request(self, *args):
        self.call = args
        self.done.set()
        return {}


class HttpTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.path = Path(self.tmp.name) / 'history.sqlite'
        db = g.database(self.path)
        g.record(db, time.time(), 4)
        db.close()
        self.key = Ed25519PrivateKey.generate()
        self.pub = self.key.public_key().public_bytes_raw().hex()
        self.api = Api()
        self.server = ThreadingHTTPServer(('127.0.0.1', 0), h.make_handler(self.path, self.api, self.pub))
        self.thread = threading.Thread(target=self.server.serve_forever)
        self.thread.start()
        self.url = 'http://127.0.0.1:' + str(self.server.server_port)

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()
        self.tmp.cleanup()

    def post(self, data, valid=True, age=0):
        body = json.dumps(data).encode()
        stamp = str(int(time.time()) - age)
        signature = self.key.sign(stamp.encode() + body).hex() if valid else '00' * 64
        req = urllib.request.Request(self.url+'/discord/player-graph-interactions', data=body,
              headers={'X-Signature-Timestamp':stamp,'X-Signature-Ed25519':signature})
        with urllib.request.urlopen(req) as r:
            return json.load(r)

    def test_signed_ping_invalid_signature_and_stale_request(self):
        self.assertEqual(self.post({'type':1}), {'type':1})
        for options in [{'valid':False}, {'age':600}]:
            with self.assertRaises(urllib.error.HTTPError) as e:
                self.post({'type':1}, **options)
            self.assertEqual(e.exception.code, 401)

    def test_private_ack_then_edit_original_not_shared_message(self):
        data={'type':3,'application_id':g.BOT,'guild_id':g.GUILD,'channel_id':g.CHANNEL,
              'message':{'author':{'id':g.BOT}},'data':{'custom_id':'bnl_graph:30'},'token':'fixture'}
        self.assertEqual(self.post(data), {'type':5,'data':{'flags':64}})
        self.assertTrue(self.api.done.wait(3))
        method,path,payload,png = self.api.call
        self.assertEqual(method,'PATCH')
        self.assertEqual(path,f'webhooks/{g.BOT}/fixture/messages/@original')
        self.assertIn('30 days',payload['content'])
        self.assertTrue(png.startswith(b'\x89PNG'))
        data['guild_id']='other'
        self.assertEqual(self.post(data)['data']['flags'],64)
        self.assertIsNone(h.selection(data))

    def test_public_history_contains_only_aggregate_data(self):
        with urllib.request.urlopen(self.url+'/player-history/data?days=7') as r:
            result=json.load(r)
        self.assertEqual(set(result),{'generated_at','days','samples'})
        self.assertEqual(result['samples'][0][1],4)
        with self.assertRaises(urllib.error.HTTPError):
            urllib.request.urlopen(self.url+'/player-history/data?days=365')


if __name__ == '__main__':
    unittest.main()
