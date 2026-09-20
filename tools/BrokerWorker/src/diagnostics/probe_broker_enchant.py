from __future__ import annotations

import argparse
import contextlib
import io
import json
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CLIENT = ROOT / "client"
DIAGNOSTICS = ROOT / "diagnostics"
BUILD = ROOT / "build"
sys.path.insert(0, str(CLIENT))
sys.path.insert(0, str(DIAGNOSTICS))

from broker_query import send_search  # noqa: E402
from process_event_broker_capture import install, uninstall  # noqa: E402


def run_script(script: Path, *arguments: object) -> None:
    completed = subprocess.run(
        [sys.executable, str(script), *(str(value) for value in arguments)],
        cwd=ROOT,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=120,
    )
    if completed.returncode != 0:
        details = "\n".join(
            value.strip() for value in (completed.stdout, completed.stderr) if value.strip()
        )
        raise RuntimeError(f"{script.name} failed ({completed.returncode}):\n{details}")


def prepare(pid: int) -> None:
    BUILD.mkdir(parents=True, exist_ok=True)
    run_script(CLIENT / "lu4_target_session.py", "--pid", pid, "prepare")
    run_script(DIAGNOSTICS / "resolve_target_route.py", pid, "--json", DIAGNOSTICS / "latest_target_route.json")
    run_script(DIAGNOSTICS / "discover_unreal_globals.py", pid, "--json", DIAGNOSTICS / "latest_unreal_globals.json")
    run_script(
        DIAGNOSTICS / "inspect_shop_ufunctions.py",
        pid,
        "--globals",
        DIAGNOSTICS / "latest_unreal_globals.json",
        "--json",
        DIAGNOSTICS / "latest_shop_ufunctions.json",
    )


def main() -> int:
    parser = argparse.ArgumentParser(description="Probe ItemBroker EnchantMin semantics")
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--item-id", type=int, required=True)
    parser.add_argument("--store-type", type=int, default=1)
    parser.add_argument("--max-enchant", type=int, default=20)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    target_prepared = False
    broker_installed = False
    try:
        prepare(args.pid)
        target_prepared = True
        with contextlib.redirect_stdout(io.StringIO()):
            install(args.pid)
        broker_installed = True
        levels = []
        for enchant_min in range(args.max_enchant + 1):
            with contextlib.redirect_stdout(io.StringIO()):
                response = send_search(args.item_id, args.store_type, enchant_min, 10.0)
            rows = [
                {
                    "object_id": int(row["object_id"]),
                    "amount": int(row["amount"]),
                }
                for row in response["rows"]
            ]
            levels.append({"enchant_min": enchant_min, "rows": rows})
            if enchant_min > 0 and not rows:
                break
        output = {
            "pid": args.pid,
            "item_id": args.item_id,
            "store_type": args.store_type,
            "levels": levels,
        }
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(output, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(output, indent=2))
        return 0
    finally:
        if broker_installed and (BUILD / "process_event_broker_capture_state.json").exists():
            with contextlib.redirect_stdout(io.StringIO()):
                uninstall()
        if target_prepared and (BUILD / "lu4_target_hook_state.json").exists():
            run_script(CLIENT / "lu4_target_session.py", "cleanup")


if __name__ == "__main__":
    raise SystemExit(main())
