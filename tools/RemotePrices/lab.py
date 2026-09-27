"""Shared, PID-scoped helpers for remote-price research (no game actions)."""
from __future__ import annotations

import json
import os
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKER = ROOT / "tools/BrokerWorker/src"
if (Path(__file__).resolve().parent.parent/'client').is_dir():
    WORKER = Path(__file__).resolve().parent.parent
sys.path[:0] = [str(WORKER / "client"), str(WORKER / "diagnostics")]

from lu4_memory_client import Lu4MemoryClient
from lu4_target_controller import read_exact, write_exact, patch_region, absolute_jump
from process_event_shop_capture import Code


def directory(pid: int) -> Path:
    path = Path(os.environ["LOCALAPPDATA"]) / "PriceCheckCollector/research/remote-prices" / str(pid)
    path.mkdir(parents=True, exist_ok=True)
    return path


def load(pid: int, name: str) -> dict:
    return json.loads((directory(pid) / name).read_text(encoding="utf-8-sig"))


def save(pid: int, name: str, value: object) -> None:
    path = directory(pid) / name
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def guard(client: Lu4MemoryClient, pid: int) -> int:
    base = client.process_base(pid)
    header = read_exact(client, pid, base, 0x1000)
    pe = struct.unpack_from("<I", header, 0x3C)[0]
    if header[:2] != b"MZ" or header[pe:pe + 4] != b"PE\0\0":
        raise RuntimeError("invalid PE")
    if (struct.unpack_from("<I", header, pe + 8)[0],
        struct.unpack_from("<I", header, pe + 80)[0]) != (0x956E0D97, 0xDCEB000):
        raise RuntimeError("unsupported client build")
    return base


def position(client: Lu4MemoryClient, pid: int) -> dict:
    base = guard(client, pid)
    def ptr(address: int) -> int:
        return struct.unpack("<Q", read_exact(client, pid, address, 8))[0]
    value = ptr(base + 0x81F6A80)
    for offset in (0x1D8, 0x38, 0, 0x30):
        value = ptr(value + offset)
    controller = value
    pawn = ptr(controller + 0x2D0)
    capsule = ptr(pawn + 0x1A0)
    xyz = struct.unpack("<ddd", read_exact(client, pid, capsule + 0x1F0, 24))
    selected = ptr(controller + 0x898)
    return {"controller": controller, "pawn": pawn, "position": dict(zip("xyz", xyz)),
            "selected": selected,
            "selected_id": struct.unpack("<i", read_exact(client, pid, selected + 0x550, 4))[0] if selected else 0}


def disassemble(client: Lu4MemoryClient, pid: int, address: int, size: int) -> str:
    from capstone import Cs, CS_ARCH_X86, CS_MODE_64
    base = client.process_base(pid)
    return "\n".join(f"{i.address - base:09X}  {i.bytes.hex():24} {i.mnemonic} {i.op_str}"
                     for i in Cs(CS_ARCH_X86, CS_MODE_64).disasm(read_exact(client, pid, address, size), address))


def function_bounds(client: Lu4MemoryClient, pid: int, rva: int) -> tuple[int, int]:
    """Resolve a native function through the PE exception directory, read-only."""
    base = guard(client, pid)
    header = read_exact(client, pid, base, 0x1000)
    pe = struct.unpack_from("<I", header, 0x3C)[0]
    table, size = struct.unpack_from("<II", header, pe + 24 + 112 + 3 * 8)
    low, high = 0, size // 12
    while low < high:
        middle = (low + high) // 2
        begin, end, _ = struct.unpack("<III", read_exact(client, pid, base + table + middle * 12, 12))
        if rva < begin:
            high = middle
        elif rva >= end:
            low = middle + 1
        else:
            return begin, end
    raise RuntimeError(f"no runtime function for RVA {rva:X}")


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pid", type=int)
    parser.add_argument("--rva", type=lambda x: int(x, 0))
    parser.add_argument("--size", type=lambda x: int(x, 0), default=0x200)
    args = parser.parse_args()
    with Lu4MemoryClient() as client:
        base = guard(client, args.pid)
        print(disassemble(client, args.pid, base + args.rva, args.size) if args.rva is not None
              else json.dumps(position(client, args.pid), indent=2))
