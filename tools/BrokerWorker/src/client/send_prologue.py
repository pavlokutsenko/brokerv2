"""Read-only validation of the supported ws2_32!send hook layouts.

Recover displaced instructions for the existing sender; never call a foreign
relay from its code cave or follow an arbitrary chain of jumps.
"""
from __future__ import annotations

import struct


SEND_PROLOGUE = bytes.fromhex("48 89 5C 24 08")
ABSOLUTE_JUMP = bytes.fromhex("FF 25 00 00 00 00")
RELAY_TAIL = bytes.fromhex("48 FF 25 00 00 00 00")
# ClientAgent layout validated on 2026-09-24. These RVAs are guarded by the
# relay destination and exact instructions, not by module name alone.
AGENT_CALLBACK_RVA = 0x28C70
AGENT_GETTER_RVA = 0x28D6C
AGENT_GETTER = bytes.fromhex("48 8B 05 A5 50 0D 00")


def _retained_prologue(raw: bytes, send: int) -> bytes | None:
    if (len(raw) != 65 or raw[44] != 0x5A or raw[50:57] != RELAY_TAIL
            or struct.unpack_from("<Q", raw, 57)[0] != send + 5):
        return None
    # Only the observed complete, position-independent five-byte instruction
    # is safe to copy. A branch or partial instruction must never be executed.
    if raw[45:50] != SEND_PROLOGUE:
        raise RuntimeError("retained send prologue is not the validated instruction")
    return raw[45:50]


def resolve_send_prologue(read, send: int, modules: dict[str, int], agent_layout=None) -> tuple[bytes, str, list[int]]:
    """Return (displaced bytes, mode, relay addresses); perform no memory writes."""
    entry = read(send, 5)
    if entry == SEND_PROLOGUE:
        return entry, "copied-prologue", []
    if len(entry) != 5 or entry[0] != 0xE9:
        raise RuntimeError("send entry is not a supported prologue or E9 relay")
    relay = send + 5 + struct.unpack_from("<i", entry, 1)[0]
    raw = read(relay, 65)
    prologue = _retained_prologue(raw, send)
    if prologue is not None:
        return prologue, "recovered-prologue-from-e9-relay", [relay]

    agent = modules.get("pricecheck.clientagent.dll")
    callback = agent_layout[0] if agent_layout else (agent + AGENT_CALLBACK_RVA if agent else 0)
    if (not agent or raw[:6] != ABSOLUTE_JUMP
            or struct.unpack_from("<Q", raw, 6)[0] != callback):
        raise RuntimeError("send E9 target is not a validated prior or ClientAgent relay")
    if agent_layout:
        slot = agent_layout[1]
    else:
        getter_address = agent + AGENT_GETTER_RVA
        getter = read(getter_address, len(AGENT_GETTER))
        if getter != AGENT_GETTER:
            raise RuntimeError("ClientAgent send layout changed; refusing unknown trampoline")
        slot = getter_address + len(getter) + struct.unpack_from("<i", getter, 3)[0]
    trampoline = struct.unpack("<Q", read(slot, 8))[0]
    if not trampoline or trampoline in (send, relay):
        raise RuntimeError("invalid ClientAgent original-send trampoline")
    jump = read(trampoline, 14)
    if jump[:6] != ABSOLUTE_JUMP:
        raise RuntimeError("ClientAgent original-send trampoline is not a validated absolute jump")
    prior = struct.unpack_from("<Q", jump, 6)[0]
    if not prior or prior in (send, relay, trampoline):
        raise RuntimeError("cyclic or empty ClientAgent send relay")
    prologue = _retained_prologue(read(prior, 65), send)
    if prologue is None:
        raise RuntimeError("ClientAgent original send is not the validated retained-prologue relay")
    return prologue, "recovered-prologue-from-agent-relay", [relay, trampoline, prior]
