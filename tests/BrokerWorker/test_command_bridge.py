"""An orphaned completed command must not disable the sole live reader."""
from pathlib import Path
import struct
import sys
import unittest
from unittest.mock import patch

root=Path(__file__).resolve().parents[2]/'tools/BrokerWorker/src'
sys.path[:0]=[str(root/'client'),str(root/'diagnostics')]
import invoke_process_event as bridge


class Memory:
    def __init__(self,status):
        self.bytes=bytearray(4096)
        struct.pack_into('<II',self.bytes,100,0,status)
        self.cleared=0

    def __enter__(self): return self
    def __exit__(self,*_): return False

    def read(self,_client,_pid,address,size):
        return bytes(self.bytes[address:address+size])

    def write(self,_client,_pid,address,value):
        self.bytes[address:address+len(value)]=value
        if address==104 and value==b'\0\0\0\0': self.cleared+=1
        if address==100 and value==struct.pack('<I',1):
            struct.pack_into('<II',self.bytes,100,0,2)


class CommandBridgeTests(unittest.TestCase):
    def test_orphaned_completed_reply_is_cleared_before_next_command(self):
        memory=Memory(2)
        state={'version':9,'pid':7,'command_address':100,'command_params_address':200}
        with patch.object(bridge,'load_state',return_value=state),\
             patch.object(bridge,'Lu4MemoryClient',return_value=memory),\
             patch.object(bridge,'read_exact',side_effect=memory.read),\
             patch.object(bridge,'write_exact',side_effect=memory.write):
            result=bridge.invoke(300,400,b'abcd')
        self.assertEqual(result,b'abcd')
        self.assertEqual(memory.cleared,2)

    def test_running_command_is_not_reset(self):
        memory=Memory(1)
        state={'version':9,'pid':7,'command_address':100,'command_params_address':200}
        with patch.object(bridge,'load_state',return_value=state),\
             patch.object(bridge,'Lu4MemoryClient',return_value=memory),\
             patch.object(bridge,'read_exact',side_effect=memory.read),\
             patch.object(bridge,'write_exact',side_effect=memory.write):
            with self.assertRaisesRegex(RuntimeError,'bridge is busy'):
                bridge.invoke(300,400,b'abcd')
        self.assertEqual(memory.cleared,0)


if __name__=='__main__': unittest.main()
