import copy
from pathlib import Path
import struct
import sys
import unittest
import json
import tempfile
from unittest.mock import patch, MagicMock
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'tools/BrokerWorker/src/client'))
from session_cache import valid_session,valid_active64,reuse

class Driver:
    def __init__(self):
        self.base=0x140000000;self.bytes={};self.active=(9000,0x90000)
        raw=bytearray(0x68);struct.pack_into('<Q',raw,0,2000);struct.pack_into('<Q',raw,0x10,3000);raw[0x31]=1
        self.bytes[1000]=bytes(raw);self.bytes[3008]=struct.pack('<I',44)
        slot=bytearray(0x14);slot[0]=1;struct.pack_into('<I',slot,0x10,7);self.slot=bytes(slot)
    def process_base(self,pid):return self.base
    def read(self,pid,address,size):return self.bytes[address][:size]
    def active64_info(self):return self.active
    def read_active64(self,offset,size):return self.slot

class CacheTests(unittest.TestCase):
    def setUp(self):
        self.driver=Driver()
        self.session={'pid':7,'image_base':self.driver.base,'connection_unique':True,'connection_candidates':[
            {'connection':1000,'vtable':2000,'socket_wrapper':3000,'rolling_key_address':1087,'socket_candidates':[{'offset':8,'handle':44}]}]}
        self.active={'pid':7,'active64_base':9000,'active64_size':0x90000,'matching_slot_unique':True,
            'matching_slots':[{'selected_state_module_offset':0x440CC}]}
    def test_live_cache(self):
        self.assertTrue(valid_session(self.driver,7,self.session));self.assertTrue(valid_active64(self.driver,7,self.active))
    def test_pid_or_base_reuse(self):
        self.assertFalse(valid_session(self.driver,8,self.session));self.driver.base+=0x10000
        self.assertFalse(valid_session(self.driver,7,self.session))
    def test_changed_connection_fields(self):
        for field,value in [('vtable',4000),('socket_wrapper',4000),('rolling_key_address',1)]:
            with self.subTest(field=field):
                data=copy.deepcopy(self.session);data['connection_candidates'][0][field]=value
                self.assertFalse(valid_session(self.driver,7,data))
        self.driver.bytes[3008]=struct.pack('<I',48)
        self.assertFalse(valid_session(self.driver,7,self.session))
    def test_inactive_connection(self):
        raw=bytearray(self.driver.bytes[1000]);raw[0x31]=0;self.driver.bytes[1000]=bytes(raw)
        self.assertFalse(valid_session(self.driver,7,self.session))
    def test_kernel_slot_rebound(self):
        self.assertFalse(valid_active64(self.driver,8,self.active))
        raw=bytearray(self.driver.slot);struct.pack_into('<I',raw,0x10,8);self.driver.slot=bytes(raw)
        self.assertFalse(valid_active64(self.driver,7,self.active))
    def test_kernel_module_changed(self):
        self.driver.active=(10000,0x90000)
        self.assertFalse(valid_active64(self.driver,7,self.active))

    def test_route_reuses_validated_broker_session_only(self):
        with tempfile.TemporaryDirectory() as root:
            source=Path(root)/'PriceCheckCollector/collection/runtime-sessions/7-start/version/BrokerRuntime/_internal/diagnostics/latest_session.json'
            source.parent.mkdir(parents=True);source.write_text(json.dumps(self.session))
            destination=Path(root)/'PriceCheckCollector/research/market-walk/7/shops/latest_session.json'
            handle=MagicMock();handle.__enter__.return_value=self.driver
            with patch.dict('os.environ',{'LOCALAPPDATA':root}),patch('session_cache.Lu4MemoryClient',return_value=handle):
                self.assertTrue(reuse(7,destination,'session'))
                self.assertEqual(json.loads(destination.read_text()),self.session)
                self.driver.bytes[3008]=struct.pack('<I',48)
                self.assertFalse(reuse(7,destination,'session'),'changed live socket rejects both caches')

if __name__=='__main__':unittest.main()
