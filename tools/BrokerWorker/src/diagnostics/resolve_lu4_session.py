from __future__ import annotations

import argparse
import ctypes
import json
import os
import socket
import struct
import sys
from ctypes import wintypes
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT_ROOT / "client"))

from lu4_memory_client import Lu4MemoryClient  # noqa: E402
from resolve_lu4_direct_hook import executable_sections, read_exact  # noqa: E402
from worker_progress import check_stop


ENCRYPT_FUNCTION_SIGNATURE = bytes.fromhex(
    "41 8B 00 45 32 D2 45 33 C9 4C 8B D9 85 C0 7E 28"
)
IMAGE_SCN_MEM_READ = 0x40000000
MEM_COMMIT = 0x1000
MEM_PRIVATE = 0x20000
MEM_MAPPED = 0x40000
PAGE_NOACCESS = 0x01
PAGE_GUARD = 0x100
PROCESS_QUERY_INFORMATION = 0x0400
PROCESS_VM_READ = 0x0010
SYSTEM_EXTENDED_HANDLE_INFORMATION = 64
MAX_USER_ADDRESS = 0x7FFF_FFFF_FFFF
MAX_CHUNK = 1024 * 1024


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


class SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX(ctypes.Structure):
    _fields_ = [
        ("Object", ctypes.c_void_p),
        ("UniqueProcessId", ctypes.c_size_t),
        ("HandleValue", ctypes.c_size_t),
        ("GrantedAccess", wintypes.ULONG),
        ("CreatorBackTraceIndex", wintypes.USHORT),
        ("ObjectTypeIndex", wintypes.USHORT),
        ("HandleAttributes", wintypes.ULONG),
        ("Reserved", wintypes.ULONG),
    ]


kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
ntdll = ctypes.WinDLL("ntdll")
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
ntdll.NtQuerySystemInformation.argtypes = [
    wintypes.ULONG,
    ctypes.c_void_p,
    wintypes.ULONG,
    ctypes.POINTER(wintypes.ULONG),
]
ntdll.NtQuerySystemInformation.restype = wintypes.LONG


def pe_sections(headers: bytes) -> list[dict[str, int | str]]:
    pe_offset = struct.unpack_from("<I", headers, 0x3C)[0]
    section_count = struct.unpack_from("<H", headers, pe_offset + 6)[0]
    optional_size = struct.unpack_from("<H", headers, pe_offset + 20)[0]
    section_table = pe_offset + 24 + optional_size
    result: list[dict[str, int | str]] = []
    for index in range(section_count):
        offset = section_table + index * 40
        name = headers[offset : offset + 8].split(b"\0", 1)[0].decode(
            "ascii", "replace"
        )
        virtual_size, virtual_address, raw_size = struct.unpack_from(
            "<III", headers, offset + 8
        )
        result.append(
            {
                "name": name,
                "rva": virtual_address,
                "size": max(virtual_size, raw_size),
                "characteristics": struct.unpack_from("<I", headers, offset + 36)[0],
            }
        )
    return result


def scan_range(
    client: Lu4MemoryClient,
    pid: int,
    address: int,
    size: int,
    needle: bytes,
) -> list[int]:
    matches: list[int] = []
    carry = b""
    offset = 0
    while offset < size:
        check_stop()
        requested = min(MAX_CHUNK, size - offset)
        try:
            chunk = read_exact(client, pid, address + offset, requested)
        except (OSError, RuntimeError):
            return matches
        data = carry + chunk
        start = 0
        while True:
            found = data.find(needle, start)
            if found < 0:
                break
            matches.append(address + offset - len(carry) + found)
            start = found + 1
        carry = data[-(len(needle) - 1) :] if len(needle) > 1 else b""
        offset += requested
    return matches


def virtual_regions(pid: int) -> list[tuple[int, int, int, int]]:
    handle = kernel32.OpenProcess(
        PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, False, pid
    )
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    result: list[tuple[int, int, int, int]] = []
    try:
        address = 0
        mbi = MEMORY_BASIC_INFORMATION()
        while address < MAX_USER_ADDRESS:
            check_stop()
            returned = kernel32.VirtualQueryEx(
                handle, ctypes.c_void_p(address), ctypes.byref(mbi), ctypes.sizeof(mbi)
            )
            if not returned:
                break
            base = int(mbi.BaseAddress or 0)
            size = int(mbi.RegionSize)
            if not size:
                break
            if (
                mbi.State == MEM_COMMIT
                and mbi.Type in (MEM_PRIVATE, MEM_MAPPED)
                and not (mbi.Protect & (PAGE_NOACCESS | PAGE_GUARD))
            ):
                result.append((base, size, int(mbi.Protect), int(mbi.Type)))
            address = base + size
    finally:
        kernel32.CloseHandle(handle)
    return result


