from __future__ import annotations

import argparse
import ctypes
import json
import struct
import subprocess
import sys
import time
from ctypes import wintypes
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
DIAGNOSTICS = PROJECT_ROOT / "diagnostics"
STATE_PATH = PROJECT_ROOT / "build" / "lu4_target_hook_state.json"
HERZ_IMAGE = DIAGNOSTICS / "herzbot-live-reconstructed.exe"
DIRECT_RVA = 0x012C1640
POST_RVA = 0x04C22A4A
DIRECT_ORIGINAL_SIZE = 19
POST_ORIGINAL_SIZE = 15
DIRECT_SIGNATURE = bytes.fromhex(
    "40 55 56 57 41 54 41 55 41 56 41 57 48 81 EC 10 01 00 00"
)
POST_SIGNATURE = bytes.fromhex(
    "4C 8B 74 24 58 48 8B 6C 24 50 48 85 DB 74 1A"
)
CAVE_SIZE = 0x1000
PROCESS_QUERY_INFORMATION = 0x0400
PROCESS_SUSPEND_RESUME = 0x0800
TOKEN_ADJUST_PRIVILEGES = 0x0020
TOKEN_QUERY = 0x0008
SE_PRIVILEGE_ENABLED = 0x00000002
ERROR_NOT_ALL_ASSIGNED = 1300
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value

sys.path.insert(0, str(PROJECT_ROOT / "client"))
sys.path.insert(0, str(DIAGNOSTICS))

from lu4_memory_client import (  # noqa: E402
    Lu4MemoryClient,
    PAGE_EXECUTE_READWRITE,
    build_target_packet,
)
from runtime_stub_builder import run_main_generator, run_post_generator  # noqa: E402


class PROCESS_BASIC_INFORMATION(ctypes.Structure):
    _fields_ = [
        ("Reserved1", ctypes.c_void_p),
        ("PebBaseAddress", ctypes.c_void_p),
        ("Reserved2", ctypes.c_void_p * 2),
        ("UniqueProcessId", ctypes.c_size_t),
        ("Reserved3", ctypes.c_void_p),
    ]


class LUID(ctypes.Structure):
    _fields_ = [("LowPart", wintypes.DWORD), ("HighPart", wintypes.LONG)]


kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
ntdll = ctypes.WinDLL("ntdll", use_last_error=True)
kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
kernel32.OpenProcess.restype = wintypes.HANDLE
kernel32.FlushInstructionCache.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_size_t]
kernel32.FlushInstructionCache.restype = wintypes.BOOL
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.CloseHandle.restype = wintypes.BOOL
ntdll.NtQueryInformationProcess.argtypes = [
    wintypes.HANDLE,
    wintypes.ULONG,
    ctypes.c_void_p,
    wintypes.ULONG,
    ctypes.POINTER(wintypes.ULONG),
]
ntdll.NtQueryInformationProcess.restype = wintypes.LONG
ntdll.NtSuspendProcess.argtypes = [wintypes.HANDLE]
ntdll.NtSuspendProcess.restype = wintypes.LONG
ntdll.NtResumeProcess.argtypes = [wintypes.HANDLE]
ntdll.NtResumeProcess.restype = wintypes.LONG
advapi32 = ctypes.WinDLL("advapi32", use_last_error=True)
advapi32.OpenProcessToken.argtypes = [
    wintypes.HANDLE,
    wintypes.DWORD,
    ctypes.POINTER(wintypes.HANDLE),
]
advapi32.OpenProcessToken.restype = wintypes.BOOL
advapi32.LookupPrivilegeValueW.argtypes = [
    wintypes.LPCWSTR,
    wintypes.LPCWSTR,
    ctypes.POINTER(LUID),
]
advapi32.LookupPrivilegeValueW.restype = wintypes.BOOL
advapi32.AdjustTokenPrivileges.argtypes = [
    wintypes.HANDLE,
    wintypes.BOOL,
    ctypes.c_void_p,
    wintypes.DWORD,
    ctypes.c_void_p,
    ctypes.c_void_p,
]
advapi32.AdjustTokenPrivileges.restype = wintypes.BOOL


class TOKEN_PRIVILEGES_ONE(ctypes.Structure):
    _fields_ = [
        ("PrivilegeCount", wintypes.DWORD),
        ("Luid", LUID),
        ("Attributes", wintypes.DWORD),
    ]


