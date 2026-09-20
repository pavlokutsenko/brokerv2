from __future__ import annotations

import struct
import sys
import time
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "client"))
sys.path.insert(0, str(ROOT / "diagnostics"))

from lu4_memory_client import Lu4MemoryClient  # noqa: E402
from lu4_target_controller import read_exact, write_exact  # noqa: E402
from process_event_shop_capture import load_state  # noqa: E402


def invoke(object_address: int, function_address: int, params: bytes) -> bytes:
    state = load_state()
    if int(state.get("version", 0)) < 9:
        raise RuntimeError("ProcessEvent command bridge requires capture version 9")
    pid = int(state["pid"])
    command = int(state["command_address"])
    params_address = int(state["command_params_address"])
    if len(params) > 0xE00:
        raise ValueError("ProcessEvent params exceed command buffer")

    with Lu4MemoryClient() as client:
        trigger, status = struct.unpack("<II", read_exact(client, pid, command, 8))
        if trigger or status:
            raise RuntimeError(
                f"ProcessEvent command bridge is busy: trigger={trigger} status={status}"
            )
        write_exact(client, pid, params_address, params or b"\0")
        write_exact(
            client,
            pid,
            command + 4,
            struct.pack("<IQQQ", 0, object_address, function_address, params_address),
        )
        write_exact(client, pid, command, struct.pack("<I", 1))
        deadline = time.monotonic() + 2.0
        while time.monotonic() < deadline:
            status = struct.unpack("<I", read_exact(client, pid, command + 4, 4))[0]
            if status == 2:
                result = read_exact(client, pid, params_address, max(1, len(params)))
                write_exact(client, pid, command + 4, b"\0\0\0\0")
                return result[: len(params)]
            time.sleep(0.001)
    raise TimeoutError("game-thread ProcessEvent command was not consumed")
