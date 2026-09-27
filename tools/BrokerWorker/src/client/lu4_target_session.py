from __future__ import annotations

import argparse
import ctypes
import json
import subprocess
import sys
import time
from ctypes import wintypes
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
CONTROLLER = PROJECT_ROOT / "client" / "lu4_target_controller.py"
SCANNER = PROJECT_ROOT / "diagnostics" / "scan_lu4_actors.py"
SHOP_READER = PROJECT_ROOT / "diagnostics" / "read_open_shop.py"
STATE_PATH = PROJECT_ROOT / "build" / "lu4_target_hook_state.json"
SNAPSHOT_PATH = PROJECT_ROOT / "diagnostics" / "latest_actor_snapshot.json"
SHOP_SNAPSHOT_PATH = PROJECT_ROOT / "diagnostics" / "latest_open_shop.json"
SESSION_PATH = PROJECT_ROOT / "diagnostics" / "latest_session.json"
ACTIVE64_STATE_PATH = PROJECT_ROOT / "diagnostics" / "latest_active64_state.json"
TH32CS_SNAPPROCESS = 0x00000002
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value

sys.path.insert(0, str(PROJECT_ROOT / "client"))
from lu4_memory_client import Lu4MemoryClient  # noqa: E402
from lu4_target_controller import read_exact  # noqa: E402
from session_cache import reuse  # noqa: E402
from worker_progress import publish, check_stop


class PROCESSENTRY32W(ctypes.Structure):
    _fields_ = [
        ("dwSize", wintypes.DWORD),
        ("cntUsage", wintypes.DWORD),
        ("th32ProcessID", wintypes.DWORD),
        ("th32DefaultHeapID", ctypes.c_size_t),
        ("th32ModuleID", wintypes.DWORD),
        ("cntThreads", wintypes.DWORD),
        ("th32ParentProcessID", wintypes.DWORD),
        ("pcPriClassBase", wintypes.LONG),
        ("dwFlags", wintypes.DWORD),
        ("szExeFile", wintypes.WCHAR * 260),
    ]


kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
kernel32.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
kernel32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
kernel32.Process32FirstW.argtypes = [wintypes.HANDLE, ctypes.POINTER(PROCESSENTRY32W)]
kernel32.Process32FirstW.restype = wintypes.BOOL
kernel32.Process32NextW.argtypes = [wintypes.HANDLE, ctypes.POINTER(PROCESSENTRY32W)]
kernel32.Process32NextW.restype = wintypes.BOOL
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.CloseHandle.restype = wintypes.BOOL


def lu4_pids() -> list[int]:
    snapshot = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if snapshot == INVALID_HANDLE_VALUE:
        raise ctypes.WinError(ctypes.get_last_error())
    result: list[int] = []
    try:
        entry = PROCESSENTRY32W()
        entry.dwSize = ctypes.sizeof(entry)
        if not kernel32.Process32FirstW(snapshot, ctypes.byref(entry)):
            raise ctypes.WinError(ctypes.get_last_error())
        while True:
            if entry.szExeFile.casefold() == "lu4.bin":
                result.append(int(entry.th32ProcessID))
            if not kernel32.Process32NextW(snapshot, ctypes.byref(entry)):
                break
    finally:
        kernel32.CloseHandle(snapshot)
    return sorted(result)


def resolve_pid(explicit_pid: int | None) -> int:
    pids = lu4_pids()
    if explicit_pid is not None:
        if explicit_pid not in pids:
            raise RuntimeError(f"PID {explicit_pid} is not a running lu4.bin process")
        return explicit_pid
    if not pids:
        raise RuntimeError("lu4.bin is not running; start the client and enter the game")
    if len(pids) != 1:
        raise RuntimeError(f"multiple LU4 clients are running {pids}; pass --pid")
    return pids[0]


def run_python(script: Path, *arguments: object, timeout: int = 240) -> str:
    command = [sys.executable, str(script), *(str(value) for value in arguments)]
    completed = subprocess.run(
        command,
        cwd=PROJECT_ROOT,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
    )
    if completed.returncode != 0:
        details = "\n".join(
            part.strip() for part in (completed.stdout, completed.stderr) if part.strip()
        )
        raise RuntimeError(f"command failed ({completed.returncode}): {' '.join(command)}\n{details}")
    return completed.stdout.strip()


def load_state() -> dict[str, object] | None:
    if not STATE_PATH.exists():
        return None
    return json.loads(STATE_PATH.read_text(encoding="utf-8"))


def remove_stale_state(current_pid: int | None = None) -> bool:
    state = load_state()
    if state is None:
        return False
    state_pid = int(state["pid"])
    running = lu4_pids()
    if state_pid in running:
        if current_pid is not None and state_pid != current_pid:
            raise RuntimeError(
                f"hooks belong to another live LU4 PID {state_pid}; clean it up explicitly"
            )
        return False
    STATE_PATH.unlink()
    return True