def read_exact(client: Lu4MemoryClient, pid: int, address: int, size: int) -> bytes:
    data = client.read(pid, address, size)
    if len(data) != size:
        raise RuntimeError(
            f"short read at 0x{address:X}: got {len(data)} of {size} bytes"
        )
    return data


def write_exact(client: Lu4MemoryClient, pid: int, address: int, data: bytes) -> None:
    copied = client.write(pid, address, data)
    if copied != len(data):
        raise RuntimeError(
            f"short write at 0x{address:X}: wrote {copied} of {len(data)} bytes"
        )


def process_modules(client: Lu4MemoryClient, pid: int) -> dict[str, int]:
    handle = kernel32.OpenProcess(PROCESS_QUERY_INFORMATION, False, pid)
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        basic = PROCESS_BASIC_INFORMATION()
        returned = wintypes.ULONG()
        status = ntdll.NtQueryInformationProcess(
            handle,
            0,
            ctypes.byref(basic),
            ctypes.sizeof(basic),
            ctypes.byref(returned),
        )
        if status != 0:
            raise OSError(f"NtQueryInformationProcess failed: 0x{status & 0xFFFFFFFF:08X}")
    finally:
        kernel32.CloseHandle(handle)

    peb = int(basic.PebBaseAddress or 0)
    if not peb:
        raise RuntimeError("target PEB address is null")
    ldr = struct.unpack("<Q", read_exact(client, pid, peb + 0x18, 8))[0]
    if not ldr:
        raise RuntimeError("target PEB loader data is null")
    list_head = ldr + 0x20
    link = struct.unpack("<Q", read_exact(client, pid, list_head, 8))[0]
    result: dict[str, int] = {}
    seen: set[int] = set()
    while link != list_head and link not in seen and len(seen) < 512:
        seen.add(link)
        entry = link - 0x10
        raw = read_exact(client, pid, entry, 0x68)
        next_link = struct.unpack_from("<Q", raw, 0x10)[0]
        module_base = struct.unpack_from("<Q", raw, 0x30)[0]
        name_length = struct.unpack_from("<H", raw, 0x58)[0]
        name_buffer = struct.unpack_from("<Q", raw, 0x60)[0]
        if module_base and name_buffer and 0 < name_length <= 512 and name_length % 2 == 0:
            name = read_exact(client, pid, name_buffer, name_length).decode(
                "utf-16-le", "replace"
            )
            result[name.lower()] = module_base
        link = next_link
    if "ntdll.dll" not in result:
        raise RuntimeError("PEB module walk did not find ntdll.dll")
    return result


def local_export_rva(module_name: str, export_name: str) -> int:
    module = ctypes.WinDLL(module_name)
    module_base = int(module._handle)
    export = ctypes.cast(getattr(module, export_name), ctypes.c_void_p).value
    if export is None or export < module_base:
        raise RuntimeError(f"could not resolve {module_name}!{export_name}")
    return export - module_base


def remote_export(modules: dict[str, int], module_name: str, export_name: str) -> int:
    base = modules.get(module_name.lower())
    if base is None:
        raise RuntimeError(f"target has no loaded {module_name}")
    return base + local_export_rva(module_name, export_name)


def flush_instruction_cache(pid: int, address: int, size: int) -> None:
    handle = kernel32.OpenProcess(PROCESS_QUERY_INFORMATION, False, pid)
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        if not kernel32.FlushInstructionCache(handle, ctypes.c_void_p(address), size):
            raise ctypes.WinError(ctypes.get_last_error())
    finally:
        kernel32.CloseHandle(handle)


def enable_debug_privilege() -> None:
    token = wintypes.HANDLE()
    if not advapi32.OpenProcessToken(
        kernel32.GetCurrentProcess(),
        TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY,
        ctypes.byref(token),
    ):
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        privilege = TOKEN_PRIVILEGES_ONE()
        privilege.PrivilegeCount = 1
        if not advapi32.LookupPrivilegeValueW(
            None, "SeDebugPrivilege", ctypes.byref(privilege.Luid)
        ):
            raise ctypes.WinError(ctypes.get_last_error())
        privilege.Attributes = SE_PRIVILEGE_ENABLED
        ctypes.set_last_error(0)
        if not advapi32.AdjustTokenPrivileges(
            token, False, ctypes.byref(privilege), 0, None, None
        ):
            raise ctypes.WinError(ctypes.get_last_error())
        error = ctypes.get_last_error()
        if error == ERROR_NOT_ALL_ASSIGNED:
            raise PermissionError("SeDebugPrivilege is not assigned to this token")
        if error:
            raise ctypes.WinError(error)
    finally:
        kernel32.CloseHandle(token)


