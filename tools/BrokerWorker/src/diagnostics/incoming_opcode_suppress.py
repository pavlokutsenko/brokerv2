from __future__ import annotations

import argparse
import json
import struct
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "client"))

from lu4_memory_client import (  # noqa: E402
    Lu4MemoryClient,
    PAGE_EXECUTE_READWRITE,
    PAGE_READWRITE,
)


NORMAL_TABLE_RVA = 0x07EC75F0
STATE_PATH = ROOT / "diagnostics" / "incoming_opcode_suppress_state.json"


def write_exact(client: Lu4MemoryClient, pid: int, address: int, data: bytes) -> None:
    copied = client.write(pid, address, data)
    if copied != len(data):
        raise RuntimeError(f"short write at 0x{address:X}: {copied}/{len(data)}")


def install(pid: int, opcode: int) -> None:
    if STATE_PATH.exists():
        raise RuntimeError(f"suppression already installed: {STATE_PATH}")
    with Lu4MemoryClient() as client:
        image_base = client.process_base(pid)
        slot = image_base + NORMAL_TABLE_RVA + opcode * 8
        original = struct.unpack("<Q", client.read(pid, slot, 8))[0]
        stub, _ = client.allocate_process_memory(pid, 0x1000, PAGE_EXECUTE_READWRITE)
        try:
            # Incoming handlers are void __fastcall(context, message), so a bare RET
            # preserves the dispatcher's stack and skips client-side processing.
            write_exact(client, pid, stub, b"\xC3")
            old_address, old_size, old_protection = client.protect_process_memory(
                pid, slot, 8, PAGE_READWRITE
            )
            try:
                write_exact(client, pid, slot, struct.pack("<Q", stub))
            finally:
                client.protect_process_memory(pid, old_address, old_size, old_protection)
        except Exception:
            client.free_process_memory(pid, stub)
            raise
    state = {
        "pid": pid,
        "image_base": f"0x{image_base:X}",
        "opcode": f"0x{opcode:02X}",
        "slot": f"0x{slot:X}",
        "original": f"0x{original:X}",
        "stub": f"0x{stub:X}",
    }
    STATE_PATH.write_text(json.dumps(state, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(state, indent=2))


def uninstall() -> None:
    state = json.loads(STATE_PATH.read_text(encoding="utf-8"))
    pid = int(state["pid"])
    slot = int(state["slot"], 16)
    original = int(state["original"], 16)
    stub = int(state["stub"], 16)
    with Lu4MemoryClient() as client:
        current = struct.unpack("<Q", client.read(pid, slot, 8))[0]
        if current != stub:
            raise RuntimeError(
                f"dispatch slot changed: expected 0x{stub:X}, found 0x{current:X}"
            )
        old_address, old_size, old_protection = client.protect_process_memory(
            pid, slot, 8, PAGE_READWRITE
        )
        try:
            write_exact(client, pid, slot, struct.pack("<Q", original))
        finally:
            client.protect_process_memory(pid, old_address, old_size, old_protection)
        client.free_process_memory(pid, stub)
    STATE_PATH.unlink()
    print("incoming opcode suppression removed")


def main() -> int:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    install_parser = sub.add_parser("install")
    install_parser.add_argument("pid", type=int)
    install_parser.add_argument("opcode", type=lambda value: int(value, 0))
    sub.add_parser("uninstall")
    args = parser.parse_args()
    if args.command == "install":
        install(args.pid, args.opcode)
    else:
        uninstall()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
