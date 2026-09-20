from __future__ import annotations

import argparse
import json
import struct
import sys
import time
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "client"))

from lu4_memory_client import Lu4MemoryClient  # noqa: E402
from lu4_target_controller import select_target  # noqa: E402
from process_event_broker_capture import load_state, read_capture  # noqa: E402


def wait_after(sequence: int, timeout: float) -> dict[str, object]:
    state = load_state()
    deadline = time.monotonic() + timeout
    with Lu4MemoryClient() as client:
        while time.monotonic() < deadline:
            result = read_capture(client, state)
            if int(result["sequence"]) > sequence:
                return result
            time.sleep(0.01)
    raise TimeoutError(f"no broker response after sequence {sequence}")


def current_sequence() -> int:
    state = load_state()
    with Lu4MemoryClient() as client:
        return int(read_capture(client, state)["sequence"])


def send_market(store_type: int, timeout: float) -> dict[str, object]:
    if not 0 <= store_type <= 0xFF:
        raise ValueError("store type must fit in one byte")
    before = current_sequence()
    select_target(
        0,
        0.0,
        0.0,
        0.0,
        False,
        fast=True,
        payload_override=struct.pack("<BB", 0x21, store_type),
        packet_name="broker_market_items",
    )
    return wait_after(before, timeout)


def send_search(
    item_id: int, store_type: int, enchant_min: int, timeout: float
) -> dict[str, object]:
    if not -0x80000000 <= item_id <= 0x7FFFFFFF:
        raise ValueError("item id must fit in int32")
    if not 0 <= store_type <= 0xFF or not 0 <= enchant_min <= 0xFF:
        raise ValueError("store type and enchant minimum must fit in one byte")
    before = current_sequence()
    select_target(
        0,
        0.0,
        0.0,
        0.0,
        False,
        fast=True,
        payload_override=struct.pack("<BiBB", 0x20, item_id, store_type, enchant_min),
        packet_name="broker_search_traders",
    )
    return wait_after(before, timeout)


def main() -> int:
    parser = argparse.ArgumentParser(description="Send and capture LU4 ItemBroker requests")
    parser.add_argument("--timeout", type=float, default=10.0)
    sub = parser.add_subparsers(dest="command", required=True)
    market = sub.add_parser("market")
    market.add_argument("store_type", type=int)
    search = sub.add_parser("search")
    search.add_argument("item_id", type=int)
    search.add_argument("store_type", type=int)
    search.add_argument("enchant_min", type=int, nargs="?", default=0)
    args = parser.parse_args()
    if args.command == "market":
        result = send_market(args.store_type, args.timeout)
    else:
        result = send_search(args.item_id, args.store_type, args.enchant_min, args.timeout)
    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