def scan(pid: int) -> dict[str, object]:
    run_python(SCANNER, pid, "--json", SNAPSHOT_PATH)
    return json.loads(SNAPSHOT_PATH.read_text(encoding="utf-8"))


def prepare(pid: int) -> dict[str, object]:
    check_stop()
    stale_removed = remove_stale_state(pid)
    state = load_state()
    installed_now = False
    if state is None:
        run_python(CONTROLLER, "install", pid)
        installed_now = True
    elif int(state["pid"]) != pid:
        raise RuntimeError(f"hook state belongs to live PID {state['pid']}, not {pid}")
    status = json.loads(run_python(CONTROLLER, "status"))
    if not status["direct_patch_matches"] or not status["post_patch_matches"]:
        raise RuntimeError("installed hook bytes do not match the saved state")
    publish('Reading current character and actor bindings')
    snapshot = scan(pid)
    check_stop()
    publish('Resolving client connection; first setup may take a few minutes')
    session = "live cache" if reuse(pid, SESSION_PATH, 'session') else run_python(
        PROJECT_ROOT / "diagnostics" / "resolve_lu4_session.py",
        pid,
        "--json",
        SESSION_PATH,
    )
    check_stop()
    publish('Validating this process encryption state')
    active64 = "live cache" if reuse(pid, ACTIVE64_STATE_PATH, 'active64') else run_python(
        PROJECT_ROOT / "diagnostics" / "resolve_active64_state.py",
        pid,
        "--json",
        ACTIVE64_STATE_PATH,
    )
    return {
        "pid": pid,
        "stale_state_removed": stale_removed,
        "installed_now": installed_now,
        "hook_status": status,
        "controller": snapshot["controller"],
        "player": snapshot["player"],
        "actor_count": snapshot["actor_count"],
        "named_actor_count": snapshot["named_actor_count"],
        "trader_count": snapshot["trader_count"],
        "session_refreshed": bool(session),
        "active64_state_refreshed": bool(active64),
    }


def choose_target(
    snapshot: dict[str, object],
    name: str | None,
    object_id: int | None,
    nearest_trader: bool,
    nearest_named: bool,
) -> dict[str, object]:
    named = list(snapshot.get("named", []))
    if name is not None:
        matches = [actor for actor in named if str(actor.get("name", "")).casefold() == name.casefold()]
    elif object_id is not None:
        matches = [actor for actor in named if int(actor.get("object_id", 0)) == object_id]
    elif nearest_trader:
        matches = list(snapshot.get("traders", []))
    elif nearest_named:
        matches = [actor for actor in named if not actor.get("is_player")]
    else:
        raise RuntimeError("select requires --name, --object-id, --nearest-trader, or --nearest-named")
    matches = [actor for actor in matches if int(actor.get("object_id", 0)) > 0 and not actor.get("is_player")]
    matches.sort(key=lambda actor: float(actor.get("distance", float("inf"))))
    if not matches:
        raise RuntimeError("no matching live actor was found")
    if name is not None and len(matches) > 1:
        ids = [int(actor["object_id"]) for actor in matches]
        raise RuntimeError(f"name is ambiguous; matching ObjectIDs: {ids}")
    return matches[0]


def verify_selected(
    pid: int,
    controller: int,
    expected_object_id: int,
    timeout_seconds: float = 5.0,
) -> dict[str, object]:
    import struct

    started = time.monotonic()
    deadline = started + timeout_seconds
    actor = 0
    actual_object_id: int | None = None
    with Lu4MemoryClient() as client:
        while True:
            actor = struct.unpack(
                "<Q", read_exact(client, pid, controller + 0x898, 8)
            )[0]
            actual_object_id = (
                struct.unpack("<i", read_exact(client, pid, actor + 0x550, 4))[0]
                if actor
                else None
            )
            if actual_object_id == expected_object_id or time.monotonic() >= deadline:
                break
            time.sleep(0.01)
    if actual_object_id != expected_object_id:
        raise RuntimeError(
            f"selection was not confirmed: expected {expected_object_id}, got {actual_object_id}"
        )
    return {
        "selected_actor": f"0x{actor:X}",
        "selected_object_id": actual_object_id,
        "confirmation_ms": round((time.monotonic() - started) * 1000, 3),
    }