def suspend_process(pid: int) -> wintypes.HANDLE:
    enable_debug_privilege()
    handle = kernel32.OpenProcess(PROCESS_SUSPEND_RESUME, False, pid)
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    status = ntdll.NtSuspendProcess(handle)
    if status != 0:
        kernel32.CloseHandle(handle)
        raise OSError(f"NtSuspendProcess failed: 0x{status & 0xFFFFFFFF:08X}")
    return handle


def resume_process(handle: wintypes.HANDLE) -> None:
    try:
        status = ntdll.NtResumeProcess(handle)
        if status != 0:
            raise OSError(f"NtResumeProcess failed: 0x{status & 0xFFFFFFFF:08X}")
    finally:
        kernel32.CloseHandle(handle)


def absolute_jump(target: int, size: int) -> bytes:
    if size < 14:
        raise ValueError("absolute jump needs at least 14 bytes")
    return b"\xFF\x25\x00\x00\x00\x00" + struct.pack("<Q", target) + b"\x90" * (size - 14)


def critical_section_initializer() -> bytes:
    # RTL_CRITICAL_SECTION with DebugInfo=-1 (no debug info), LockCount=-1.
    return struct.pack("<QiIQQQ", 0xFFFFFFFFFFFFFFFF, -1, 0, 0, 0, 0)


def load_state() -> dict[str, object]:
    if not STATE_PATH.exists():
        raise RuntimeError(f"hook state does not exist: {STATE_PATH}")
    return json.loads(STATE_PATH.read_text(encoding="utf-8"))


def save_state(state: dict[str, object]) -> None:
    STATE_PATH.parent.mkdir(parents=True, exist_ok=True)
    temporary = STATE_PATH.with_suffix(".tmp")
    temporary.write_text(json.dumps(state, indent=2) + "\n", encoding="utf-8")
    temporary.replace(STATE_PATH)


def patch_region(
    client: Lu4MemoryClient, pid: int, address: int, replacement: bytes
) -> tuple[int, int, int]:
    aligned_address, aligned_size, old_protection = client.protect_process_memory(
        pid, address, len(replacement), PAGE_EXECUTE_READWRITE
    )
    try:
        write_exact(client, pid, address, replacement)
        flush_instruction_cache(pid, address, len(replacement))
    finally:
        client.protect_process_memory(
            pid, aligned_address, aligned_size, old_protection
        )
    return aligned_address, aligned_size, old_protection


