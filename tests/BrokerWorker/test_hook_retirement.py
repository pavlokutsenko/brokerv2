"""Restoring a live detour must not unmap code a game thread may still execute."""
from pathlib import Path
import json
import sys
import tempfile
import unittest
from unittest.mock import patch

root = Path(__file__).resolve().parents[2] / 'tools/BrokerWorker/src'
sys.path[:0] = [str(root / 'client'), str(root / 'diagnostics')]
import process_event_broker_capture as broker
import process_event_shop_capture as shop


class Client:
    def __enter__(self):
        return self

    def __exit__(self, *_):
        return False

    def free_process_memory(self, *_):
        raise AssertionError('retired trampoline was freed while game is live')


class Tests(unittest.TestCase):
    def test_live_uninstall_retires_executable_and_history_memory(self):
        for module in (shop, broker):
            with self.subTest(module=module.__name__), tempfile.TemporaryDirectory() as folder:
                path = Path(folder) / 'state.json'
                path.write_text(json.dumps({
                    'pid': 100, 'process_event': 0x1000, 'original_hex': module.PROLOGUE.hex(),
                    'patch_hex': 'ff' * len(module.PROLOGUE), 'cave': 0x2000,
                    'history_cave': 0x3000,
                    'history_caves': [{'address': 0x4000}],
                }), encoding='utf-8')
                restored = []
                with patch.object(module, 'STATE_PATH', path), \
                     patch.object(module, 'Lu4MemoryClient', Client), \
                     patch.object(module, 'read_exact', return_value=b'\xff' * len(module.PROLOGUE)), \
                     patch.object(module, 'patch_region', side_effect=lambda _c, _p, _a, value: restored.append(value)):
                    if module is shop:
                        with patch.object(module, 'suspend_process', side_effect=OSError('protected')):
                            module.uninstall()
                    else:
                        module.uninstall()
                self.assertEqual(restored, [module.PROLOGUE])
                self.assertFalse(path.exists())


if __name__ == '__main__':
    unittest.main()
