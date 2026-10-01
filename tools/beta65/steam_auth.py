"""Public beta login: RS256 tickets from the existing Steam web identity service."""
import base64
import json
import re
import threading
import time
from pathlib import Path
from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives import hashes,serialization
from cryptography.hazmat.primitives.asymmetric import padding,rsa


class SteamTickets:
    def __init__(self,public_key,clock=time.time):
        self.key=serialization.load_pem_public_key(Path(public_key).read_bytes())
        if not isinstance(self.key,rsa.RSAPublicKey):raise ValueError('Expected RSA public key')
        self.clock=clock;self.used={};self.lock=threading.Lock()

    def verify(self,ticket):
        try:
            if not isinstance(ticket,str) or len(ticket)>4096:return None
            h,p,s=ticket.split('.')
            decode=lambda v:base64.b64decode(v+'='*((-len(v))%4),altchars=b'-_',validate=True)
            header=json.loads(decode(h));claims=json.loads(decode(p))
            if header.get('alg')!='RS256' or header.get('typ')!='JWT':return None
            self.key.verify(decode(s),(h+'.'+p).encode('ascii'),padding.PKCS1v15(),hashes.SHA256())
            now=self.clock()
            if claims.get('iss')!='https://auth.blocknload.cc' or claims.get('aud')!='bnl-beta65-game':return None
            if not all(type(claims.get(k)) is int for k in ('iat','nbf','exp')):return None
            if not claims['nbf']<=now<claims['exp'] or claims['iat']>now+5 or not 0<claims['exp']-claims['iat']<=120:return None
            if not re.fullmatch(r'[0-9]{17}',claims.get('sub','')) or not re.fullmatch(r'[a-f0-9]{32}',claims.get('jti','')):return None
            if not isinstance(claims.get('name'),str) or not 1<=len(claims['name'].encode('utf-8'))<=96:return None
            with self.lock:
                self.used={k:v for k,v in self.used.items() if v>now}
                if claims['jti'] in self.used:return None
                self.used[claims['jti']]=claims['exp']
            return claims
        except (ValueError,TypeError,KeyError,AttributeError,InvalidSignature,UnicodeError):return None
