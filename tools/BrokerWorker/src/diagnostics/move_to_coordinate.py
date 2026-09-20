from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "client"))
sys.path.insert(0, str(ROOT / "diagnostics"))

from native_client_move import move_and_wait  # noqa: E402


def main() -> int:
    parser = argparse.ArgumentParser(description="Move the LU4 player to one world coordinate")
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--target-x", type=float, required=True)
    parser.add_argument("--target-y", type=float, required=True)
    parser.add_argument("--radius", type=float, default=8.0)
    parser.add_argument("--timeout", type=float, default=90.0)
    parser.add_argument("--json", type=Path, required=True)
    args = parser.parse_args()
    snapshot = json.loads((ROOT / "diagnostics" / "latest_actor_snapshot.json").read_text(encoding="utf-8"))
    if int(snapshot.get("pid", -1)) != args.pid:
        raise RuntimeError("cached actor snapshot belongs to another PID")
    result = move_and_wait(
        args.pid,
        int(str(snapshot["controller"]), 16),
        int(str(snapshot["player_actor"]), 16),
        args.target_x,
        args.target_y,
        args.radius,
        args.timeout,
    )
    args.json.parent.mkdir(parents=True, exist_ok=True)
    args.json.write_text(
        json.dumps(result, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
