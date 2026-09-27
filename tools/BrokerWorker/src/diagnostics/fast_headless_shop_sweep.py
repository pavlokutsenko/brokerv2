from __future__ import annotations

import argparse
import json
import struct
import sys
import time
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "client"))
sys.path.insert(0, str(ROOT / "diagnostics"))

from event_shop_cycle import send_packet  # noqa: E402
from lu4_memory_client import Lu4MemoryClient  # noqa: E402
from lu4_target_controller import read_exact  # noqa: E402
from no_ui_target_shop_cycle import player_position  # noqa: E402
from passby_event_collector import current_traders  # noqa: E402
from process_event_shop_capture import (  # noqa: E402
    DATA_SIZE,
    HISTORY_DEPTH,
    MAX_ROWS,
    RECORD_STRIDE,
    ROWS_OFFSET,
    ROW_SIZE,
    load_state,
    read_capture,
)
from read_open_shop import coherent_row  # noqa: E402


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Read nearby shops sequentially without cancelling between targets"
    )
    parser.add_argument("--radius", type=float, default=115.0)
    parser.add_argument("--max-shops", type=int, default=0)
    parser.add_argument("--timeout", type=float, default=2.0)
    parser.add_argument("--batch-size", type=int, default=4)
    parser.add_argument("--json", type=Path)
    parser.add_argument(
        "--verbose",
        action="store_true",
        help="print one progress record per captured shop",
    )
    args = parser.parse_args()
    if (
        args.radius <= 0
        or args.timeout <= 0
        or args.max_shops < 0
        or not 1 <= args.batch_size <= HISTORY_DEPTH
    ):
        raise ValueError(
            f"radius/timeout must be positive, max-shops nonnegative, and "
            f"batch-size in 1..{HISTORY_DEPTH}"
        )

    snapshot = json.loads(
        (ROOT / "diagnostics" / "latest_actor_snapshot.json").read_text(
            encoding="utf-8"
        )
    )
    state = load_state()
    if not read_capture_for_mode(state):
        raise RuntimeError("shop capture headless mode is not enabled")

    pid = int(snapshot["pid"])
    with Lu4MemoryClient() as client:
        traders, actor_refresh_ms = current_traders(client, snapshot)
        position = player_position(
            client, pid, int(str(snapshot["player_actor"]), 16)
        )
    candidates = sorted(
        (
            trader
            for trader in traders
            if float(trader["distance"]) <= args.radius
        ),
        key=lambda trader: float(trader["distance"]),
    )
    if args.max_shops:
        candidates = candidates[: args.max_shops]

    results: list[dict[str, object]] = []
    failures: list[dict[str, object]] = []
    started = time.perf_counter()
    try:
        for batch_start in range(0, len(candidates), args.batch_size):
            batch = candidates[batch_start : batch_start + args.batch_size]
            with Lu4MemoryClient() as client:
                sequence_before = int(read_capture(client, state)["sequence"])
            batch_started = time.perf_counter()
            packets: dict[int, list[dict[str, object]]] = {}
            for trader in batch:
                object_id = int(trader["object_id"])
                packets[object_id] = [
                    send_packet(object_id, position),
                    send_packet(object_id, position),
                ]
            send_completed = time.perf_counter()
            captures = wait_for_batch(
                state,
                {int(trader["object_id"]) for trader in batch},
                sequence_before,
                args.timeout,
            )
            batch_completed = time.perf_counter()
            for trader in batch:
                object_id = int(trader["object_id"])
                capture = captures.get(object_id)
                if capture is None:
                    failures.append(
                        {
                            "name": trader.get("name", ""),
                            "object_id": object_id,
                            "distance": trader.get("distance"),
                            "error": "TimeoutError: response absent from capture history",
                        }
                    )
                    continue
                expected_side = "buy" if int(trader["kiosk_type"]) == 3 else "sell"
                rows = list(capture["rows"])
                if capture["side"] != expected_side:
                    failures.append(
                        {
                            "name": trader.get("name", ""),
                            "object_id": object_id,
                            "distance": trader.get("distance"),
                            "error": (
                                f"side mismatch: expected={expected_side} "
                                f"got={capture['side']}"
                            ),
                        }
                    )
                    continue
                if int(capture["copied_count"]) != int(capture["count"]):
                    failures.append(
                        {
                            "name": trader.get("name", ""),
                            "object_id": object_id,
                            "distance": trader.get("distance"),
                            "error": "hook did not copy the complete shop array",
                        }
                    )
                    continue
                if not rows or any(not coherent_row(row) for row in rows):
                    failures.append(
                        {
                            "name": trader.get("name", ""),
                            "object_id": object_id,
                            "distance": trader.get("distance"),
                            "error": "captured shop contains no coherent rows",
                        }
                    )
                    continue
                result = {
                    "schema": 1,
                    "pid": pid,
                    "trader": trader,
                    "side": capture["side"],
                    "rows": rows,
                    "row_count": len(rows),
                    "capture_sequence": capture["sequence"],
                    "timings": {
                        "batch_send_ms": round(
                            (send_completed - batch_started) * 1000, 3
                        ),
                        "batch_wait_ms": round(
                            (batch_completed - send_completed) * 1000, 3
                        ),
                        "batch_total_ms": round(
                            (batch_completed - batch_started) * 1000, 3
                        ),
                    },
                    "packets": packets[object_id],
                }
                results.append(result)
                if args.verbose:
                    print(
                        json.dumps(
                            {
                                "name": trader["name"],
                                "distance": round(float(trader["distance"]), 2),
                                "rows": result["row_count"],
                                "batch_total_ms": result["timings"]["batch_total_ms"],
                            },
                            ensure_ascii=False,
                        ),
                        flush=True,
                    )
    finally:
        final_cancel = send_packet(
            0, position, b"\x48", "target_cancel_after_fast_sweep"
        )
    elapsed = time.perf_counter() - started
    summary: dict[str, object] = {
        "schema": 1,
        "mode": "headless_batched_keep_target",
        "batch_size": args.batch_size,
        "radius": args.radius,
        "candidate_count": len(candidates),
        "captured_shops": len(results),
        "failure_count": len(failures),
        "elapsed_seconds": round(elapsed, 3),
        "shops_per_second": round(len(results) / elapsed, 3) if elapsed else 0,
        "average_ms_per_captured_shop": (
            round(elapsed * 1000 / len(results), 3) if results else None
        ),
        "actor_refresh_ms": round(actor_refresh_ms, 3),
        "final_cancel": final_cancel,
        "failures": failures,
        "shops": results,
    }
    rendered = json.dumps(summary, ensure_ascii=False, indent=2)
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(rendered + "\n", encoding="utf-8")
    print(
        json.dumps(
            {
                "candidate_count": summary["candidate_count"],
                "captured_shops": summary["captured_shops"],
                "failure_count": summary["failure_count"],
                "elapsed_seconds": summary["elapsed_seconds"],
                "shops_per_second": summary["shops_per_second"],
                "average_ms_per_captured_shop": summary[
                    "average_ms_per_captured_shop"
                ],
                "output": str(args.json) if args.json else None,
            },
            ensure_ascii=False,
            indent=2,
        )
    )
    return 0 if not failures else 2


