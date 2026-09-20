from __future__ import annotations

import argparse
import ctypes
import json
import struct
import sys
import time
from ctypes import wintypes
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT_ROOT / "client"))

from lu4_memory_client import Lu4MemoryClient  # noqa: E402


MEM_COMMIT = 0x1000
MEM_PRIVATE = 0x20000
PAGE_NOACCESS = 0x01
PAGE_READWRITE = 0x04
PAGE_WRITECOPY = 0x08
PAGE_EXECUTE_READWRITE = 0x40
PAGE_EXECUTE_WRITECOPY = 0x80
PAGE_GUARD = 0x100
PROCESS_QUERY_INFORMATION = 0x0400
MAX_ADDRESS = 0x0000800000000000
MAX_TRANSFER = 1024 * 1024
MAX_ROWS = 200
ROW_SIZE = 0x38


class MEMORY_BASIC_INFORMATION(ctypes.Structure):
    _fields_ = [
        ("BaseAddress", ctypes.c_void_p),
        ("AllocationBase", ctypes.c_void_p),
        ("AllocationProtect", wintypes.DWORD),
        ("PartitionId", wintypes.WORD),
        ("RegionSize", ctypes.c_size_t),
        ("State", wintypes.DWORD),
        ("Protect", wintypes.DWORD),
        ("Type", wintypes.DWORD),
    ]


kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
kernel32.OpenProcess.restype = wintypes.HANDLE
kernel32.VirtualQueryEx.argtypes = [
    wintypes.HANDLE,
    ctypes.c_void_p,
    ctypes.POINTER(MEMORY_BASIC_INFORMATION),
    ctypes.c_size_t,
]
kernel32.VirtualQueryEx.restype = ctypes.c_size_t
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.CloseHandle.restype = wintypes.BOOL


def pointer(value: int) -> bool:
    return 0x10000 <= value < MAX_ADDRESS


def read_exact(client: Lu4MemoryClient, pid: int, address: int, size: int) -> bytes:
    data = client.read(pid, address, size)
    if len(data) != size:
        raise RuntimeError(
            f"short read at 0x{address:X}: requested={size} copied={len(data)}"
        )
    return data


def readable_writable_private(protect: int) -> bool:
    if protect & (PAGE_GUARD | PAGE_NOACCESS):
        return False
    return protect & 0xFF in (
        PAGE_READWRITE,
        PAGE_WRITECOPY,
        PAGE_EXECUTE_READWRITE,
        PAGE_EXECUTE_WRITECOPY,
    )


def regions(pid: int):
    handle = kernel32.OpenProcess(PROCESS_QUERY_INFORMATION, False, pid)
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        address = 0x10000
        while address < MAX_ADDRESS:
            info = MEMORY_BASIC_INFORMATION()
            queried = kernel32.VirtualQueryEx(
                handle,
                ctypes.c_void_p(address),
                ctypes.byref(info),
                ctypes.sizeof(info),
            )
            if not queried:
                break
            base = int(info.BaseAddress or 0)
            size = int(info.RegionSize)
            if (
                info.State == MEM_COMMIT
                and info.Type == MEM_PRIVATE
                and size >= 4
                and readable_writable_private(int(info.Protect))
            ):
                yield base, size, int(info.Protect)
            next_address = base + size
            address = next_address if next_address > address else address + 0x1000
    finally:
        kernel32.CloseHandle(handle)


def read_header(raw: bytes, side: str) -> dict[str, int]:
    if side == "sell":
        return {
            "object_id": struct.unpack_from("<i", raw, 0x00)[0],
            "data": struct.unpack_from("<Q", raw, 0x10)[0],
            "count": struct.unpack_from("<i", raw, 0x18)[0],
            "capacity": struct.unpack_from("<i", raw, 0x1C)[0],
        }
    return {
        "object_id": struct.unpack_from("<i", raw, 0x00)[0],
        "data": struct.unpack_from("<Q", raw, 0x08)[0],
        "count": struct.unpack_from("<i", raw, 0x10)[0],
        "capacity": struct.unpack_from("<i", raw, 0x14)[0],
    }


