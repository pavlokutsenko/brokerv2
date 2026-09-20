from __future__ import annotations

import argparse
import contextlib
import io
import json
import math
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "client"))
sys.path.insert(0, str(ROOT / "diagnostics"))

from lu4_memory_client import Lu4MemoryClient, build_move_to_location_packet  # noqa: E402
from lu4_target_controller import select_target  # noqa: E402
from no_ui_target_shop_cycle import player_position  # noqa: E402


def main() -> int:
    parser = argparse.ArgumentParser(description="Move the LU4 player to one world coordinate")
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--target-x", type=float, required=True)
    parser.add_argument("--target-y", type=float, required=True)
    parser.add_argument("--radius", type=float, default=180.0)
    parser.add_argument("--timeout", type=float, default=90.0)
    parser.add_argument("--json", type=Path, required=True)
    args = parser.parse_args()
    snapshot = json.loads((ROOT / "diagnostics" / "latest_actor_snapshot.json").read_text(encoding="utf-8"))
    if int(snapshot.get("pid", -1)) != args.pid:
        raise RuntimeError("cached actor snapshot belongs to another PID")
    player_actor = int(str(snapshot["player_actor"]), 16)
    with Lu4MemoryClient() as client:
        start = player_position(client, args.pid, player_actor)
    packet = build_move_to_location_packet(
        start["x"], start["y"], start["z"], args.target_x, args.target_y, start["z"]
    )
    output = io.StringIO()
    with contextlib.redirect_stdout(output):
        select_target(0, start["x"], start["y"], start["z"], False, fast=True,
                      payload_override=packet, packet_name="move_to_center")
    sent = json.loads(output.getvalue())
    deadline = time.perf_counter() + args.timeout
    current = start
    while time.perf_counter() < deadline:
        with Lu4MemoryClient() as client:
            current = player_position(client, args.pid, player_actor)
        if math.hypot(current["x"] - args.target_x, current["y"] - args.target_y) <= args.radius:
            result = {"reached": True, "start": start, "current": current, "packet": sent}
            args.json.parent.mkdir(parents=True, exist_ok=True)
            args.json.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            return 0
        time.sleep(0.1)
    raise TimeoutError(f"centre was not reached; current=({current['x']:.0f},{current['y']:.0f})")


if __name__ == "__main__":
    raise SystemExit(main())