def read_capture_for_mode(state: dict[str, object]) -> bool:
    with Lu4MemoryClient() as client:
        return bool(read_capture(client, state).get("suppress_ui"))


def wait_for_batch(
    state: dict[str, object], object_ids: set[int], after: int, timeout: float
) -> dict[int, dict[str, object]]:
    deadline = time.perf_counter() + timeout
    found: dict[int, dict[str, object]] = {}
    with Lu4MemoryClient() as client:
        while time.perf_counter() < deadline:
            for current in read_history(client, state):
                object_id = int(current["object_id"])
                if int(current["sequence"]) > after and object_id in object_ids:
                    found[object_id] = current
            if len(found) == len(object_ids):
                return found
            time.sleep(0.0005)
    return found


def read_history(
    client: Lu4MemoryClient, state: dict[str, object]
) -> list[dict[str, object]]:
    pid = int(state["pid"])
    address = int(state["data"])
    record_addresses = [
        int(value) for value in state.get("record_addresses", [])
    ]
    if len(record_addresses) != HISTORY_DEPTH:
        # Compatibility with the original contiguous four-record ring.
        record_addresses = [
            address + slot * RECORD_STRIDE for slot in range(HISTORY_DEPTH)
        ]
    for _ in range(20):
        busy_before = struct.unpack(
            "<I", read_exact(client, pid, address, 4)
        )[0]
        sequence_before = struct.unpack(
            "<Q", read_exact(client, pid, address + 8, 8)
        )[0]
        if busy_before:
            time.sleep(0.0005)
            continue
        raw_records = [
            read_exact(client, pid, record_address, DATA_SIZE)
            for record_address in record_addresses
        ]
        busy_after = struct.unpack(
            "<I", read_exact(client, pid, address, 4)
        )[0]
        sequence_after = struct.unpack(
            "<Q", read_exact(client, pid, address + 8, 8)
        )[0]
        if busy_after == 0 and sequence_after == sequence_before:
            records = []
            for raw in raw_records:
                record = decode_record(raw, state)
                if int(record["sequence"]) > 0:
                    records.append(record)
            return sorted(records, key=lambda item: int(item["sequence"]))
        time.sleep(0.0005)
    raise RuntimeError("could not read coherent shop capture history")


def decode_record(raw: bytes, state: dict[str, object]) -> dict[str, object]:
    sequence = struct.unpack_from("<Q", raw, 8)[0]
    name_index = struct.unpack_from("<I", raw, 0x68)[0]
    object_id = struct.unpack_from("<i", raw, 0x70)[0]
    side_code = raw[0x74]
    count, capacity, copied_count = struct.unpack_from("<iii", raw, 0x80)
    names = {
        int(item["name_index"]): name
        for name, item in dict(state["functions"]).items()
    }
    rows = []
    if 0 < copied_count <= MAX_ROWS:
        for index in range(copied_count):
            offset = ROWS_OFFSET + index * ROW_SIZE
            rows.append(
                {
                    "row_index": index,
                    "item_object_id": struct.unpack_from("<i", raw, offset)[0],
                    "item_id": struct.unpack_from("<i", raw, offset + 4)[0],
                    "count": struct.unpack_from("<i", raw, offset + 8)[0],
                    "enchant_level": struct.unpack_from("<i", raw, offset + 0x14)[0],
                    "price": struct.unpack_from("<i", raw, offset + 0x2C)[0],
                    "buy_count": struct.unpack_from("<i", raw, offset + 0x30)[0],
                    "base_price": struct.unpack_from("<i", raw, offset + 0x34)[0],
                }
            )
    return {
        "sequence": sequence,
        "function_name": names.get(name_index, ""),
        "parser_caller": f"0x{struct.unpack_from('<Q', raw, 0xB8)[0]:X}",
        "alternate_converter_caller": f"0x{struct.unpack_from('<Q', raw, 0xC0)[0]:X}",
        "object_id": object_id,
        "side": {1: "buy", 2: "sell"}.get(side_code, ""),
        "count": count,
        "capacity": capacity,
        "copied_count": copied_count,
        "rows": rows,
    }


if __name__ == "__main__":
    raise SystemExit(main())
