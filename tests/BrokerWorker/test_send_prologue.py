from pathlib import Path
import struct
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/BrokerWorker/src/client"))
from send_prologue import resolve_send_prologue


class Memory:
    def __init__(self):
        self.bytes = {}

    def put(self, address, data):
        self.bytes.update((address + i, b) for i, b in enumerate(data))

    def read(self, address, size):
        return bytes(self.bytes[address + i] for i in range(size))


def fixture(nested):
    memory = Memory()
    send, relay, agent, trampoline, prior = 0x100000, 0x101000, 0x400000, 0x102000, 0x103000
    memory.put(send, b"\xE9" + struct.pack("<i", relay - send - 5))
    retained = bytes(44) + bytes.fromhex("5A 48 89 5C 24 08 48 FF 25 00 00 00 00") + struct.pack("<Q", send + 5)
    if nested:
        memory.put(relay, bytes.fromhex("FF 25 00 00 00 00") + struct.pack("<Q", agent + 0x28C70) + bytes(51))
        memory.put(agent + 0x28D6C, bytes.fromhex("48 8B 05 A5 50 0D 00"))
        memory.put(agent + 0xFDE18, struct.pack("<Q", trampoline))
        memory.put(trampoline, bytes.fromhex("FF 25 00 00 00 00") + struct.pack("<Q", prior))
        memory.put(prior, retained)
    else:
        memory.put(relay, retained)
    return memory, send, relay, agent, trampoline, prior


class SendPrologueTests(unittest.TestCase):
    def test_unhooked_send(self):
        memory = Memory()
        memory.put(0x100000, bytes.fromhex("48 89 5C 24 08"))
        self.assertEqual(resolve_send_prologue(memory.read, 0x100000, {}),
                         (bytes.fromhex("48 89 5C 24 08"), "copied-prologue", []))

    def test_existing_direct_relay(self):
        memory, send, relay, *_ = fixture(False)
        prologue, mode, addresses = resolve_send_prologue(memory.read, send, {})
        self.assertEqual(prologue, bytes.fromhex("48 89 5C 24 08"))
        self.assertEqual(mode, "recovered-prologue-from-e9-relay")
        self.assertEqual(addresses, [relay])

    def test_current_agent_wrapping_prior_relay(self):
        memory, send, relay, agent, trampoline, prior = fixture(True)
        prologue, mode, addresses = resolve_send_prologue(memory.read, send, {"pricecheck.clientagent.dll": agent})
        self.assertEqual(prologue, bytes.fromhex("48 89 5C 24 08"))
        self.assertEqual(mode, "recovered-prologue-from-agent-relay")
        self.assertEqual(addresses, [relay, trampoline, prior])

    def test_rejects_unknown_or_changed_chain(self):
        mutations = (
            lambda m, s, r, a, t, p: m.put(s, b"\xEB\0\x90\x90\x90"),
            lambda m, s, r, a, t, p: m.put(r + 6, struct.pack("<Q", a + 0x28C71)),
            lambda m, s, r, a, t, p: m.put(a + 0x28D6C, b"\x90"),
            lambda m, s, r, a, t, p: m.put(a + 0xFDE18, bytes(8)),
            lambda m, s, r, a, t, p: m.put(t, b"\xE9"),
            lambda m, s, r, a, t, p: m.put(t + 6, struct.pack("<Q", r)),
            lambda m, s, r, a, t, p: m.put(p + 45, b"\xE9"),
            lambda m, s, r, a, t, p: m.put(p + 57, struct.pack("<Q", s + 10)),
        )
        for index, mutate in enumerate(mutations):
            with self.subTest(index=index):
                values = fixture(True)
                mutate(*values)
                memory, send, _, agent, *_ = values
                with self.assertRaises(RuntimeError):
                    resolve_send_prologue(memory.read, send, {"pricecheck.clientagent.dll": agent})

    def test_agent_module_is_required_for_wrapped_relay(self):
        memory, send, *_ = fixture(True)
        with self.assertRaises(RuntimeError):
            resolve_send_prologue(memory.read, send, {})


if __name__ == "__main__":
    unittest.main()