def install(pid: int) -> None:
    if STATE_PATH.exists():
        raise RuntimeError(
            f"state file already exists; inspect/status/uninstall first: {STATE_PATH}"
        )

    with Lu4MemoryClient() as client:
        image_base = client.process_base(pid)
        direct = image_base + DIRECT_RVA
        post = image_base + POST_RVA
        direct_original = read_exact(client, pid, direct, DIRECT_ORIGINAL_SIZE)
        post_original = read_exact(client, pid, post, POST_ORIGINAL_SIZE)
        if direct_original != DIRECT_SIGNATURE:
            raise RuntimeError(f"direct hook signature mismatch at 0x{direct:X}")
        if post_original != POST_SIGNATURE:
            raise RuntimeError(f"post hook signature mismatch at 0x{post:X}")

        modules = process_modules(client, pid)
        send_address = remote_export(modules, "ws2_32.dll", "send")
        enter_cs = remote_export(modules, "ntdll.dll", "RtlEnterCriticalSection")
        leave_cs = remote_export(modules, "ntdll.dll", "RtlLeaveCriticalSection")
        cave, cave_size = client.allocate_process_memory(pid, CAVE_SIZE)
        state: dict[str, object] = {
            "version": 1,
            "pid": pid,
            "image_base": image_base,
            "cave": cave,
            "cave_size": cave_size,
            "direct": direct,
            "post": post,
            "direct_original_hex": direct_original.hex(),
            "post_original_hex": post_original.hex(),
            "send": send_address,
            "enter_critical_section": enter_cs,
            "leave_critical_section": leave_cs,
            "installed": False,
        }
        direct_patched = False
        post_patched = False
        try:
            send_first_five = read_exact(client, pid, send_address, 5)
            send_dispatch: int | None = None
            send_mode = "copied-prologue"
            generator_prologue = send_first_five
            if send_first_five[:1] == b"\xE9":
                # Herz's ws2_32 relay retains the displaced five-byte prologue
                # immediately before an absolute jump back to send+5. Recover
                # it only after validating the exact dispatcher tail, then use
                # our normal copied-prologue trampoline. Calling the relay from
                # the cave changes the successful Herz route and can disconnect
                # the session.
                relay = send_address + 5 + struct.unpack_from(
                    "<i", send_first_five, 1
                )[0]
                relay_bytes = read_exact(client, pid, relay, 65)
                if (
                    relay_bytes[44] != 0x5A
                    or relay_bytes[50:57] != b"\x48\xFF\x25\x00\x00\x00\x00"
                    or struct.unpack_from("<Q", relay_bytes, 57)[0]
                    != send_address + 5
                ):
                    raise RuntimeError(
                        "send E9 target is not the validated Herz relay layout"
                    )
                generator_prologue = relay_bytes[45:50]
                if generator_prologue[:1] in (b"\xE9", b"\xEB"):
                    raise RuntimeError("recovered send prologue is still a branch")
                send_mode = "recovered-prologue-from-e9-relay"
            elif send_first_five[:1] == b"\xEB":
                raise RuntimeError("send entry begins with an unsupported short branch")
            main_stub, trampoline_offset = run_main_generator(
                HERZ_IMAGE, cave, direct, send_address, generator_prologue
            )
            post_stub = run_post_generator(HERZ_IMAGE, cave, post)
            page = bytearray(CAVE_SIZE)
            if send_dispatch is None:
                send_dispatch = cave + 0x80 + trampoline_offset
            struct.pack_into("<Q", page, 0x10, send_dispatch)
            struct.pack_into("<Q", page, 0x18, 0)
            struct.pack_into("<Q", page, 0x20, cave + 0x300)
            struct.pack_into("<Q", page, 0x60, send_address)
            page[0x80 : 0x80 + len(main_stub)] = main_stub
            page[0xD00 : 0xD28] = critical_section_initializer()
            struct.pack_into("<Q", page, 0xD30, enter_cs)
            struct.pack_into("<Q", page, 0xD38, leave_cs)
            page[0xE00 : 0xE00 + len(post_stub)] = post_stub
            write_exact(client, pid, cave, bytes(page))
            flush_instruction_cache(pid, cave, CAVE_SIZE)

            post_patch = absolute_jump(cave + 0xE00, POST_ORIGINAL_SIZE)
            direct_patch = absolute_jump(cave + 0x80, DIRECT_ORIGINAL_SIZE)
            patch_region(client, pid, post, post_patch)
            post_patched = True
            patch_region(client, pid, direct, direct_patch)
            direct_patched = True
            write_exact(client, pid, cave + 0x74, struct.pack("<I", 1))

            state.update(
                {
                    "direct_patch_hex": direct_patch.hex(),
                    "post_patch_hex": post_patch.hex(),
                    "main_stub_size": len(main_stub),
                    "post_stub_size": len(post_stub),
                    "send_mode": send_mode,
                    "send_dispatch": send_dispatch,
                    "installed": True,
                }
            )
            save_state(state)
        except Exception:
            try:
                write_exact(client, pid, cave + 0x74, b"\0\0\0\0")
                if direct_patched:
                    patch_region(client, pid, direct, direct_original)
                if post_patched:
                    patch_region(client, pid, post, post_original)
                client.free_process_memory(pid, cave)
            finally:
                raise

    print(json.dumps(state, indent=2))


def status() -> int:
    state = load_state()
    pid = int(state["pid"])
    with Lu4MemoryClient() as client:
        base = client.process_base(pid)
        if base != int(state["image_base"]):
            raise RuntimeError("PID/image base no longer matches the installed state")
        cave = int(state["cave"])
        direct = int(state["direct"])
        post = int(state["post"])
        result = {
            "pid": pid,
            "image_base": base,
            "cave": cave,
            "enabled": struct.unpack("<I", read_exact(client, pid, cave + 0x74, 4))[0],
            "primary_trigger": struct.unpack("<I", read_exact(client, pid, cave, 4))[0],
            "type2_trigger": struct.unpack("<I", read_exact(client, pid, cave + 0x0C, 4))[0],
            "status": struct.unpack("<I", read_exact(client, pid, cave + 8, 4))[0],
            "phase": struct.unpack("<I", read_exact(client, pid, cave + 0xDC8, 4))[0],
            "last_send_result": struct.unpack("<i", read_exact(client, pid, cave + 0x58, 4))[0],
            "direct_patch_matches": read_exact(client, pid, direct, DIRECT_ORIGINAL_SIZE).hex()
            == str(state["direct_patch_hex"]),
            "post_patch_matches": read_exact(client, pid, post, POST_ORIGINAL_SIZE).hex()
            == str(state["post_patch_hex"]),
        }
    print(json.dumps(result, indent=2))
    return 0 if result["direct_patch_matches"] and result["post_patch_matches"] else 2


