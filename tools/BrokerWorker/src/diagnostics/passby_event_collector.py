from __future__ import annotations

import argparse
import json
import sys
import time
from datetime import datetime, timezone
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "client"))
sys.path.insert(0, str(ROOT / "diagnostics"))

from event_shop_cycle import event_shop_cycle  # noqa: E402
from lu4_memory_client import Lu4MemoryClient  # noqa: E402
from scan_lu4_actors import (  # noqa: E402
    Memory,
    coherent_actor_snapshot,
    enumerate_positioned_actors,
)


def current_traders(
    client: Lu4MemoryClient, snapshot: dict[str, object]
) -> tuple[list[dict[str, object]], float]:
    started = time.perf_counter()
    pid = int(snapshot["pid"])
    level = int(str(snapshot["persistent_level"]), 16)
    player_actor = int(str(snapshot["player_actor"]), 16)
    mem = Memory(client, pid)
    capsule = mem.u64(player_actor + 0x1A0)
    world = {
        "player_actor": player_actor,
        "player": (
            mem.f64(capsule + 0x1F0),
            mem.f64(capsule + 0x1F8),
            mem.f64(capsule + 0x200),
        ),
    }
    actors, _ = coherent_actor_snapshot(mem, level)
    positioned = enumerate_positioned_actors(mem, world, actors)
    traders = [
        item
        for item in positioned
        if int(item.get("object_id", 0)) > 0
        and str(item.get("name", ""))
        and int(item.get("kiosk_type", 0)) in (1, 3, 8)
    ]
    return traders, (time.perf_counter() - started) * 1000


def write_jsonl(path: Path | None, value: dict[str, object]) -> None:
    if path is None:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as stream:
        stream.write(json.dumps(value, ensure_ascii=False) + "\n")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Read every LU4 private shop entering a live radius"
    )
    parser.add_argument("--radius", type=float, default=100.0)
    parser.add_argument("--cooldown", type=float, default=300.0)
    parser.add_argument("--timeout", type=float, default=8.0)
    parser.add_argument("--poll", type=float, default=0.05)
    parser.add_argument("--duration", type=float, default=0.0)
    parser.add_argument("--max-shops", type=int, default=0)
    parser.add_argument("--once", action="store_true")
    parser.add_argument("--jsonl", type=Path)
    parser.add_argument("--json", type=Path)
    args = parser.parse_args()
    if args.radius <= 0 or args.poll <= 0 or args.timeout <= 0:
        raise ValueError("radius, poll, and timeout must be positive")

    snapshot_path = ROOT / "diagnostics" / "latest_actor_snapshot.json"
    snapshot = json.loads(snapshot_path.read_text(encoding="utf-8"))
    seen_at: dict[int, float] = {}
    results: list[dict[str, object]] = []
    failures: list[dict[str, object]] = []
    scan_samples: list[float] = []
    started = time.monotonic()
    stop_at = started + args.duration if args.duration > 0 else None

    with Lu4MemoryClient() as client:
        while True:
            if stop_at is not None and time.monotonic() >= stop_at:
                break
            traders, scan_ms = current_traders(client, snapshot)
            scan_samples.append(scan_ms)
            now = time.monotonic()
            candidates = [
                trader
                for trader in traders
                if float(trader["distance"]) <= args.radius
                and now - seen_at.get(int(trader["object_id"]), -1e30)
                >= args.cooldown
            ]
            candidates.sort(key=lambda item: float(item["distance"]))
            if not candidates:
                if args.once:
                    break
                time.sleep(args.poll)
                continue

            for trader in candidates:
                object_id = int(trader["object_id"])
                # Claim before sending so an error cannot cause a tight retry loop.
                seen_at[object_id] = time.monotonic()
                try:
                    result = event_shop_cycle(trader, snapshot, args.timeout)
                    result["captured_at"] = datetime.now(timezone.utc).isoformat()
                    results.append(result)
                    write_jsonl(args.jsonl, result)
                    print(
                        json.dumps(
                            {
                                "name": trader["name"],
                                "object_id": object_id,
                                "distance": round(float(trader["distance"]), 2),
                                "rows": result["row_count"],
                                "response_ms": result["timings"][
                                    "response_after_send_ms"
                                ],
                            },
                            ensure_ascii=False,
                        ),
                        flush=True,
                    )
                except Exception as error:
                    failure = {
                        "name": trader["name"],
                        "object_id": object_id,
                        "distance": trader["distance"],
                        "error": f"{type(error).__name__}: {error}",
                    }
                    failures.append(failure)
                    write_jsonl(args.jsonl, {"failure": failure})
                    print(json.dumps({"failure": failure}, ensure_ascii=False), flush=True)
                if args.max_shops > 0 and len(results) >= args.max_shops:
                    break
            if args.once or (args.max_shops > 0 and len(results) >= args.max_shops):
                break

    summary: dict[str, object] = {
        "schema": 1,
        "radius": args.radius,
        "cooldown_seconds": args.cooldown,
        "elapsed_seconds": round(time.monotonic() - started, 3),
        "captured_shops": len(results),
        "failures": failures,
        "actor_refresh_ms": {
            "samples": len(scan_samples),
            "minimum": round(min(scan_samples), 3) if scan_samples else None,
            "maximum": round(max(scan_samples), 3) if scan_samples else None,
            "average": (
                round(sum(scan_samples) / len(scan_samples), 3)
                if scan_samples
                else None
            ),
        },
        "shops": results,
    }
    rendered = json.dumps(summary, ensure_ascii=False, indent=2)
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(rendered + "\n", encoding="utf-8")
    print(rendered)
    return 0 if not failures else 2


if __name__ == "__main__":
    raise SystemExit(main())
