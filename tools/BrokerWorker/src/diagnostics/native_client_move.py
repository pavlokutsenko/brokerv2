from __future__ import annotations

import json
import math
import struct
import sys
import time
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "client"))
sys.path.insert(0, str(ROOT / "diagnostics"))

from inspect_shop_ufunctions import decode_name, read_object  # noqa: E402
from invoke_process_event import invoke  # noqa: E402
from lu4_memory_client import Lu4MemoryClient  # noqa: E402
from no_ui_target_shop_cycle import player_position  # noqa: E402
from scan_lu4_actors import Memory, pointer  # noqa: E402


MOVE_BY_KEYBOARD_OBJECT_INDEX = 248406
MOVE_BY_KEYBOARD_NAME = "Move to Location by Keyboard"


def resolve_move_function(client: Lu4MemoryClient, pid: int) -> int:
    globals_data = json.loads(
        (ROOT / "diagnostics" / "latest_unreal_globals.json").read_text(
            encoding="utf-8"
        )
    )
    base = client.process_base(pid)
    gobjects = base + int(globals_data["gobjects_candidates"][0]["rva"])
    fname_pool = base + int(globals_data["fname_pool_candidates"][0]["rva"])
    memory = Memory(client, pid)
    function = read_object(memory, gobjects, MOVE_BY_KEYBOARD_OBJECT_INDEX)
    if not pointer(function):
        raise RuntimeError("Move to Location by Keyboard UObject is unavailable")
    name = decode_name(memory, fname_pool, memory.i32(function + 0x18, -1))
    if name != MOVE_BY_KEYBOARD_NAME:
        raise RuntimeError(
            "movement UObject index changed: "
            f"index={MOVE_BY_KEYBOARD_OBJECT_INDEX} name={name!r}"
        )
    return function


def move_and_wait(
    pid: int,
    controller: int,
    player_actor: int,
    target_x: float,
    target_y: float,
    arrival_radius: float,
    timeout: float,
) -> dict[str, object]:
    with Lu4MemoryClient() as client:
        source = player_position(client, pid, player_actor)
        function = resolve_move_function(client, pid)
    destination = {"x": target_x, "y": target_y, "z": source["z"]}
    params = bytearray(32)
    struct.pack_into("<ddd", params, 0, target_x, target_y, source["z"])
    result = invoke(controller, function, bytes(params))
    request = {
        "destination": destination,
        "blueprint_time": struct.unpack_from("<d", result, 24)[0],
    }

    deadline = time.monotonic() + timeout
    last_progress_at = time.monotonic()
    best_distance = math.hypot(target_x - source["x"], target_y - source["y"])
    current = source
    samples = 0
    while time.monotonic() < deadline:
        time.sleep(0.02)
        with Lu4MemoryClient() as client:
            current = player_position(client, pid, player_actor)
        samples += 1
        distance = math.hypot(target_x - current["x"], target_y - current["y"])
        if distance + 0.5 < best_distance:
            best_distance = distance
            last_progress_at = time.monotonic()
        if distance <= arrival_radius:
            return {
                "reached": True,
                "source": source,
                "current": current,
                "request": request,
                "final_distance": distance,
                "samples": samples,
            }
        if time.monotonic() - last_progress_at >= 3.0:
            raise TimeoutError(
                "native client movement stalled: "
                f"best_distance={best_distance:.2f}, current_distance={distance:.2f}"
            )
    raise TimeoutError(
        "native client movement timeout: "
        f"best_distance={best_distance:.2f}, arrival_radius={arrival_radius:.2f}"
    )