def uninstall() -> None:
    state = load_state()
    pid = int(state["pid"])
    with Lu4MemoryClient() as client:
        if client.process_base(pid) != int(state["image_base"]):
            raise RuntimeError("PID/image base no longer matches; refusing stale rollback")
        cave = int(state["cave"])
        direct = int(state["direct"])
        post = int(state["post"])
        write_exact(client, pid, cave + 0x74, b"\0\0\0\0")
        patch_region(client, pid, direct, bytes.fromhex(str(state["direct_original_hex"])))
        patch_region(client, pid, post, bytes.fromhex(str(state["post_original_hex"])))
        client.free_process_memory(pid, cave)
    STATE_PATH.unlink()
    print("uninstalled and restored both original prologues")


def run_resolver(script: str, pid: int, output: Path) -> dict[str, object]:
    command = [sys.executable, str(DIAGNOSTICS / script), str(pid), "--json", str(output)]
    completed = subprocess.run(
        command, cwd=PROJECT_ROOT, capture_output=True, text=True, timeout=180
    )
    if completed.returncode != 0:
        raise RuntimeError(
            f"{script} failed ({completed.returncode}):\n"
            f"{completed.stdout}\n{completed.stderr}"
        )
    return json.loads(output.read_text(encoding="utf-8"))


