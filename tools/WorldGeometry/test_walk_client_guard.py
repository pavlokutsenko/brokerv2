import unittest
from unittest.mock import patch

from walk_client import WalkClient


class FakeMemory:
    def __init__(self, snapshots):
        self.snapshots = iter(snapshots)
        self.pages = {}
        self.reads = []

    def u64(self, address):
        if address == 0x1010:
            self.current = next(self.snapshots)
            return self.current[0]
        if address == 0x22D0:
            return self.current[1]
        raise AssertionError(f"unexpected address {address:#x}")


class WorldPawnGuardTests(unittest.TestCase):
    def client(self, snapshots):
        client = WalkClient.__new__(WalkClient)
        client.m = FakeMemory(snapshots)
        client.base = 0x1000
        client.rvas = {'gworld': 0x10}
        client.world = {'world': 0xAAAA, 'controller': 0x2000,
                        'player_actor': 0xBBBB}
        return client

    @patch('walk_client.time.sleep')
    def test_transient_mismatch_is_rechecked(self, sleep):
        client = self.client([(0, 0), (0xAAAA, 0xBBBB)])
        self.assertTrue(client.world_pawn_current())
        sleep.assert_called_once_with(.05)

    @patch('walk_client.time.sleep')
    def test_persistent_change_still_stops_navigation(self, sleep):
        client = self.client([(0xCCCC, 0xDDDD)] * 3)
        self.assertFalse(client.world_pawn_current())
        self.assertEqual(sleep.call_count, 2)
        client = self.client([(0xCCCC, 0xDDDD)] * 3)
        with self.assertRaisesRegex(RuntimeError, 'world/pawn changed'):
            client.position()


if __name__ == '__main__':
    unittest.main()
