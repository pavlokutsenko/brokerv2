"""Read-only, unique-signature resolution of the owned client's approach hook."""
import struct

from inspect_world import pe_info

APPROACH_PREFIX = bytes.fromhex("4883ec588b4210f20f10421c488b0d")
APPROACH_MESSAGE_SLOT = 0x72
CHUNK = 0x100000


def _matches(memory, base, sections, needle):
    found = []
    for section in sections:
        start = base + section["rva"]
        for offset in range(0, section["size"], CHUNK):
            data = memory.read(start + offset, min(CHUNK, section["size"] - offset))
            at = data.find(needle)
            while at >= 0:
                found.append(start + offset + at)
                at = data.find(needle, at + 1)
    return found


def approach_slot(memory, base):
    pe = pe_info(memory, base)
    executable = [s for s in pe["sections"] if s["characteristics"] & 0x20000000]
    handlers = _matches(memory, base, executable, APPROACH_PREFIX)
    if len(handlers) != 1:
        raise RuntimeError(f"MoveToObject handler signature is not unique: {len(handlers)}")
    handler = handlers[0]
    writable = [s for s in pe["sections"] if s["characteristics"] & 0x80000000
                and s["characteristics"] & 0x40000000]
    references = _matches(memory, base, writable, struct.pack("<Q", handler))
    if len(references) != 1 or references[0] & 7:
        raise RuntimeError(f"MoveToObject handler table is not unique: {len(references)}")
    slot = references[0]
    table = slot - APPROACH_MESSAGE_SLOT * 8
    if not any(base + s["rva"] <= table < slot < base + s["rva"] + s["size"]
               for s in writable):
        raise RuntimeError("MoveToObject slot is outside writable module table")
    around = struct.unpack("<5Q", memory.read(slot - 16, 40))
    if any(not base <= value < base + pe["image_size"] for value in around):
        raise RuntimeError("MoveToObject table neighbor guard failed")
    return slot, handler
