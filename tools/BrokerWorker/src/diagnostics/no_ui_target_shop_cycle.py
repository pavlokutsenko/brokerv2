from __future__ import annotations

import argparse
import contextlib
import io
import json
import struct
import sys
import threading
import time
import math
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "client"))
sys.path.insert(0, str(ROOT / "diagnostics"))

from lu4_memory_client import Lu4MemoryClient  # noqa: E402
from lu4_target_controller import select_target  # noqa: E402
from read_open_shop import scan  # noqa: E402


def selected(client: Lu4MemoryClient, pid: int, controller: int) -> tuple[int, int]:
    actor = struct.unpack("<Q", client.read(pid, controller + 0x898, 8))[0]
    object_id = (
        struct.unpack("<i", client.read(pid, actor + 0x550, 4))[0] if actor else 0
    )
    return actor, object_id


def write_selected(client: Lu4MemoryClient, pid: int, controller: int, actor: int) -> None:
    address = controller + 0x898
    copied = client.write(pid, address, struct.pack("<Q", actor))
    if copied != 8:
        raise RuntimeError(f"short selected-pointer write: {copied} of 8")


def player_position(
    client: Lu4MemoryClient, pid: int, player_actor: int
) -> dict[str, float]:
    capsule = struct.unpack("<Q", client.read(pid, player_actor + 0x1A0, 8))[0]
    if not capsule:
        raise RuntimeError("player capsule is null")
    x, y, z = struct.unpack("<ddd", client.read(pid, capsule + 0x1F0, 24))
    return {"x": x, "y": y, "z": z}


def distance(a: dict[str, float], b: dict[str, float]) -> float:
    return math.sqrt(sum((float(a[key]) - float(b[key])) ** 2 for key in "xyz"))


def send(object_id: int, player: dict[str, float]) -> dict:
    output = io.StringIO()
    with contextlib.redirect_stdout(output):
        select_target(
            object_id,
            float(player["x"]),
            float(player["y"]),
            float(player["z"]),
            False,
            fast=True,
        )
    return json.loads(output.getvalue())


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Test server target/open packets with the local target pointer hidden"
    )
    target = parser.add_mutually_exclusive_group(required=True)
    target.add_argument("--name")
    target.add_argument("--object-id", type=int)
    parser.add_argument("--timeout", type=float, default=15.0)
    parser.add_argument(
        "--second-origin",
        choices=("player", "trader"),
        default="player",
        help="coordinates placed into the repeated action packet",
    )
    parser.add_argument("--json", type=Path)
    args = parser.parse_args()

    snapshot = json.loads(
        (ROOT / "diagnostics" / "latest_actor_snapshot.json").read_text(
            encoding="utf-8"
        )
    )
    pid = int(snapshot["pid"])
    controller = int(snapshot["controller"], 16)
    matches = [
        actor
        for actor in snapshot.get("traders", [])
        if (
            args.object_id is not None
            and int(actor.get("object_id", 0)) == args.object_id
        )
        or (
            args.name is not None
            and str(actor.get("name", "")).casefold() == args.name.casefold()
        )
    ]
    if len(matches) != 1:
        raise RuntimeError(f"expected one matching trader, found {len(matches)}")
    trader = matches[0]
    object_id = int(trader["object_id"])
    kiosk_type = int(trader["kiosk_type"])
    player_actor = int(snapshot["player_actor"], 16)
    trader_position = {key: float(trader[key]) for key in "xyz"}

    overall = time.perf_counter()
    scan_result: dict | None = None
    scan_error: BaseException | None = None
    timings: dict[str, float] = {}
    first: dict | None = None
    second: dict | None = None
    actor_seen = 0
    object_seen = 0

    with Lu4MemoryClient() as client:
        # This is a local-only pointer change. No ESC or target-clear packet is sent.
        if selected(client, pid, controller)[0]:
            write_selected(client, pid, controller, 0)

        position_before = player_position(client, pid, player_actor)
        started = time.perf_counter()
        first = send(object_id, position_before)
        deadline = time.perf_counter() + 2.0
        while time.perf_counter() < deadline:
            actor_seen, object_seen = selected(client, pid, controller)
            if object_seen == object_id:
                break
            time.sleep(0.001)
        else:
            raise RuntimeError("first packet did not receive the expected target acknowledgement")
        timings["first_ack_ms"] = (time.perf_counter() - started) * 1000

        started = time.perf_counter()
        write_selected(client, pid, controller, 0)
        if selected(client, pid, controller)[0] != 0:
            raise RuntimeError("local target pointer did not clear")
        timings["local_hide_ms"] = (time.perf_counter() - started) * 1000

        def capture() -> None:
            nonlocal scan_result, scan_error
            try:
                scan_result = scan(
                    pid,
                    object_id,
                    kiosk_type,
                    controller,
                    args.timeout,
                    settle_seconds=0.0,
                    require_selected=False,
                )
            except BaseException as error:
                scan_error = error

        worker = threading.Thread(target=capture, name="unselected-shop-scan", daemon=True)
        worker.start()
        second_origin = (
            player_position(client, pid, player_actor)
            if args.second_origin == "player"
            else trader_position
        )
        started = time.perf_counter()
        second = send(object_id, second_origin)
        timings["second_send_ms"] = (time.perf_counter() - started) * 1000
        movement_samples = [player_position(client, pid, player_actor)]
        movement_deadline = time.perf_counter() + args.timeout + 2.0
        while worker.is_alive() and time.perf_counter() < movement_deadline:
            time.sleep(0.01)
            movement_samples.append(player_position(client, pid, player_actor))
        if worker.is_alive():
            raise TimeoutError("shop scan did not finish")
        if scan_error is not None:
            raise scan_error
        if scan_result is None:
            raise RuntimeError("shop scan returned no result")

        actor_after_response, object_after_response = selected(client, pid, controller)
        if actor_after_response:
            write_selected(client, pid, controller, 0)
        actor_final, object_final = selected(client, pid, controller)
        position_after = player_position(client, pid, player_actor)
        maximum_displacement = max(
            distance(position_before, sample) for sample in movement_samples
        )

    result = {
        "schema": 1,
        "pid": pid,
        "name": trader.get("name", ""),
        "object_id": object_id,
        "kiosk_type": kiosk_type,
        "first_action": first,
        "target_ack": {"actor": f"0x{actor_seen:X}", "object_id": object_seen},
        "second_action": second,
        "second_origin_mode": args.second_origin,
        "second_origin": second_origin,
        "shop": scan_result,
        "after_response": {
            "actor": f"0x{actor_after_response:X}",
            "object_id": object_after_response,
        },
        "final": {"actor": f"0x{actor_final:X}", "object_id": object_final},
        "movement": {
            "position_before": position_before,
            "position_after": position_after,
            "maximum_displacement": maximum_displacement,
            "sample_count": len(movement_samples),
        },
        "timings": timings,
        "total_ms": (time.perf_counter() - overall) * 1000,
    }
    rendered = json.dumps(result, ensure_ascii=False, indent=2)
    if args.json is not None:
        args.json.write_text(rendered + "\n", encoding="utf-8")
    print(rendered)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