def system_handles_for_pids(
    pids: set[int],
) -> dict[int, dict[int, dict[str, int]]]:
    needed = wintypes.ULONG()
    size = 1 << 20
    while True:
        buffer = ctypes.create_string_buffer(size)
        status = ntdll.NtQuerySystemInformation(
            SYSTEM_EXTENDED_HANDLE_INFORMATION,
            buffer,
            size,
            ctypes.byref(needed),
        )
        if status == 0:
            break
        if (status & 0xFFFFFFFF) != 0xC0000004:
            raise OSError(f"NtQuerySystemInformation failed: 0x{status & 0xFFFFFFFF:08X}")
        size = max(size * 2, int(needed.value) + 0x10000)

    count = ctypes.c_size_t.from_buffer(buffer, 0).value
    entry_size = ctypes.sizeof(SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX)
    offset = ctypes.sizeof(ctypes.c_size_t) * 2
    handles: dict[int, dict[int, dict[str, int]]] = {pid: {} for pid in pids}
    for index in range(count):
        entry = SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX.from_buffer(
            buffer, offset + index * entry_size
        )
        entry_pid = int(entry.UniqueProcessId)
        if entry_pid in handles:
            handles[entry_pid][int(entry.HandleValue)] = {
                "type_index": int(entry.ObjectTypeIndex),
                "granted_access": int(entry.GrantedAccess),
            }
    return handles


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Read-only LU4 connection/key/socket session resolver."
    )
    parser.add_argument("pid", type=int)
    parser.add_argument("--json", type=Path)
    args = parser.parse_args()

    with Lu4MemoryClient() as client:
        base = client.process_base(args.pid)
        headers = read_exact(client, args.pid, base, 0x1000)
        sections = pe_sections(headers)
        encrypt_matches: list[int] = []
        for section in executable_sections(headers):
            encrypt_matches.extend(
                scan_range(
                    client,
                    args.pid,
                    base + int(section["rva"]),
                    int(section["size"]),
                    ENCRYPT_FUNCTION_SIGNATURE,
                )
            )
        if len(encrypt_matches) != 1:
            raise RuntimeError(
                f"expected one encrypt function signature, found {len(encrypt_matches)}"
            )
        encrypt_function = encrypt_matches[0]

        pointer_matches: list[int] = []
        needle = struct.pack("<Q", encrypt_function)
        for section in sections:
            if int(section["characteristics"]) & IMAGE_SCN_MEM_READ:
                pointer_matches.extend(
                    scan_range(
                        client,
                        args.pid,
                        base + int(section["rva"]),
                        int(section["size"]),
                        needle,
                    )
                )
        vtables = sorted({address - 0x40 for address in pointer_matches})

        candidates: list[dict[str, int | str | list[dict[str, int]]]] = []
        regions = virtual_regions(args.pid)
        probe_socket = socket.socket()
        try:
            probe_value = int(probe_socket.fileno())
            handle_tables = system_handles_for_pids({args.pid, os.getpid()})
            socket_signature = handle_tables[os.getpid()].get(probe_value)
        finally:
            probe_socket.close()
        if socket_signature is None:
            raise RuntimeError("could not determine the current Windows socket handle type")
        target_handles = handle_tables[args.pid]
        for vtable in vtables:
            object_needle = struct.pack("<Q", vtable)
            for region_base, region_size, protect, region_type in regions:
                for address in scan_range(
                    client, args.pid, region_base, region_size, object_needle
                ):
                    try:
                        object_data = read_exact(client, args.pid, address, 0x80)
                        wrapper = struct.unpack_from("<Q", object_data, 0x10)[0]
                        ready = object_data[0x31]
                        if not wrapper or ready != 1:
                            continue
                        wrapper_data = read_exact(client, args.pid, wrapper, 0x80)
                        socket_candidates: list[dict[str, int]] = []
                        for offset in range(8, 0x80, 4):
                            value = struct.unpack_from("<I", wrapper_data, offset)[0]
                            metadata = target_handles.get(value)
                            if (
                                3 < value < 0x1000000
                                and value % 4 == 0
                                and metadata is not None
                                and metadata["type_index"]
                                == socket_signature["type_index"]
                                and metadata["granted_access"]
                                == socket_signature["granted_access"]
                            ):
                                socket_candidates.append(
                                    {
                                        "offset": offset,
                                        "handle": value,
                                        **metadata,
                                    }
                                )
                        candidates.append(
                            {
                                "connection": address,
                                "vtable": vtable,
                                "ready": ready,
                                "rolling_key_address": address + 0x57,
                                "rolling_key_hex": object_data[0x57 : 0x68].hex(" "),
                                "socket_wrapper": wrapper,
                                "socket_candidates": socket_candidates,
                                "region_protect": protect,
                                "region_type": region_type,
                            }
                        )
                    except (OSError, RuntimeError):
                        continue

    result = {
        "pid": args.pid,
        "image_base": base,
        "encrypt_function": encrypt_function,
        "encrypt_signature_matches": encrypt_matches,
        "encrypt_pointer_matches": pointer_matches,
        "candidate_vtables": vtables,
        "socket_handle_signature": socket_signature,
        "connection_candidates": candidates,
        "connection_unique": len(candidates) == 1,
    }
    rendered = json.dumps(result, indent=2)
    print(rendered)
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(rendered + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
