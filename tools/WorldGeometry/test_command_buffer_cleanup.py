import struct
import unittest
from types import SimpleNamespace
from unittest.mock import Mock,patch
from walk_client import WalkClient,capture


class CommandBufferCleanupTests(unittest.TestCase):
    def client(self):
        c=object.__new__(WalkClient);c.pid=10;c.client=Mock()
        return c

    def test_only_finished_owned_commands_allow_parameter_buffer_release(self):
        for trigger,status,expected in ((1,0,False),(0,1,False),(0,0,True),(0,2,True),(0,3,False)):
            c=self.client()
            with patch('walk_client.capture.load_state',return_value={'pid':10,'command_address':100}), \
                 patch('walk_client.read_exact',return_value=struct.pack('<II',trigger,status)):
                self.assertEqual(c.release_command_buffer(200),expected)
            self.assertEqual(c.client.free_process_memory.call_count,int(expected))

    def test_unknown_or_other_pid_command_retains_buffer(self):
        for state in ({'pid':20,'command_address':100},{'pid':10}):
            c=self.client()
            with patch('walk_client.capture.load_state',return_value=state):
                self.assertFalse(c.release_command_buffer(200))
            c.client.free_process_memory.assert_not_called()
        c=self.client()
        with patch('walk_client.capture.load_state',return_value={'pid':10,'command_address':100}), \
             patch('walk_client.read_exact',side_effect=RuntimeError('client unavailable')):
            self.assertFalse(c.release_command_buffer(200))
        c.client.free_process_memory.assert_not_called()

    def test_timeout_cleanup_joins_reader_but_does_not_free_armed_trace_array(self):
        c=self.client();c.owned=True;c.mutex=None
        c.original_paths=capture.STATE_PATH,capture.ROUTE_PATH,capture.FUNCTIONS_PATH
        c.stop=Mock(side_effect=TimeoutError('game-thread ProcessEvent command was not consumed'))
        joined=Mock();c.cleanup_callbacks=[lambda:c.release_command_buffer(200),joined]
        with patch('walk_client.capture.load_state',return_value={'pid':10,'command_address':100}), \
             patch('walk_client.read_exact',return_value=struct.pack('<II',1,0)), \
             patch('walk_client.capture.set_suppress_ui'),patch('walk_client.capture.set_suppress_target_ui'):
            with self.assertRaises(TimeoutError):c.close()
        joined.assert_called_once();c.client.free_process_memory.assert_not_called()
        c.client.close.assert_called_once();self.assertFalse(c.owned)


if __name__=='__main__':unittest.main()
