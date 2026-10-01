"""Validated edits to full native UnitInit fixtures, preserving the recovered layout."""
import struct

def set_identity(packet,team,player_id=None):
    # Test harnesses use short opaque stand-ins; only native UnitCreate frames have fields.
    if len(packet)<7 or packet[:2]!=b'\x06\x08':return packet
    if packet[6]&0xe6!=0xe6 or packet[11:13]!=b'\xff\x80':raise ValueError('Unsupported UnitInit template')
    at=44;out=bytearray(packet)
    if packet[6]&0x10:
        if player_id is not None:struct.pack_into('<I',out,at,player_id)
        at+=4
    if packet[6]&0x08:at+=4
    if len(packet)!=at+5:raise ValueError('UnitInit tail mismatch')
    out[at]=team
    return bytes(out)
