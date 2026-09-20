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

    parser = argparse.ArgumentParser(description="PriceCheck embedded broker collector")
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    collect(args.pid, args.output.resolve())
    print(json.dumps({"pid": args.pid, "output": str(args.output.resolve())}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