def select(args: argparse.Namespace) -> dict[str, object]:
    pid = resolve_pid(args.pid)
    preparation = prepare(pid)
    snapshot = json.loads(SNAPSHOT_PATH.read_text(encoding="utf-8"))
    target = choose_target(
        snapshot,
        args.name,
        args.object_id,
        args.nearest_trader,
        args.nearest_named,
    )
    player = snapshot["player"]
    output = run_python(
        CONTROLLER,
        "select",
        int(target["object_id"]),
        float(player["x"]),
        float(player["y"]),
        float(player["z"]),
    )
    send_result = json.loads(output)
    confirmation = verify_selected(pid, int(str(snapshot["controller"]), 16), int(target["object_id"]))
    return {
        "pid": pid,
        "target": {
            "name": target.get("name", ""),
            "object_id": int(target["object_id"]),
            "actor": target["actor"],
            "distance": float(target["distance"]),
        },
        "send": send_result,
        "confirmation": confirmation,
        "installed_now": preparation["installed_now"],
        "stale_state_removed": preparation["stale_state_removed"],
    }


def list_actors(pid: int, traders_only: bool) -> dict[str, object]:
    preparation = prepare(pid)
    snapshot = json.loads(SNAPSHOT_PATH.read_text(encoding="utf-8"))
    actors = snapshot["traders"] if traders_only else snapshot["named"]
    return {
        "pid": pid,
        "player": snapshot["player"],
        "actors": [
            {
                "name": actor.get("name", ""),
                "object_id": actor.get("object_id"),
                "actor": actor.get("actor"),
                "distance": actor.get("distance"),
                "kiosk_type": actor.get("kiosk_type"),
            }
            for actor in actors
            if not actor.get("is_player") and int(actor.get("object_id", 0)) > 0
        ],
        "installed_now": preparation["installed_now"],
        "stale_state_removed": preparation["stale_state_removed"],
    }


def cleanup() -> dict[str, object]:
    state = load_state()
    if state is None:
        return {"cleaned": False, "reason": "no hook state"}
    pid = int(state["pid"])
    if pid in lu4_pids():
        run_python(CONTROLLER, "uninstall")
        return {"cleaned": True, "pid": pid, "mode": "uninstalled-live-hooks"}
    STATE_PATH.unlink()
    return {"cleaned": True, "pid": pid, "mode": "removed-stale-state"}


def read_shop(pid: int, timeout_seconds: float) -> dict[str, object]:
    import struct

    preparation = prepare(pid)
    snapshot = json.loads(SNAPSHOT_PATH.read_text(encoding="utf-8"))
    controller = int(str(snapshot["controller"]), 16)
    with Lu4MemoryClient() as client:
        actor = struct.unpack(
            "<Q", read_exact(client, pid, controller + 0x898, 8)
        )[0]
        if not actor:
            raise RuntimeError("there is no selected actor")
        object_id = struct.unpack(
            "<i", read_exact(client, pid, actor + 0x550, 4)
        )[0]
        kiosk_type = struct.unpack(
            "<i", read_exact(client, pid, actor + 0x7DC, 4)
        )[0]
    actor_record = next(
        (
            item
            for item in snapshot.get("named", [])
            if int(str(item.get("actor", "0")), 16) == actor
        ),
        None,
    )
    output = run_python(
        SHOP_READER,
        pid,
        object_id,
        kiosk_type,
        hex(controller),
        "--timeout",
        timeout_seconds,
        "--json",
        SHOP_SNAPSHOT_PATH,
        timeout=max(30, int(timeout_seconds) + 15),
    )
    result = json.loads(output)
    result["selected_name"] = actor_record.get("name", "") if actor_record else ""
    result["installed_now"] = preparation["installed_now"]
    result["stale_state_removed"] = preparation["stale_state_removed"]
    SHOP_SNAPSHOT_PATH.write_text(
        json.dumps(result, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description="Restart-safe LU4 target session helper")
    parser.add_argument("--pid", type=int, help="required only when several LU4 clients run")
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("prepare", help="install hooks and refresh the actor snapshot")
    list_parser = commands.add_parser("list", help="refresh and list live named actors")
    list_parser.add_argument("--traders", action="store_true", help="show private-store actors only")
    select_parser = commands.add_parser("select", help="refresh, select, and verify one live actor")
    group = select_parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--name")
    group.add_argument("--object-id", type=int)
    group.add_argument("--nearest-trader", action="store_true")
    group.add_argument("--nearest-named", action="store_true")
    shop_parser = commands.add_parser("shop", help="read the currently open non-empty shop")
    shop_parser.add_argument("--timeout", type=float, default=90.0)
    commands.add_parser("status", help="show the installed hook state")
    commands.add_parser("cleanup", help="uninstall live hooks or remove stale state")
    args = parser.parse_args()

    if args.command == "cleanup":
        result = cleanup()
    elif args.command == "status":
        remove_stale_state()
        if load_state() is None:
            result = {"installed": False}
        else:
            result = json.loads(run_python(CONTROLLER, "status"))
            result["installed"] = True
    else:
        pid = resolve_pid(args.pid)
        if args.command == "prepare":
            result = prepare(pid)
        elif args.command == "list":
            result = list_actors(pid, args.traders)
        elif args.command == "shop":
            result = read_shop(pid, args.timeout)
        else:
            result = select(args)
    print(json.dumps(result, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
