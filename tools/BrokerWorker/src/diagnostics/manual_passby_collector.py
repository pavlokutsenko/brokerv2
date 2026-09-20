from __future__ import annotations

import argparse
import json
import math
import sys
import time
from datetime import datetime, timezone
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "client"))
sys.path.insert(0, str(ROOT / "diagnostics"))

from event_shop_cycle import send_packet  # noqa: E402
from fast_headless_shop_sweep import read_capture_for_mode, read_history  # noqa: E402
from incoming_opcode_suppress import STATE_PATH, install, uninstall  # noqa: E402
from lu4_memory_client import Lu4MemoryClient  # noqa: E402
from no_ui_target_shop_cycle import player_position  # noqa: E402
from passby_event_collector import current_traders  # noqa: E402
from process_event_shop_capture import HISTORY_DEPTH, load_state, read_capture  # noqa: E402
from read_open_shop import coherent_row  # noqa: E402


MOVE_TO_OBJECT_OPCODE = 0x72


def capture_batch(
    targets: list[dict[str, object]],
    snapshot: dict[str, object],
    state: dict[str, object],
    timeout: float,
) -> tuple[list[dict[str, object]], list[dict[str, object]]]:
    pid = int(snapshot["pid"])
    player_actor = int(str(snapshot["player_actor"]), 16)
    with Lu4MemoryClient() as client:
        position = player_position(client, pid, player_actor)
        sequence_before = int(read_capture(client, state)["sequence"])

    packets: dict[int, list[dict[str, object]]] = {}
    captures: dict[int, dict[str, object]] = {}
    pending: dict[int, float] = {}
    seen_sequences: set[int] = set()
    next_index = 0
    with Lu4MemoryClient() as reader:
        while next_index < len(targets) or pending:
            progress = False
            for capture in read_history(reader, state):
                sequence = int(capture["sequence"])
                object_id = int(capture["object_id"])
                if sequence <= sequence_before or sequence in seen_sequences or object_id not in pending:
                    continue
                seen_sequences.add(sequence)
                pending.pop(object_id)
                captures[object_id] = capture
                progress = True

            now = time.perf_counter()
            for object_id, sent_at in list(pending.items()):
                if now - sent_at > timeout:
                    pending.pop(object_id)
                    progress = True

            while next_index < len(targets) and len(pending) < HISTORY_DEPTH:
                target = targets[next_index]
                next_index += 1
                object_id = int(target["object_id"])
                packets[object_id] = [
                    send_packet(object_id, position),
                    send_packet(object_id, position),
                ]
                pending[object_id] = time.perf_counter()
                progress = True
            if not progress:
                time.sleep(0.0005)

    shops: list[dict[str, object]] = []
    failures: list[dict[str, object]] = []
    for target in targets:
        object_id = int(target["object_id"])
        capture = captures.get(object_id)
        if capture is None:
            failures.append({"object_id": object_id, "name": target.get("name", ""), "error": "response absent"})
            continue
        expected = "buy" if int(target["kiosk_type"]) == 3 else "sell"
        rows = list(capture["rows"])
        if capture["side"] != expected:
            failures.append({"object_id": object_id, "name": target.get("name", ""), "error": "side mismatch"})
            continue
        if int(capture["copied_count"]) != int(capture["count"]) or not rows or any(not coherent_row(row) for row in rows):
            failures.append({"object_id": object_id, "name": target.get("name", ""), "error": "incoherent rows"})
            continue
        shops.append({
            "captured_at": datetime.now(timezone.utc).isoformat(),
            "trader": target,
            "side": capture["side"],
            "rows": rows,
            "row_count": len(rows),
            "capture_sequence": capture["sequence"],
            "packets": packets[object_id],
        })
    return shops, failures


