from __future__ import annotations

import argparse
import contextlib  # bundled for dynamically dispatched helpers
import csv  # bundled for dynamically dispatched helpers
import ctypes  # bundled for dynamically dispatched helpers
import ctypes.wintypes  # bundled for dynamically dispatched helpers
import json
import math  # bundled for dynamically dispatched helpers
import subprocess
import sys
import unicodedata  # bundled for dynamically dispatched helpers
from pathlib import Path


ROOT = Path(__file__).resolve().parent
BUILD = ROOT / "build"
DIAGNOSTICS = ROOT / "diagnostics"
CLIENT = ROOT / "client"


def run_script(script: Path, *arguments: object, timeout: int = 300) -> str:
    completed = subprocess.run(
        [sys.executable, str(script), *(str(value) for value in arguments)],
        cwd=ROOT,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
    )
    if completed.returncode != 0:
        details = "\n".join(
            value.strip() for value in (completed.stdout, completed.stderr) if value.strip()
        )
        raise RuntimeError(f"{script.name} failed ({completed.returncode}):\n{details}")
    return completed.stdout.strip()


def remove_stale_state(path: Path, pid: int) -> None:
    if not path.exists():
        return
    try:
        state = json.loads(path.read_text(encoding="utf-8"))
        if int(state.get("pid", -1)) == pid:
            return
    except (OSError, ValueError, TypeError):
        pass
    path.unlink(missing_ok=True)


def json_matches_pid(path: Path, pid: int) -> bool:
    try:
        return int(json.loads(path.read_text(encoding="utf-8")).get("pid", -1)) == pid
    except (OSError, ValueError, TypeError):
        return False


def collect(pid: int, output: Path) -> None:
    BUILD.mkdir(parents=True, exist_ok=True)
    DIAGNOSTICS.mkdir(parents=True, exist_ok=True)
    state_names = (
        "lu4_target_hook_state.json",
        "process_event_broker_capture_state.json",
        "process_event_shop_capture_state.json",
    )
    for name in state_names:
        remove_stale_state(BUILD / name, pid)

    target_prepared = False
    broker_installed = False
    try:
        target_prepared = True
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
        run_script(DIAGNOSTICS / "process_event_broker_capture.py", "install", pid)
        broker_installed = True
        run_script(
            DIAGNOSTICS / "collect_full_broker_inventory.py",
            "--store-types",
            "1,3,8",
            "--batch-size",
            32,
            "--timeout",
            12,
            "--output",
            output,
            timeout=600,
        )
    finally:
        if broker_installed and (BUILD / "process_event_broker_capture_state.json").exists():
            try:
                run_script(DIAGNOSTICS / "process_event_broker_capture.py", "uninstall")
            except Exception:
                pass
        if target_prepared and (BUILD / "lu4_target_hook_state.json").exists():
            try:
                run_script(CLIENT / "lu4_target_session.py", "cleanup")
            except Exception:
                pass


def prepare_price(pid: int) -> None:
    BUILD.mkdir(parents=True, exist_ok=True)
    DIAGNOSTICS.mkdir(parents=True, exist_ok=True)
    for name in ("lu4_target_hook_state.json", "process_event_shop_capture_state.json"):
        remove_stale_state(BUILD / name, pid)
    target_state = BUILD / "lu4_target_hook_state.json"
    if not target_state.exists():
        run_script(CLIENT / "lu4_target_session.py", "--pid", pid, "prepare")
    route_path = DIAGNOSTICS / "latest_target_route.json"
    globals_path = DIAGNOSTICS / "latest_unreal_globals.json"
    functions_path = DIAGNOSTICS / "latest_shop_ufunctions.json"
    if not json_matches_pid(route_path, pid):
        run_script(DIAGNOSTICS / "resolve_target_route.py", pid, "--json", DIAGNOSTICS / "latest_target_route.json")
    if not json_matches_pid(globals_path, pid):
        run_script(DIAGNOSTICS / "discover_unreal_globals.py", pid, "--json", DIAGNOSTICS / "latest_unreal_globals.json")
    if not json_matches_pid(functions_path, pid):
        run_script(DIAGNOSTICS / "inspect_shop_ufunctions.py", pid, "--globals", DIAGNOSTICS / "latest_unreal_globals.json", "--json", DIAGNOSTICS / "latest_shop_ufunctions.json")
    run_script(DIAGNOSTICS / "process_event_shop_capture.py", "install", pid)
    run_script(DIAGNOSTICS / "process_event_shop_capture.py", "suppress-ui", "on")
    run_script(DIAGNOSTICS / "process_event_shop_capture.py", "suppress-target-ui", "on")


def collect_price(
    pid: int,
    object_id: int,
    output: Path,
    kiosk_type: int | None = None,
    trader_name: str | None = None,
    target_x: float | None = None,
    target_y: float | None = None,
) -> None:
    prepare_price(pid)
    snapshot = DIAGNOSTICS / "latest_actor_snapshot.json"
    if not json_matches_pid(snapshot, pid):
        run_script(
            DIAGNOSTICS / "scan_lu4_actors.py",
            pid,
            "--limit",
            100,
            "--json",
            snapshot,
        )
    arguments: list[object] = [
        "--object-id",
        object_id,
        "--pid",
        pid,
        "--timeout",
        45,
        "--json",
        output,
    ]
    arguments.extend(("--kiosk-type", kiosk_type or 0))
    if trader_name:
        arguments.extend(("--trader-name", trader_name))
    if target_x is not None:
        arguments.extend(("--target-x", target_x))
    if target_y is not None:
        arguments.extend(("--target-y", target_y))
    run_script(DIAGNOSTICS / "event_shop_cycle.py", *arguments, timeout=60)