def select_target(
    object_id: int,
    origin_x: float,
    origin_y: float,
    origin_z: float,
    force_attack: bool,
    fast: bool = False,
    payload_override: bytes | None = None,
    packet_name: str = "target_action",
) -> None:
    state = load_state()
    pid = int(state["pid"])
    cave = int(state["cave"])
    session_path = DIAGNOSTICS / "latest_session.json"
    try:
        if fast:
            if not session_path.exists():
                raise RuntimeError("fast select requires latest_session.json")
            session = json.loads(session_path.read_text(encoding="utf-8"))
            if (
                int(session.get("pid", -1)) != pid
                or int(session.get("image_base", 0)) != int(state["image_base"])
            ):
                raise RuntimeError("cached session does not match the live hook state")
        else:
            session = run_resolver("resolve_lu4_session.py", pid, session_path)
    except RuntimeError:
        # A temporary plaintext-capture hook replaces the encrypt prologue, so
        # signature discovery cannot see it while the hook is armed. Reuse only
        # the live session snapshot for this exact PID/image; all pointers below
        # are still validated again by the normal send path.
        capture_path = PROJECT_ROOT / "build" / "encrypt_plaintext_capture_state.json"
        if not capture_path.exists() or not session_path.exists():
            raise
        capture = json.loads(capture_path.read_text(encoding="utf-8"))
        session = json.loads(session_path.read_text(encoding="utf-8"))
        if (
            int(capture.get("pid", -1)) != pid
            or int(session.get("pid", -1)) != pid
            or int(session.get("image_base", 0)) != int(state["image_base"])
            or int(capture.get("function", 0)) != int(session.get("encrypt_function", -1))
        ):
            raise RuntimeError("cached session does not match the armed capture hook")
    connections = session.get("connection_candidates", [])
    if not session.get("connection_unique") or len(connections) != 1:
        raise RuntimeError("session resolver did not return one connection")
    connection = connections[0]
    sockets = connection.get("socket_candidates", [])
    if len(sockets) != 1:
        raise RuntimeError("connection did not contain exactly one validated socket")
    socket_field = int(connection["socket_wrapper"]) + int(sockets[0]["offset"])
    rolling_key = int(connection["rolling_key_address"])
    active64_path = DIAGNOSTICS / "latest_active64_state.json"
    if fast:
        if not active64_path.exists():
            raise RuntimeError("fast select requires latest_active64_state.json")
        active64 = json.loads(active64_path.read_text(encoding="utf-8"))
        if int(active64.get("pid", -1)) != pid:
            raise RuntimeError("cached active64 state belongs to another PID")
    else:
        active64 = run_resolver(
            "resolve_active64_state.py",
            pid,
            active64_path,
        )
    matching_slots = active64.get("matching_slots", [])
    if not active64.get("matching_slot_unique") or len(matching_slots) != 1:
        raise RuntimeError("active64 resolver did not return one PID slot")
    state_offset = int(matching_slots[0]["selected_state_module_offset"])

    # Herz resolves these three values from the local player immediately before
    # constructing RequestAction. They are the player's origin, not the target's.
    payload = (
        payload_override
        if payload_override is not None
        else build_target_packet(
            object_id, origin_x, origin_y, origin_z, force_attack
        )
    )
    if not payload:
        raise ValueError("packet payload must not be empty")
    wire = struct.pack("<H", len(payload) + 2) + payload
    with Lu4MemoryClient() as client:
        if client.process_base(pid) != int(state["image_base"]):
            raise RuntimeError("PID/image base no longer matches hook state")
        write_exact(client, pid, cave + 0x18, struct.pack("<Q", socket_field))
        write_exact(client, pid, cave + 0x300, wire)
        write_exact(client, pid, cave + 4, struct.pack("<I", len(wire)))
        write_exact(client, pid, cave + 8, b"\0" * 4)
        write_exact(client, pid, cave + 0xDC8, b"\0" * 8)
        write_exact(client, pid, cave + 0x0C, b"\0" * 4)
        write_exact(client, pid, cave, struct.pack("<I", 1))

        deadline = time.monotonic() + 3.0
        while time.monotonic() < deadline:
            if struct.unpack("<I", read_exact(client, pid, cave + 8, 4))[0] == 1:
                break
            time.sleep(0.002)
        else:
            write_exact(client, pid, cave + 0xDC8, struct.pack("<I", 3))
            raise TimeoutError("phase1 timeout: game-thread hook did not claim command")

        try:
            encrypted = client.encrypt_packet(
                pid,
                cave + 0x302,
                len(payload),
                rolling_key,
                state_index=1,
                state_offset=state_offset,
            )
            if encrypted != len(payload):
                raise RuntimeError(f"packet crypto changed only {encrypted} bytes")
        except Exception:
            write_exact(client, pid, cave + 0xDC8, struct.pack("<I", 3))
            raise
        write_exact(client, pid, cave + 0xDC8, struct.pack("<I", 2))

        deadline = time.monotonic() + 1.0
        while time.monotonic() < deadline:
            trigger = struct.unpack("<I", read_exact(client, pid, cave, 4))[0]
            if trigger == 0:
                result = struct.unpack("<i", read_exact(client, pid, cave + 0x58, 4))[0]
                print(
                    json.dumps(
                        {
                            "packet": packet_name,
                            "object_id": object_id,
                            "wire_length": len(wire),
                            "payload_length": len(payload),
                            "send_result": result,
                        },
                        indent=2,
                    )
                )
                return
            time.sleep(0.002)
        raise TimeoutError("phase2 timeout: send did not complete")


def main() -> int:
    parser = argparse.ArgumentParser(description="LU4 target-only DirectHook controller")
    sub = parser.add_subparsers(dest="command", required=True)
    install_parser = sub.add_parser("install")
    install_parser.add_argument("pid", type=int)
    sub.add_parser("status")
    sub.add_parser("uninstall")
    select_parser = sub.add_parser("select")
    select_parser.add_argument("object_id", type=int)
    select_parser.add_argument("origin_x", type=float)
    select_parser.add_argument("origin_y", type=float)
    select_parser.add_argument("origin_z", type=float)
    select_parser.add_argument("--force-attack", action="store_true")
    select_parser.add_argument(
        "--fast",
        action="store_true",
        help="reuse PID/image-bound session caches; driver guards remain active",
    )
    cancel_parser = sub.add_parser("cancel")
    cancel_parser.add_argument(
        "--fast",
        action="store_true",
        help="reuse PID/image-bound session caches; driver guards remain active",
    )
    args = parser.parse_args()

    if args.command == "install":
        install(args.pid)
    elif args.command == "status":
        return status()
    elif args.command == "uninstall":
        uninstall()
    elif args.command == "select":
        select_target(
            args.object_id,
            args.origin_x,
            args.origin_y,
            args.origin_z,
            args.force_attack,
            args.fast,
        )
    elif args.command == "cancel":
        # Herz SendTargetCancel serializes the bodyless
        # RequestTargetCanceld client packet (opcode 0x48).
        select_target(
            0,
            0.0,
            0.0,
            0.0,
            False,
            args.fast,
            payload_override=b"\x48",
            packet_name="target_cancel",
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