def plausible_header(header: dict[str, int], object_id: int) -> bool:
    return (
        header["object_id"] == object_id
        and pointer(header["data"])
        and 0 < header["count"] <= MAX_ROWS
        and header["count"] <= header["capacity"] <= 512
    )


def decode_row(raw: bytes, address: int, index: int) -> dict[str, int | str]:
    row: dict[str, int | str] = {
        "row_index": index,
        "row_address": f"0x{address:X}",
        "item_object_id": struct.unpack_from("<i", raw, 0x00)[0],
        "item_id": struct.unpack_from("<i", raw, 0x04)[0],
        "count": struct.unpack_from("<i", raw, 0x08)[0],
        "enchant_level": struct.unpack_from("<i", raw, 0x14)[0],
        "price": struct.unpack_from("<i", raw, 0x2C)[0],
        "buy_count": struct.unpack_from("<i", raw, 0x30)[0],
        "base_price": struct.unpack_from("<i", raw, 0x34)[0],
    }
    return row


def coherent_row(row: dict[str, int | str]) -> bool:
    return (
        int(row["item_object_id"]) >= 0
        and 0 < int(row["item_id"]) <= 5_000_000
        and int(row["count"]) >= 0
        and int(row["buy_count"]) >= 0
        and int(row["price"]) > 0
        and int(row["base_price"]) >= 0
        and 0 <= int(row["enchant_level"]) <= 65_535
    )


def inspect_candidate(
    client: Lu4MemoryClient,
    pid: int,
    base: int,
    object_id: int,
    side: str,
) -> dict[str, object] | None:
    header_size = 0x20 if side == "sell" else 0x18
    try:
        before = read_header(read_exact(client, pid, base, header_size), side)
        if not plausible_header(before, object_id):
            return None
        rows_raw = read_exact(client, pid, before["data"], before["count"] * ROW_SIZE)
        rows = []
        for index in range(before["count"]):
            offset = index * ROW_SIZE
            row = decode_row(
                rows_raw[offset : offset + ROW_SIZE],
                before["data"] + offset,
                index,
            )
            if not coherent_row(row):
                return None
            rows.append(row)
        after = read_header(read_exact(client, pid, base, header_size), side)
    except (OSError, RuntimeError, struct.error):
        return None
    if before != after or not plausible_header(after, object_id):
        return None
    return {
        "side": side,
        "base": f"0x{base:X}",
        "items_data": f"0x{after['data']:X}",
        "object_id": object_id,
        "count": after["count"],
        "capacity": after["capacity"],
        "rows": rows,
    }


def selected_guard(
    client: Lu4MemoryClient,
    pid: int,
    controller: int,
    object_id: int,
    kiosk_type: int,
) -> dict[str, object]:
    actor = struct.unpack(
        "<Q", read_exact(client, pid, controller + 0x898, 8)
    )[0]
    observed_object_id = struct.unpack(
        "<i", read_exact(client, pid, actor + 0x550, 4)
    )[0] if actor else 0
    observed_kiosk = struct.unpack(
        "<i", read_exact(client, pid, actor + 0x7DC, 4)
    )[0] if actor else 0
    if observed_object_id != object_id or observed_kiosk != kiosk_type:
        raise RuntimeError(
            "selected-trader guard failed: "
            f"actor=0x{actor:X} object_id={observed_object_id} kiosk={observed_kiosk}"
        )
    return {
        "actor": f"0x{actor:X}",
        "object_id": observed_object_id,
        "kiosk_type": observed_kiosk,
    }


