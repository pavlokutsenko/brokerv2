from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "client"))
sys.path.insert(0, str(ROOT / "diagnostics"))

from event_shop_cycle import send_packet  # noqa: E402
from fast_headless_shop_sweep import read_capture_for_mode, read_history  # noqa: E402
from lu4_memory_client import Lu4MemoryClient  # noqa: E402
from no_ui_target_shop_cycle import player_position  # noqa: E402
from process_event_shop_capture import HISTORY_DEPTH, load_state, read_capture  # noqa: E402
from read_open_shop import coherent_row  # noqa: E402


def main() -> int:
    parser = argparse.ArgumentParser(description="Capture one leased local trader batch")
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--timeout", type=float, default=2.0)
    parser.add_argument("--json", type=Path, required=True)
    args = parser.parse_args()
    targets = json.loads(args.input.read_text(encoding="utf-8"))
    if not isinstance(targets, list) or not 1 <= len(targets) <= HISTORY_DEPTH:
        raise ValueError(f"target batch must contain 1..{HISTORY_DEPTH} entries")

    snapshot = json.loads((ROOT / "diagnostics" / "latest_actor_snapshot.json").read_text(encoding="utf-8"))
    if int(snapshot.get("pid", -1)) != args.pid:
        raise RuntimeError("cached actor snapshot belongs to another PID")
    state = load_state()
    if not read_capture_for_mode(state):
        raise RuntimeError("shop capture headless mode is not enabled")
    with Lu4MemoryClient() as client:
        position = player_position(client, args.pid, int(str(snapshot["player_actor"]), 16))
        sequence_before = int(read_capture(client, state)["sequence"])

    packets: dict[int, list[dict[str, object]]] = {}
    captures: dict[int, dict[str, object]] = {}
    pending: dict[int, float] = {}
    seen_sequences: set[int] = set()
    next_index = 0
    window = min(8, HISTORY_DEPTH)
    started = time.perf_counter()
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
                if now - sent_at > args.timeout:
                    pending.pop(object_id)
                    progress = True

            while next_index < len(targets) and len(pending) < window:
                target = targets[next_index]
                next_index += 1
                object_id = int(target["object_id"])
                packets[object_id] = [send_packet(object_id, position), send_packet(object_id, position)]
                pending[object_id] = time.perf_counter()
                progress = True

            if not progress:
                time.sleep(0.0005)
    received = time.perf_counter()
    send_packet(0, position, b"\x48", "target_cancel_after_price_batch")

    shops: list[dict[str, object]] = []
    failures: list[dict[str, object]] = []
    for target in targets:
        object_id = int(target["object_id"])
        capture = captures.get(object_id)
        if capture is None:
            failures.append({"object_id": object_id, "error": "response absent from capture history"})
            continue
        kiosk_type = int(target.get("kiosk_type", 0))
        expected_side = "buy" if kiosk_type == 3 else ("sell" if kiosk_type in (1, 8) else None)
        rows = list(capture["rows"])
        error = None
        if expected_side is not None and capture["side"] != expected_side:
            error = f"side mismatch: expected={expected_side} got={capture['side']}"
        elif int(capture["copied_count"]) != int(capture["count"]):
            error = "hook did not copy the complete shop array"
        elif not rows or any(not coherent_row(row) for row in rows):
            error = "captured shop contains no coherent rows"
        if error:
            failures.append({"object_id": object_id, "error": error})
            continue
        shops.append({
            "schema": 1,
            "pid": args.pid,
            "trader": target,
            "side": capture["side"],
            "rows": rows,
            "row_count": len(rows),
            "capture_sequence": capture["sequence"],
            "packets": packets[object_id],
        })

    elapsed = received - started
    result = {
        "schema": 1,
        "mode": "leased_price_batch",
        "target_count": len(targets),
        "captured_shops": len(shops),
        "failure_count": len(failures),
        "elapsed_seconds": round(elapsed, 3),
        "shops_per_second": round(len(shops) / elapsed, 3) if elapsed else 0,
        "window": window,
        "send_ms": None,
        "wait_ms": round((received - started) * 1000, 3),
        "shops": shops,
        "failures": failures,
    }
    args.json.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: result[key] for key in ("target_count", "captured_shops", "failure_count", "elapsed_seconds", "shops_per_second")}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
