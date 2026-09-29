"""The shop capture stub must classify buy replies by the live function IDs."""
from pathlib import Path
import struct
import sys
import unittest


root = Path(__file__).resolve().parents[2] / 'tools/BrokerWorker/src'
sys.path[:0] = [str(root / 'client'), str(root / 'diagnostics')]
import process_event_shop_capture as shop


class Tests(unittest.TestCase):
    def test_buy_branch_uses_resolved_name_indices(self):
        buy = (73295, 73318)
        stub = shop.build_stub(
            [*buy, 73345, 73371], buy, 0x100000,
            [0x200000 + index * shop.RECORD_STRIDE for index in range(shop.HISTORY_DEPTH)],
            0x300000, 0x400000, 0x400000 + len(shop.PROLOGUE),
        )
        comparisons = [
            struct.unpack_from('<I', stub, offset + 3)[0]
            for offset in range(len(stub) - 6)
            if stub[offset:offset + 3] == bytes.fromhex('41 81 FB')
        ]
        self.assertEqual(comparisons, list(buy))


if __name__ == '__main__':
    unittest.main()
