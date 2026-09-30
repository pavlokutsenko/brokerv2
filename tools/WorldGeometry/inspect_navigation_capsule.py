"""Bounded read-only check of the current pawn's known capsule fields.

No hook, game action or reader attachment. Uses the established LU4Memory
driver and the same validated per-image world/component resolver as navigation.
"""
import argparse
import json
import struct
from inspect_world import Lu4MemoryClient,Memory,pe_info
from walk_runtime import resolve_runtime


def read(pid):
    with Lu4MemoryClient() as client:
        base=client.process_base(pid);mem=Memory(client,pid)
        pe=pe_info(mem,base)
        _,_,world,survey=resolve_runtime(mem,base,pe)
        if not world: raise RuntimeError('world/controller guard failed')
        capsule=world['player_capsule'];description=survey.describe(capsule)
        if description['class']!='CapsuleComponent': raise RuntimeError('player capsule class changed')
        radius=mem.unpack('<f',capsule+0x544,0);half=mem.unpack('<f',capsule+0x540,0)
        scale=struct.unpack('<3d',mem.read(capsule+0x210,24));factor=max(abs(v) for v in scale)
        return {'pid':pid,'pe_timestamp':pe['timestamp'],'image_size':pe['image_size'],
                'capsule':hex(capsule),'radius':radius,'half_height':half,
                'scale':scale,'effective':[radius*factor,half*factor],
                'position':struct.unpack('<3d',mem.read(capsule+0x1F0,24))}


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('pid',type=int)
    print(json.dumps(read(parser.parse_args().pid)))
