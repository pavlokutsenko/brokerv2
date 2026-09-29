import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'src' / 'client'))
from agent_send_layout import read_agent_send_layout
from send_prologue import resolve_send_prologue, SEND_PROLOGUE, ABSOLUTE_JUMP, RELAY_TAIL


class AgentSendLayoutTests(unittest.TestCase):
    def setUp(self):
        self.base = 0x180000000
        self.image = bytearray(8192)
        self.image[:2] = b'MZ'; struct.pack_into('<I', self.image, 0x3c, 128)
        self.image[128:132] = b'PE\0\0'
        struct.pack_into('<H', self.image, 152, 0x20b)
        struct.pack_into('<I', self.image, 208, len(self.image))
        struct.pack_into('<II', self.image, 264, 512, 256)
        struct.pack_into('<IIIII', self.image, 532, 1, 1, 560, 564, 568)
        struct.pack_into('<I', self.image, 560, 1024)
        struct.pack_into('<I', self.image, 564, 600)
        struct.pack_into('<H', self.image, 568, 0)
        self.image[600:626] = b'PriceCheckSendHookLayout\0'.ljust(26, b'\0')
        struct.pack_into('<IIIIQQ', self.image, 1024, 0x5043534c, 32, 1, 0,
                         self.base+1500, self.base+1600)

    def read(self, address, size):
        offset = address-self.base
        if not 0 <= offset <= len(self.image)-size: raise RuntimeError('out of range')
        return bytes(self.image[offset:offset+size])

    def test_relocated_addresses_and_rebuilt_layout(self):
        self.assertEqual(read_agent_send_layout(self.read, self.base), (self.base+1500, self.base+1600))
        struct.pack_into('<QQ', self.image, 1040, self.base+1700, self.base+1800)
        self.assertEqual(read_agent_send_layout(self.read, self.base), (self.base+1700, self.base+1800))

    def test_actual_built_agent_descriptor(self):
        path = Path(__file__).resolve().parents[3] / 'native' / 'ClientLaunch' / 'build' / 'x64' / 'PriceCheck.ClientAgent.dll'
        file = path.read_bytes(); nt = struct.unpack_from('<I', file, 0x3c)[0]
        sections = struct.unpack_from('<H', file, nt+6)[0]
        optional_size = struct.unpack_from('<H', file, nt+20)[0]
        base = struct.unpack_from('<Q', file, nt+48)[0]
        image = bytearray(struct.unpack_from('<I', file, nt+80)[0])
        header_size = struct.unpack_from('<I', file, nt+84)[0]
        image[:header_size] = file[:header_size]
        for index in range(sections):
            section = nt+24+optional_size+index*40
            virtual, raw_size, raw = struct.unpack_from('<III', file, section+12)
            image[virtual:virtual+raw_size] = file[raw:raw+raw_size]
        callback, slot = read_agent_send_layout(lambda address, size: bytes(image[address-base:address-base+size]), base)
        self.assertGreater(callback, base); self.assertGreater(slot, base)

    def test_invalid_version_pointers_and_forwarders_are_rejected(self):
        for offset, value in [(1032, 2), (1040, 0), (560, 600), (564, 9000)]:
            with self.subTest(offset=offset):
                original = self.image[:]
                struct.pack_into('<I', self.image, offset, value)
                with self.assertRaises(RuntimeError): read_agent_send_layout(self.read, self.base)
                self.image = original

    def test_relay_recovery_uses_exported_hook_and_validates_original(self):
        send = self.base+2048; relay = self.base+2200; trampoline = self.base+2300; prior = self.base+2400
        self.image[2048:2053] = b'\xe9'+struct.pack('<i', relay-send-5)
        self.image[2200:2214] = ABSOLUTE_JUMP+struct.pack('<Q', self.base+1500)
        struct.pack_into('<Q', self.image, 1600, trampoline)
        self.image[2300:2314] = ABSOLUTE_JUMP+struct.pack('<Q', prior)
        self.image[2444] = 0x5a; self.image[2445:2450] = SEND_PROLOGUE
        self.image[2450:2457] = RELAY_TAIL; struct.pack_into('<Q', self.image, 2457, send+5)
        layout = read_agent_send_layout(self.read, self.base)
        result = resolve_send_prologue(self.read, send, {'pricecheck.clientagent.dll': self.base}, layout)
        self.assertEqual(result, (SEND_PROLOGUE, 'recovered-prologue-from-agent-relay', [relay, trampoline, prior]))
        self.image[2445] = 0x90
        with self.assertRaises(RuntimeError):
            resolve_send_prologue(self.read, send, {'pricecheck.clientagent.dll': self.base}, layout)


if __name__ == '__main__': unittest.main()