def sweep_prices(
    pid: int,
    output: Path,
    radius: float,
    max_shops: int,
    batch_size: int,
) -> None:
    prepare_price(pid)
    snapshot = DIAGNOSTICS / "latest_actor_snapshot.json"
    if not json_matches_pid(snapshot, pid):
        run_script(
            DIAGNOSTICS / "scan_lu4_actors.py",
            pid,
            "--limit",
            100,
            "--json",
            snapshot,
        )
    run_script(
        DIAGNOSTICS / "fast_headless_shop_sweep.py",
        "--radius",
        radius,
        "--max-shops",
        max_shops,
        "--batch-size",
        batch_size,
        "--timeout",
        2,
        "--json",
        output,
        timeout=60,
    )


def collect_price_batch(pid: int, input_path: Path, output: Path) -> None:
    prepare_price(pid)
    snapshot = DIAGNOSTICS / "latest_actor_snapshot.json"
    if not json_matches_pid(snapshot, pid):
        run_script(DIAGNOSTICS / "scan_lu4_actors.py", pid, "--limit", 100, "--json", snapshot)
    run_script(
        DIAGNOSTICS / "event_shop_batch.py",
        "--pid",
        pid,
        "--input",
        input_path.resolve(),
        "--timeout",
        2,
        "--json",
        output,
        timeout=15,
    )


def move_to_coordinate(pid: int, output: Path, target_x: float, target_y: float, radius: float) -> None:
    prepare_price(pid)
    snapshot = DIAGNOSTICS / "latest_actor_snapshot.json"
    if not json_matches_pid(snapshot, pid):
        run_script(DIAGNOSTICS / "scan_lu4_actors.py", pid, "--limit", 100, "--json", snapshot)
    run_script(
        DIAGNOSTICS / "move_to_coordinate.py",
        "--pid", pid,
        "--target-x", target_x,
        "--target-y", target_y,
        "--radius", radius,
        "--timeout", 90,
        "--json", output,
        timeout=100,
    )


def cleanup_price(pid: int) -> None:
    remove_stale_state(BUILD / "process_event_shop_capture_state.json", pid)
    remove_stale_state(BUILD / "lu4_target_hook_state.json", pid)
    if (BUILD / "process_event_shop_capture_state.json").exists():
        run_script(DIAGNOSTICS / "process_event_shop_capture.py", "uninstall")
    if (BUILD / "lu4_target_hook_state.json").exists():
        run_script(CLIENT / "lu4_target_session.py", "cleanup")


def main() -> int:
    # Python helpers launch sys.executable with a script path. In the frozen
    # worker sys.executable is BrokerWorker.exe, so dispatch that invocation
    # back through runpy while keeping every helper inside this distribution.
    if len(sys.argv) > 1 and sys.argv[1].lower().endswith(".py"):
        import runpy

        script = Path(sys.argv[1]).resolve()
        sys.path.insert(0, str(script.parent))
        sys.argv = [str(script), *sys.argv[2:]]
        runpy.run_path(str(script), run_name="__main__")
        return 0

    parser = argparse.ArgumentParser(description="PriceCheck embedded market worker")
    parser.add_argument("--mode", choices=("broker", "price-prepare", "price", "price-batch", "price-sweep", "move", "cleanup"), default="broker")
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--object-id", type=int)
    parser.add_argument("--kiosk-type", type=int, choices=(1, 3, 8))
    parser.add_argument("--trader-name")
    parser.add_argument("--target-x", type=float)
    parser.add_argument("--target-y", type=float)
    parser.add_argument("--radius", type=float, default=95.0)
    parser.add_argument("--max-shops", type=int, default=16)
    parser.add_argument("--batch-size", type=int, default=16)
    parser.add_argument("--input", type=Path)
    args = parser.parse_args()
    if args.mode == "price-prepare":
        prepare_price(args.pid)
    elif args.mode == "price":
        if args.object_id is None:
            parser.error("--object-id is required in price mode")
        collect_price(
            args.pid,
            args.object_id,
            args.output.resolve(),
            args.kiosk_type,
            args.trader_name,
            args.target_x,
            args.target_y,
        )
    elif args.mode == "price-sweep":
        sweep_prices(
            args.pid,
            args.output.resolve(),
            args.radius,
            args.max_shops,
            args.batch_size,
        )
    elif args.mode == "price-batch":
        if args.input is None:
            parser.error("--input is required in price-batch mode")
        collect_price_batch(args.pid, args.input, args.output.resolve())
    elif args.mode == "move":
        if args.target_x is None or args.target_y is None:
            parser.error("--target-x and --target-y are required in move mode")
        move_to_coordinate(args.pid, args.output.resolve(), args.target_x, args.target_y, args.radius)
    elif args.mode == "cleanup":
        cleanup_price(args.pid)
    else:
        collect(args.pid, args.output.resolve())
    print(json.dumps({"pid": args.pid, "output": str(args.output.resolve())}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
