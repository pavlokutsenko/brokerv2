from __future__ import annotations

import argparse
import contextlib
import csv
import io
import json
import struct
import sys
import time
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "client"))
sys.path.insert(0, str(ROOT / "diagnostics"))

from broker_query import send_market  # noqa: E402
from lu4_memory_client import Lu4MemoryClient  # noqa: E402
from lu4_target_controller import select_target  # noqa: E402
from process_event_broker_capture import load_state, read_capture, read_history  # noqa: E402


def quiet_search(item_id: int, store_type: int) -> None:
    with contextlib.redirect_stdout(io.StringIO()):
        select_target(
            0,
            0.0,
            0.0,
            0.0,
            False,
            fast=True,
            payload_override=struct.pack("<BiBB", 0x20, item_id, store_type, 0),
            packet_name="broker_bulk_inventory",
        )


def wait_for_sequence(
    client: Lu4MemoryClient,
    state: dict[str, object],
    target: int,
    timeout: float,
) -> int:
    deadline = time.monotonic() + timeout
    current = 0
    while time.monotonic() < deadline:
        current = int(read_capture(client, state)["sequence"])
        if current >= target:
            return current
        time.sleep(0.001)
    raise TimeoutError(f"broker sequence stopped at {current}, expected {target}")


def loaded_trader_names() -> dict[int, str]:
    path = ROOT / "diagnostics" / "latest_actor_snapshot.json"
    if not path.exists():
        return {}
    snapshot = json.loads(path.read_text(encoding="utf-8"))
    return {
        int(item["object_id"]): str(item.get("name", ""))
        for item in snapshot.get("traders", [])
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Collect complete pipelined broker inventory")
    parser.add_argument("--store-types", default="1,3,8")
    parser.add_argument("--batch-size", type=int, default=32)
    parser.add_argument("--timeout", type=float, default=10.0)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--progress", action="store_true")
    args = parser.parse_args()
    store_types = [int(value) for value in args.store_types.split(",")]
    if not 1 <= args.batch_size <= 32:
        raise ValueError("batch size must be 1..32")

    state = load_state()
    if int(state.get("history_depth", 1)) < args.batch_size:
        raise RuntimeError("broker capture history is smaller than the batch")
    markets: dict[int, list[int]] = {}
    market_timings: dict[int, float] = {}
    started = time.monotonic()
    for store_type in store_types:
        phase = time.monotonic()
        with contextlib.redirect_stdout(io.StringIO()):
            response = send_market(store_type, args.timeout)
        markets[store_type] = [int(value) for value in response["rows"]]
        market_timings[store_type] = round(time.monotonic() - phase, 3)

    records: list[dict[str, object]] = []
    batch_metrics: list[dict[str, object]] = []
    with Lu4MemoryClient() as client:
        for store_type in store_types:
            item_ids = markets[store_type]
            for offset in range(0, len(item_ids), args.batch_size):
                batch = item_ids[offset : offset + args.batch_size]
                before = int(read_capture(client, state)["sequence"])
                batch_started = time.monotonic()
                for item_id in batch:
                    quiet_search(item_id, store_type)
                target = before + len(batch)
                reached = wait_for_sequence(client, state, target, args.timeout)
                history = read_history(client, state, len(batch))
                fresh = sorted(
                    (
                        record
                        for record in history
                        if before < int(record["sequence"]) <= reached
                        and record["function_name"] == "BrokerTradersByItem"
                    ),
                    key=lambda record: int(record["sequence"]),
                )
                expected = {(int(item_id), store_type) for item_id in batch}
                actual = {(int(row["arg0"]), int(row["arg1"])) for row in fresh}
                if len(fresh) != len(batch) or actual != expected:
                    raise RuntimeError(
                        f"incomplete batch type={store_type} offset={offset}: "
                        f"records={len(fresh)}/{len(batch)}, missing={sorted(expected-actual)[:8]}"
                    )
                for response in fresh:
                    if int(response["copied_count"]) != int(response["count"]):
                        raise RuntimeError(
                            f"truncated broker response item={response['arg0']} "
                            f"{response['copied_count']}/{response['count']}"
                        )
                    query_item_id = int(response["arg0"])
                    query_store_type = int(response["arg1"])
                    for row in response["rows"]:
                        records.append(
                            {
                                "store_type": query_store_type,
                                "item_id": query_item_id,
                                "trader_object_id": int(row["object_id"]),
                                "broker_item_id": int(row["item_id"]),
                                "amount": int(row["amount"]),
                            }
                        )
                elapsed = time.monotonic() - batch_started
                metric = {
                    "store_type": store_type,
                    "offset": offset,
                    "requests": len(batch),
                    "responses": len(fresh),
                    "elapsed_seconds": round(elapsed, 3),
                    "requests_per_second": round(len(batch) / elapsed, 3),
                }
                batch_metrics.append(metric)
                if args.progress:
                    print(json.dumps(metric), flush=True)

    names = loaded_trader_names()
    for row in records:
        row["trader_name"] = names.get(int(row["trader_object_id"]), "")

    unique_items = {int(row["item_id"]) for row in records}
    unique_market_items = {
        int(item_id) for values in markets.values() for item_id in values
    }
    unique_traders = {int(row["trader_object_id"]) for row in records}
    by_type: dict[int, dict[str, object]] = {}
    for store_type in store_types:
        selected = [row for row in records if int(row["store_type"]) == store_type]
        by_type[store_type] = {
            "market_item_ids": len(markets[store_type]),
            "items_with_rows": len({int(row["item_id"]) for row in selected}),
            "unique_traders": len({int(row["trader_object_id"]) for row in selected}),
            "listing_rows": len(selected),
            "total_amount": sum(int(row["amount"]) for row in selected),
        }
    by_item: dict[int, dict[str, int]] = defaultdict(
        lambda: {"traders": 0, "listing_rows": 0, "total_amount": 0}
    )
    item_traders: dict[int, set[int]] = defaultdict(set)
    for row in records:
        item_id = int(row["item_id"])
        item_traders[item_id].add(int(row["trader_object_id"]))
        by_item[item_id]["listing_rows"] += 1
        by_item[item_id]["total_amount"] += int(row["amount"])
    for item_id, traders in item_traders.items():
        by_item[item_id]["traders"] = len(traders)

    elapsed = time.monotonic() - started
    output = {
        "schema": 1,
        "captured_at": datetime.now(timezone.utc).isoformat(),
        "store_types": store_types,
        "batch_size": args.batch_size,
        "elapsed_seconds": round(elapsed, 3),
        "market_timings_seconds": market_timings,
        "summary": {
            "market_item_requests": sum(len(values) for values in markets.values()),
            "unique_market_items": len(unique_market_items),
            "items_with_rows": len(unique_items),
            "unique_traders": len(unique_traders),
            "listing_rows": len(records),
            "total_amount": sum(int(row["amount"]) for row in records),
            "named_traders": len(
                {int(row["trader_object_id"]) for row in records if row["trader_name"]}
            ),
        },
        "by_store_type": {str(key): value for key, value in by_type.items()},
        "by_item": {str(key): value for key, value in sorted(by_item.items())},
        "batch_metrics": batch_metrics,
        "rows": records,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(output, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    csv_path = args.output.with_suffix(".csv")
    with csv_path.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(
            stream,
            fieldnames=(
                "store_type",
                "item_id",
                "trader_object_id",
                "trader_name",
                "broker_item_id",
                "amount",
            ),
        )
        writer.writeheader()
        writer.writerows(records)
    print(json.dumps({**output["summary"], "elapsed_seconds": output["elapsed_seconds"], "output": str(args.output), "csv": str(csv_path)}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
