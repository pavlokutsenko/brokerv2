from __future__ import annotations

import argparse
import contextlib
import io
import json
import struct
import sys
import time
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "client"))
sys.path.insert(0, str(ROOT / "diagnostics"))

from lu4_memory_client import Lu4MemoryClient  # noqa: E402
from lu4_target_controller import select_target  # noqa: E402
from no_ui_target_shop_cycle import player_position  # noqa: E402
from process_event_shop_capture import load_state, read_capture  # noqa: E402
from read_open_shop import coherent_row  # noqa: E402


def send_packet(
    object_id: int,
    position: dict[str, float],
    payload: bytes | None = None,
    name: str = "target_action",
) -> dict[str, object]:
    output = io.StringIO()
    with contextlib.redirect_stdout(output):
        select_target(
            object_id,
            position["x"],
            position["y"],
            position["z"],
            False,
            fast=True,
            payload_override=payload,
            packet_name=name,
        )
    return json.loads(output.getvalue())


def event_shop_cycle(
    trader: dict[str, object],
    snapshot: dict[str, object],
    timeout: float,
    cleanup_target: bool = True,
) -> dict[str, object]:
    pid = int(snapshot["pid"])
    object_id = int(trader["object_id"])
    kiosk_type = int(trader["kiosk_type"])
    expected_side = "buy" if kiosk_type == 3 else ("sell" if kiosk_type in (1, 8) else None)
    player_actor = int(str(snapshot["player_actor"]), 16)
    capture_state = load_state()
    if int(capture_state["pid"]) != pid:
        raise RuntimeError("ProcessEvent capture belongs to another PID")

    with Lu4MemoryClient() as client:
        position = player_position(client, pid, player_actor)
        sequence_before = int(read_capture(client, capture_state)["sequence"])

    started = time.perf_counter()
    first = send_packet(object_id, position)
    second = send_packet(object_id, position)
    # Keep the server-side action order alive while an out-of-range trader is
    # approached.  Cancelling immediately after the double action works only
    # for shops already inside interaction range and prevents native pathing
    # from starting for distant shops.  Cleanup is still performed after the
    # matching shop response has been captured.
    cancel = None
    sent = time.perf_counter()

    capture: dict[str, object] | None = None
    deadline = sent + timeout
    with Lu4MemoryClient() as client:
        while time.perf_counter() < deadline:
            current = read_capture(client, capture_state)
            if (
                int(current["sequence"]) > sequence_before
                and int(current["object_id"]) == object_id
            ):
                capture = current
                break
            time.sleep(0.001)
        if capture is None:
            raise TimeoutError(
                f"no matching shop event for ObjectID {object_id} after sequence "
                f"{sequence_before}"
            )
        selected_actor = struct.unpack(
            "<Q", client.read(pid, int(str(snapshot["controller"]), 16) + 0x898, 8)
        )[0]
    response_captured = time.perf_counter()

    # The first cancel is deliberately queued immediately after the two action
    # packets, but its acknowledgement can lose a race with the later target
    # update.  Once the list itself has arrived, send one bounded cleanup
    # cancel only when a target is still present and wait for the controller
    # field to settle.
    cleanup_cancel: dict[str, object] | None = None
    cleanup_started = time.perf_counter()
    if cleanup_target and selected_actor:
        cleanup_cancel = send_packet(0, position, b"\x48", "target_cancel_cleanup")
        cleanup_deadline = time.perf_counter() + min(1.0, timeout)
        with Lu4MemoryClient() as client:
            while time.perf_counter() < cleanup_deadline:
                selected_actor = struct.unpack(
                    "<Q",
                    client.read(
                        pid, int(str(snapshot["controller"]), 16) + 0x898, 8
                    ),
                )[0]
                if selected_actor == 0:
                    break
                time.sleep(0.002)
    cleanup_completed = time.perf_counter()

    rows = list(capture["rows"])
    if expected_side is not None and capture["side"] != expected_side:
        raise RuntimeError(
            f"shop side mismatch: expected={expected_side} got={capture['side']}"
        )
    if int(capture["copied_count"]) != int(capture["count"]):
        raise RuntimeError("hook did not copy the complete shop array")
    if not rows or any(not coherent_row(row) for row in rows):
        raise RuntimeError("captured shop contains no coherent rows")

    completed = time.perf_counter()
    return {
        "schema": 1,
        "pid": pid,
        "trader": {
            "name": trader.get("name", ""),
            "object_id": object_id,
            "kiosk_type": kiosk_type,
            "distance_at_snapshot": trader.get("distance"),
        },
        "side": capture["side"],
        "rows": rows,
        "row_count": len(rows),
        "capture_sequence": capture["sequence"],
        "selected_actor_after": f"0x{selected_actor:X}",
        "timings": {
            "send_ms": round((sent - started) * 1000, 3),
            "response_after_send_ms": round(
                (response_captured - sent) * 1000, 3
            ),
            "total_ms": round((completed - started) * 1000, 3),
            "target_cleanup_ms": round(
                (cleanup_completed - cleanup_started) * 1000, 3
            ),
        },
        "packets": {
            "first": first,
            "second": second,
            "cancel": cancel,
            "cleanup_cancel": cleanup_cancel,
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Open and read one LU4 private shop from ProcessEvent capture"
    )
    target = parser.add_mutually_exclusive_group(required=True)
    target.add_argument("--name")
    target.add_argument("--object-id", type=int)
    parser.add_argument("--pid", type=int)
    parser.add_argument("--kiosk-type", type=int, choices=(0, 1, 3, 8))
    parser.add_argument("--trader-name")
    parser.add_argument("--target-x", type=float)
    parser.add_argument("--target-y", type=float)
    parser.add_argument("--timeout", type=float, default=10.0)
    parser.add_argument("--json", type=Path)
    args = parser.parse_args()
    snapshot = json.loads(
        (ROOT / "diagnostics" / "latest_actor_snapshot.json").read_text(
            encoding="utf-8"
        )
    )
    if args.pid is not None and int(snapshot.get("pid", -1)) != args.pid:
        raise RuntimeError("cached actor snapshot belongs to another PID")
    if args.object_id is not None and args.kiosk_type is not None:
        matches = [{
            "object_id": args.object_id,
            "kiosk_type": args.kiosk_type,
            "name": args.trader_name or "",
            "x": args.target_x,
            "y": args.target_y,
        }]
    else:
        matches = [
            item
            for item in snapshot.get("named", [])
            if ((args.object_id is not None and int(item.get("object_id", 0)) == args.object_id)
                or (args.name is not None and str(item.get("name", "")).casefold() == args.name.casefold()))
            and int(item.get("kiosk_type", 0)) in (1, 3, 8)
        ]
    if len(matches) != 1:
        raise RuntimeError(f"expected one live named trader, found {len(matches)}")
    result = event_shop_cycle(matches[0], snapshot, args.timeout)
    rendered = json.dumps(result, ensure_ascii=False, indent=2)
    if args.json:
        args.json.write_text(rendered + "\n", encoding="utf-8")
    print(rendered)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