def main() -> int:
    parser = argparse.ArgumentParser(description="Fast manual-movement LU4 shop reader")
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--radius", type=float, default=95.0)
    parser.add_argument("--poll", type=float, default=0.12)
    parser.add_argument("--timeout", type=float, default=2.0)
    parser.add_argument("--retry", type=float, default=30.0)
    parser.add_argument("--duration", type=float, default=0.0)
    parser.add_argument("--max-batch", type=int, default=64)
    parser.add_argument("--json", type=Path, required=True)
    parser.add_argument("--jsonl", type=Path, required=True)
    args = parser.parse_args()
    if not 0 < args.radius <= 100 or args.poll <= 0 or args.timeout <= 0 or not 1 <= args.max_batch <= 64:
        raise ValueError("radius must be 1..100; poll/timeout positive; max-batch 1..64")

    snapshot = json.loads((ROOT / "diagnostics" / "latest_actor_snapshot.json").read_text(encoding="utf-8"))
    if int(snapshot.get("pid", -1)) != args.pid:
        raise RuntimeError("actor snapshot belongs to another PID")
    state = load_state()
    if int(state.get("pid", -1)) != args.pid or not read_capture_for_mode(state):
        raise RuntimeError("matching headless shop capture is not enabled")

    successful: set[int] = set()
    retry_at: dict[int, float] = {}
    shops: list[dict[str, object]] = []
    failures: list[dict[str, object]] = []
    batches = 0
    started = time.monotonic()
    deadline = started + args.duration if args.duration > 0 else None
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.jsonl.parent.mkdir(parents=True, exist_ok=True)
    suppression_installed = False

    try:
        # The shop response starts with incoming opcode 0x72, whose normal
        # handler replaces the client's current route with a route to the
        # trader.  The price rows arrive separately through 0xA1, so skip only
        # 0x72 while this manual reader is active.  Do not cancel the target
        # between batches: that also clears user-directed movement.
        install(args.pid, MOVE_TO_OBJECT_OPCODE)
        suppression_installed = True
        while deadline is None or time.monotonic() < deadline:
            with Lu4MemoryClient() as client:
                traders, _ = current_traders(client, snapshot)
                player = player_position(client, args.pid, int(str(snapshot["player_actor"]), 16))
            now = time.monotonic()
            candidates = []
            for trader in traders:
                object_id = int(trader["object_id"])
                distance = math.hypot(float(trader["x"]) - player["x"], float(trader["y"]) - player["y"])
                trader["distance"] = distance
                if object_id not in successful and now >= retry_at.get(object_id, 0) and distance <= args.radius:
                    candidates.append(trader)
            candidates.sort(key=lambda item: float(item["distance"]))
            candidates = candidates[: args.max_batch]
            if not candidates:
                time.sleep(args.poll)
                continue

            batch_started = time.perf_counter()
            captured, failed = capture_batch(candidates, snapshot, state, args.timeout)
            batches += 1
            for shop in captured:
                object_id = int(shop["trader"]["object_id"])
                successful.add(object_id)
                shops.append(shop)
                with args.jsonl.open("a", encoding="utf-8") as stream:
                    stream.write(json.dumps(shop, ensure_ascii=False) + "\n")
            for failure in failed:
                retry_at[int(failure["object_id"])] = time.monotonic() + args.retry
                failures.append({**failure, "failed_at": datetime.now(timezone.utc).isoformat()})
            elapsed = time.perf_counter() - batch_started
            print(json.dumps({
                "batch": batches,
                "nearby": len(candidates),
                "captured": len(captured),
                "failed": len(failed),
                "total_unique": len(successful),
                "seconds": round(elapsed, 3),
                "shops_per_second": round(len(captured) / elapsed, 3) if elapsed else 0,
            }, ensure_ascii=False), flush=True)
    except KeyboardInterrupt:
        pass
    finally:
        if suppression_installed and STATE_PATH.exists():
            uninstall()
        elapsed = time.monotonic() - started
        summary = {
            "schema": 1,
            "mode": "manual_movement_fast_passby",
            "pid": args.pid,
            "radius": args.radius,
            "elapsed_seconds": round(elapsed, 3),
            "captured_shops": len(shops),
            "unique_traders": len(successful),
            "failure_events": len(failures),
            "batches": batches,
            "average_shops_per_second": round(len(shops) / elapsed, 3) if elapsed else 0,
            "failures": failures,
            "shops": shops,
        }
        args.json.write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({key: summary[key] for key in ("captured_shops", "unique_traders", "failure_events", "elapsed_seconds", "average_shops_per_second")}, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