def scan(
    pid: int,
    object_id: int,
    kiosk_type: int,
    controller: int,
    timeout_seconds: float,
    settle_seconds: float = 2.0,
    require_selected: bool = True,
) -> dict[str, object]:
    sides = ["sell"] if kiosk_type in (1, 8) else ["buy"] if kiosk_type == 3 else []
    if not sides:
        raise RuntimeError(f"unsupported kiosk type: {kiosk_type}")
    started = time.monotonic()
    deadline = started + timeout_seconds
    settle_deadline: float | None = None
    pattern = struct.pack("<i", object_id)
    exact_hits = 0
    bytes_scanned = 0
    region_count = 0
    read_failures = 0
    candidates: dict[tuple[str, str], dict[str, object]] = {}

    with Lu4MemoryClient() as client:
        before_guard = (
            selected_guard(client, pid, controller, object_id, kiosk_type)
            if require_selected
            else None
        )
        for region_base, region_size, _ in regions(pid):
            if time.monotonic() >= deadline or (
                settle_deadline is not None and time.monotonic() >= settle_deadline
            ):
                break
            region_count += 1
            offset = 0
            while offset < region_size:
                now = time.monotonic()
                if now >= deadline or (
                    settle_deadline is not None and now >= settle_deadline
                ):
                    break
                size = min(MAX_TRANSFER, region_size - offset)
                address = region_base + offset
                try:
                    chunk = read_exact(client, pid, address, size)
                except (OSError, RuntimeError):
                    read_failures += 1
                    offset += size
                    continue
                bytes_scanned += len(chunk)
                cursor = 0
                while True:
                    found = chunk.find(pattern, cursor)
                    if found < 0:
                        break
                    cursor = found + 4
                    candidate_base = address + found
                    if candidate_base & 3:
                        continue
                    exact_hits += 1
                    for side in sides:
                        candidate = inspect_candidate(
                            client, pid, candidate_base, object_id, side
                        )
                        if candidate is not None:
                            candidates[(side, str(candidate["base"]))] = candidate
                            if settle_deadline is None:
                                settle_deadline = time.monotonic() + settle_seconds
                offset += size
        after_guard = (
            selected_guard(client, pid, controller, object_id, kiosk_type)
            if require_selected
            else None
        )

    ordered = sorted(
        candidates.values(),
        key=lambda item: (-int(item["count"]), str(item["base"])),
    )
    if not ordered:
        raise RuntimeError(
            "no coherent non-empty shop payload found; "
            f"hits={exact_hits} scanned={bytes_scanned}"
        )
    return {
        "schema": 1,
        "pid": pid,
        "object_id": object_id,
        "kiosk_type": kiosk_type,
        "expected_sides": sides,
        "selected_before": before_guard,
        "selected_after": after_guard,
        "exact_hit_count": exact_hits,
        "valid_candidate_count": len(ordered),
        "regions_scanned": region_count,
        "bytes_scanned": bytes_scanned,
        "read_failures": read_failures,
        "elapsed_ms": round((time.monotonic() - started) * 1000, 3),
        "candidates": ordered,
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Read an open LU4 private-shop payload through LU4Memory"
    )
    parser.add_argument("pid", type=int)
    parser.add_argument("object_id", type=int)
    parser.add_argument("kiosk_type", type=int)
    parser.add_argument("controller", type=lambda value: int(value, 0))
    parser.add_argument("--timeout", type=float, default=90.0)
    parser.add_argument("--settle", type=float, default=2.0)
    parser.add_argument(
        "--allow-unselected",
        action="store_true",
        help="scan by exact ObjectID without the selected-trader guard",
    )
    parser.add_argument("--json", type=Path)
    args = parser.parse_args()
    result = scan(
        args.pid,
        args.object_id,
        args.kiosk_type,
        args.controller,
        args.timeout,
        args.settle,
        not args.allow_unselected,
    )
    rendered = json.dumps(result, indent=2, ensure_ascii=False)
    if args.json is not None:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(rendered + "\n", encoding="utf-8")
    print(rendered)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
