from pathlib import Path
import struct
import sys
import unittest
from unittest.mock import patch
root=Path(__file__).resolve().parents[2]/'tools/BrokerWorker/src'
sys.path[:0]=[str(root/'client'),str(root/'diagnostics')]
from discover_unreal_globals import validate_gobjects,current_profile

class Memory:
    def __init__(self):
        self.chunk_table=0x200000;self.chunk0=0x300000;self.last_chunk=0x400000
        self.objects=[0x500000,0x500100,0x500200]
    def page_read(self,address,size):
        if address==self.chunk_table:return struct.pack('<2Q',self.chunk0,self.last_chunk)
        raise AssertionError(hex(address))
    def u64(self,address):
        if address==self.last_chunk:return 0 # Normal free slot after GC.
        index=(address-self.chunk0)//0x18
        return self.objects[index] if 0<=index<3 else 0
    def i32(self,address,default=-1):
        return next((i for i,p in enumerate(self.objects) if p+0x0C==address),default)

class Tests(unittest.TestCase):
    def header(self,count=70000):
        data=bytearray(0x30);struct.pack_into('<Q',data,0,0x200000)
        struct.pack_into('<iiii',data,0x10,4*65536,count,4,2);return data
    def test_free_dynamic_chunk_slot_is_valid(self):
        self.assertIsNotNone(validate_gobjects(Memory(),0x100000,self.header(),0))
    def test_count_at_chunk_boundary_is_valid(self):
        self.assertIsNotNone(validate_gobjects(Memory(),0x100000,self.header(65536),0))
    def test_bad_internal_index_rejected(self):
        mem=Memory()
        with patch.object(mem,'i32',return_value=99):self.assertIsNone(validate_gobjects(mem,0x100000,self.header(),0))
    def test_capacity_overflow_rejected(self):
        self.assertIsNone(validate_gobjects(Memory(),0x100000,self.header(150000),0))
    def test_unknown_build_never_uses_fixed_rva(self):
        with self.assertRaisesRegex(RuntimeError,'Unsupported'):
            current_profile(None,0,{'timestamp':0,'image_size':0})

if __name__=='__main__':unittest.main()
