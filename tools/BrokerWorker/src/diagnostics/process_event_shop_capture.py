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
from lu4_target_controller import (  # noqa: E402
    absolute_jump,
    patch_region,
    read_exact,
    resume_process,
    suspend_process,
    write_exact,
)


STATE_PATH = ROOT / "build" / "process_event_shop_capture_state.json"
ROUTE_PATH = ROOT / "diagnostics" / "latest_target_route.json"
FUNCTIONS_PATH = ROOT / "diagnostics" / "latest_shop_ufunctions.json"
CAPTURE_NAMES = (
    "PlayerShopBuyItemsList",
    "PlayerShopNewBuyItemsList",
    "PlayerShopNewSellItemsList",
    "PlayerShopSellItemsList",
)
TARGET_UI_NAME_INDICES = (71344, 72117)  # MyTargetSelected, TargetSelected
PROLOGUE = bytes.fromhex(
    "48 89 5C 24 10 48 89 6C 24 18 57 48 83 EC 20"
)
DATA_OFFSET = 0x1000
ROWS_OFFSET = 0x100
MAX_ROWS = 200
ROW_SIZE = 0x38
DATA_SIZE = ROWS_OFFSET + MAX_ROWS * ROW_SIZE
PRIMARY_HISTORY_DEPTH = 4
HISTORY_DEPTH = 8
RECORD_STRIDE = DATA_SIZE
CAVE_SIZE = 0x10000
HISTORY_CAVE_SIZE = 0x10000


class Code:
    def __init__(self) -> None:
        self.data = bytearray()
        self.labels: dict[str, int] = {}
        self.fixups: list[tuple[int, str]] = []

    def emit(self, value: bytes) -> None:
        self.data.extend(value)

    def label(self, name: str) -> None:
        if name in self.labels:
            raise ValueError(f"duplicate label: {name}")
        self.labels[name] = len(self.data)

    def branch32(self, opcode: bytes, label: str) -> None:
        self.emit(opcode)
        self.fixups.append((len(self.data), label))
        self.emit(b"\0\0\0\0")

    def finish(self) -> bytes:
        for displacement_offset, label in self.fixups:
            target = self.labels[label]
            displacement = target - (displacement_offset + 4)
            struct.pack_into("<i", self.data, displacement_offset, displacement)
        return bytes(self.data)


def build_stub(
    name_indices: list[int],
    data_address: int,
    record_addresses: list[int],
    command_address: int,
    process_event: int,
    continuation: int,
) -> bytes:
    code = Code()
    # Preserve flags and all volatile registers touched by the filter/copy path.
    code.emit(bytes.fromhex("9C 50 51 52 41 50 41 51 41 52 41 53 56 57"))
    # A command posted by the driver client is executed on the next game-thread
    # ProcessEvent. Clear the trigger before recursively entering ProcessEvent
    # so the nested call follows the ordinary passthrough path.
    code.emit(b"\x48\xB8" + struct.pack("<Q", command_address))
    code.emit(bytes.fromhex("83 38 01"))
    code.branch32(bytes.fromhex("0F 85"), "filter")
    code.emit(bytes.fromhex("C7 00 00 00 00 00"))
    code.emit(bytes.fromhex("C7 40 04 01 00 00 00"))
    code.emit(bytes.fromhex("48 8B 48 08"))
    code.emit(bytes.fromhex("48 8B 50 10"))
    code.emit(bytes.fromhex("4C 8B 40 18"))
    code.emit(bytes.fromhex("48 83 EC 28"))
    code.emit(b"\x48\xB8" + struct.pack("<Q", process_event))
    code.emit(bytes.fromhex("FF D0"))
    code.emit(bytes.fromhex("48 83 C4 28"))
    code.emit(b"\x48\xB8" + struct.pack("<Q", command_address))
    code.emit(bytes.fromhex("C7 40 04 02 00 00 00"))
    # Restore the original outer ProcessEvent arguments before filtering it.
    code.emit(bytes.fromhex("48 8B 4C 24 38"))
    code.emit(bytes.fromhex("48 8B 54 24 30"))
    code.emit(bytes.fromhex("4C 8B 44 24 28"))
    code.label("filter")
    code.emit(bytes.fromhex("8B 42 18"))  # mov eax, dword ptr [rdx+18h]
    for name_index in TARGET_UI_NAME_INDICES:
        code.emit(b"\x3D" + struct.pack("<I", name_index))
        code.branch32(bytes.fromhex("0F 84"), "target_ui")
    for name_index in name_indices:
        code.emit(b"\x3D" + struct.pack("<I", name_index))  # cmp eax, imm32
        code.branch32(bytes.fromhex("0F 84"), "capture")
    code.branch32(b"\xE9", "restore")

    code.label("target_ui")
    code.emit(b"\x48\xB8" + struct.pack("<Q", data_address))
    code.emit(bytes.fromhex("80 B8 C9 00 00 00 00"))
    code.branch32(bytes.fromhex("0F 84"), "restore")
    code.emit(bytes.fromhex("5F 5E 41 5B 41 5A 41 59 41 58 5A 59 58 9D C3"))

    code.label("capture")
    code.emit(b"\x48\xB8" + struct.pack("<Q", data_address))  # mov rax, data
    code.emit(bytes.fromhex("41 B9 01 00 00 00"))  # mov r9d, 1
    code.emit(bytes.fromhex("44 87 08"))  # xchg dword ptr [rax], r9d
    code.emit(bytes.fromhex("45 85 C9"))  # test r9d, r9d
    code.branch32(bytes.fromhex("0F 85"), "restore")
    # Preserve the preceding complete records before overwriting slot 0.
    # Copy backwards so overlapping logical history is never destroyed. The
    # latest slot's busy flag remains set for the whole shift/capture, allowing
    # readers to take one coherent history snapshot.
    for source_slot in range(HISTORY_DEPTH - 2, -1, -1):
        destination_slot = source_slot + 1
        code.emit(
            b"\x48\xBE"
            + struct.pack("<Q", record_addresses[source_slot])
        )
        code.emit(
            b"\x48\xBF"
            + struct.pack("<Q", record_addresses[destination_slot])
        )
        code.emit(b"\xB9" + struct.pack("<I", RECORD_STRIDE))
        code.emit(bytes.fromhex("F3 A4"))
        code.emit(b"\x48\xB8" + struct.pack("<Q", data_address))
        code.emit(b"\x48\xBF" + struct.pack("<Q", record_addresses[destination_slot]))
        code.emit(bytes.fromhex("C7 07 00 00 00 00"))
    code.emit(bytes.fromhex("48 89 48 10"))  # this
    code.emit(bytes.fromhex("48 89 50 18"))  # function
    code.emit(bytes.fromhex("4C 89 40 20"))  # params pointer
    # Ten pushes above moved ProcessEvent's original return address to
    # [rsp+50h].  Recording that call site identifies the native packet
    # dispatcher which raised the reflected shop event.
    code.emit(bytes.fromhex("4C 8B 4C 24 50"))
    code.emit(bytes.fromhex("4C 89 88 90 00 00 00"))
    # The current LU4 shop receive thunk uses a 48h-byte frame.  At the
    # ProcessEvent entry its own caller is therefore 50h bytes above the
    # immediate return address, or [rsp+A0h] after our ten pushes.
    code.emit(bytes.fromhex("4C 8B 8C 24 A0 00 00 00"))
    code.emit(bytes.fromhex("4C 89 88 98 00 00 00"))
    # RSI in the receive thunk points 10h bytes into the parser-owned source
    # structure.  Preserve it as a lead for tracing the deserializer.
    code.emit(bytes.fromhex("4C 8B 0C 24"))  # saved RDI (diagnostic context)
    code.emit(bytes.fromhex("4C 89 88 A0 00 00 00"))
    code.emit(bytes.fromhex("4C 8B 4C 24 08"))  # saved RSI
    code.emit(bytes.fromhex("4C 89 88 A8 00 00 00"))
    # PlayerShopSellItemsList's converter has an 88h-byte frame above the
    # 48h-byte receive thunk.  From this hook its caller return is at
    # [rsp+130h], which leads to the protocol parser/dispatcher.
    code.emit(bytes.fromhex("4C 8B 8C 24 30 01 00 00"))
    code.emit(bytes.fromhex("4C 89 88 B0 00 00 00"))
    # The direct shop parser owns another 58h-byte frame above the converter.
    # Its caller return is consequently at [rsp+190h] from this hook.  This is
    # the next step toward the decoded-message opcode dispatcher.
    code.emit(bytes.fromhex("4C 8B 8C 24 90 01 00 00"))
    code.emit(bytes.fromhex("4C 89 88 B8 00 00 00"))
    # Buy-list conversion uses a 78h frame instead of the sell converter's
    # 88h frame, so its parser return lives 10h lower. Keep both diagnostics;
    # the event name tells the reader which stack layout applies.
    code.emit(bytes.fromhex("4C 8B 8C 24 20 01 00 00"))
    code.emit(bytes.fromhex("4C 89 88 C0 00 00 00"))
    code.emit(bytes.fromhex("44 8B 5A 18"))  # function FName index
    code.emit(bytes.fromhex("44 89 58 68"))
    code.emit(bytes.fromhex("C7 80 88 00 00 00 00 00 00 00"))  # copied=0
    code.emit(bytes.fromhex("4D 85 C0"))
    code.branch32(bytes.fromhex("0F 84"), "publish")
    for offset in range(0, 0x40, 8):
        if offset == 0:
            code.emit(bytes.fromhex("4D 8B 08"))
        else:
            code.emit(bytes.fromhex("4D 8B 48") + bytes([offset]))
        code.emit(bytes.fromhex("4C 89 48") + bytes([0x28 + offset]))

    # Snapshot the temporary FGamePlayerShopTradeItem array before the original
    # ProcessEvent consumes and releases it. Normal/New Buy share the base array
    # at +08h; normal/New Sell share it at +10h.
    code.emit(bytes.fromhex("45 8B 10"))  # object id = [r8]
    code.emit(bytes.fromhex("44 89 50 70"))
    for buy_name_index in (71700, 71723):
        code.emit(bytes.fromhex("41 81 FB") + struct.pack("<I", buy_name_index))
        code.branch32(bytes.fromhex("0F 84"), "buy_array")
    code.branch32(b"\xE9", "sell_array")

    code.label("buy_array")
    code.emit(bytes.fromhex("C6 40 74 01"))
    code.emit(bytes.fromhex("49 8B 70 08"))
    code.emit(bytes.fromhex("41 8B 50 10"))
    code.emit(bytes.fromhex("41 8B 48 14"))
    code.branch32(b"\xE9", "copy_array")

    code.label("sell_array")
    code.emit(bytes.fromhex("C6 40 74 02"))
    code.emit(bytes.fromhex("49 8B 70 10"))
    code.emit(bytes.fromhex("41 8B 50 18"))
    code.emit(bytes.fromhex("41 8B 48 1C"))

    code.label("copy_array")
    code.emit(bytes.fromhex("48 89 70 78"))
    code.emit(bytes.fromhex("89 90 80 00 00 00"))
    code.emit(bytes.fromhex("89 88 84 00 00 00"))
    code.emit(bytes.fromhex("48 85 F6"))
    code.branch32(bytes.fromhex("0F 84"), "publish")
    code.emit(bytes.fromhex("85 D2"))
    code.branch32(bytes.fromhex("0F 8E"), "publish")
    code.emit(bytes.fromhex("81 FA C8 00 00 00"))
    code.branch32(bytes.fromhex("0F 8F"), "publish")
    code.emit(bytes.fromhex("39 D1"))
    code.branch32(bytes.fromhex("0F 8C"), "publish")
    code.emit(bytes.fromhex("81 F9 00 02 00 00"))
    code.branch32(bytes.fromhex("0F 8F"), "publish")
    code.emit(bytes.fromhex("89 90 88 00 00 00"))
    code.emit(bytes.fromhex("89 D1 6B C9 38"))
    code.emit(b"\x48\xBF" + struct.pack("<Q", data_address + ROWS_OFFSET))
    code.emit(bytes.fromhex("F3 A4"))
    code.label("publish")
    code.emit(bytes.fromhex("F0 48 FF 40 08"))  # lock inc qword [rax+8]
    code.emit(bytes.fromhex("C7 00 00 00 00 00"))  # release busy flag
    # Optional headless mode: the decoded rows have already been published, so
    # return from this shop-only ProcessEvent call before Blueprint/UI work.
    # Non-shop ProcessEvent calls always take the restore path above.
    code.emit(bytes.fromhex("80 B8 C8 00 00 00 00"))
    code.branch32(bytes.fromhex("0F 84"), "restore")
    code.emit(bytes.fromhex("5F 5E 41 5B 41 5A 41 59 41 58 5A 59 58 9D C3"))

    code.label("restore")
    code.emit(bytes.fromhex("5F 5E 41 5B 41 5A 41 59 41 58 5A 59 58 9D"))
    code.emit(PROLOGUE)
    code.emit(absolute_jump(continuation, 14))
    return code.finish()


def save_state(state: dict[str, object]) -> None:
    STATE_PATH.parent.mkdir(parents=True, exist_ok=True)
    temporary = STATE_PATH.with_suffix(".tmp")
    temporary.write_text(json.dumps(state, indent=2) + "\n", encoding="utf-8")
    temporary.replace(STATE_PATH)


def load_state() -> dict[str, object]:
    if not STATE_PATH.exists():
        raise RuntimeError(f"capture hook is not installed: {STATE_PATH}")
    return json.loads(STATE_PATH.read_text(encoding="utf-8"))


def prepare_install_state(pid: int) -> dict[str, object] | None:
    """Discard dead-PID state and reuse an intact hook for the requested PID."""
    if not STATE_PATH.exists():
        return None
    state = load_state()
    state_pid = int(state["pid"])
    try:
        with Lu4MemoryClient() as client:
            live_base = client.process_base(state_pid)
            if not live_base:
                raise RuntimeError("saved PID has no image base")
            if state_pid != pid:
                raise RuntimeError(
                    f"capture hook belongs to another live LU4 PID {state_pid}"
                )
            process_event = int(state["process_event"])
            expected_patch = bytes.fromhex(str(state["patch_hex"]))
            current = read_exact(client, pid, process_event, len(PROLOGUE))
            if live_base != int(state["image_base"]):
                raise RuntimeError("capture state image base does not match live process")
            if current == expected_patch:
                return state
            if current != PROLOGUE:
                raise RuntimeError(
                    "saved capture hook matches neither patch nor original prologue"
                )
    except OSError:
        # The saved process exited; its allocation and patch disappeared with it.
        STATE_PATH.unlink()
        return None

    # Same PID/base with the original prologue means installation never survived;
    # remove only the stale bookkeeping and perform a clean install below.
    STATE_PATH.unlink()
    return None


def resolve_inputs(pid: int) -> tuple[int, dict[str, dict[str, int]]]:
    route = json.loads(ROUTE_PATH.read_text(encoding="utf-8"))
    functions_data = json.loads(FUNCTIONS_PATH.read_text(encoding="utf-8"))
    if int(route["pid"]) != pid or int(functions_data["pid"]) != pid:
        raise RuntimeError("route/UFunction diagnostics do not match the live PID")
    process_event = int(route["process_event"], 16)
    matches: dict[str, dict[str, int]] = {
        item["name"]: {
            "address": int(item["object"]),
            "name_index": int(item["name_id"]),
        }
        for item in functions_data.get("matches", [])
        if item.get("name") in CAPTURE_NAMES
    }
    missing = [name for name in CAPTURE_NAMES if name not in matches]
    if missing:
        raise RuntimeError(f"missing shop UFunctions: {missing}")
    return process_event, matches


def install(pid: int) -> None:
    existing = prepare_install_state(pid)
    if existing is not None:
        result = dict(existing)
        result["reused"] = True
        print(json.dumps(result, indent=2))
        return
    process_event, functions = resolve_inputs(pid)
    with Lu4MemoryClient() as client:
        image_base = client.process_base(pid)
        original = read_exact(client, pid, process_event, len(PROLOGUE))
        if original != PROLOGUE:
            raise RuntimeError(
                f"ProcessEvent signature mismatch at 0x{process_event:X}: {original.hex()}"
            )
        cave, cave_size = client.allocate_process_memory(pid, CAVE_SIZE)
        try:
            history_cave, history_cave_size = client.allocate_process_memory(
                pid, HISTORY_CAVE_SIZE
            )
        except Exception:
            client.free_process_memory(pid, cave)
            raise
        data_address = cave + DATA_OFFSET
        record_addresses = [
            data_address + index * RECORD_STRIDE
            for index in range(PRIMARY_HISTORY_DEPTH)
        ] + [
            history_cave + index * RECORD_STRIDE
            for index in range(HISTORY_DEPTH - PRIMARY_HISTORY_DEPTH)
        ]
        command_address = history_cave + 0xF000
        stub = build_stub(
            [item["name_index"] for item in functions.values()],
            data_address,
            record_addresses,
            command_address,
            process_event,
            process_event + len(PROLOGUE),
        )
        if len(stub) >= DATA_OFFSET:
            client.free_process_memory(pid, history_cave)
            client.free_process_memory(pid, cave)
            raise RuntimeError("capture stub overlaps its data region")
        patch = absolute_jump(cave, len(PROLOGUE))
        patched = False
        suspended = None
        patch_mode = "suspended"
        try:
            write_exact(client, pid, cave, stub)
            write_exact(
                client,
                pid,
                data_address,
                b"\0" * (RECORD_STRIDE * PRIMARY_HISTORY_DEPTH),
            )
            write_exact(
                client,
                pid,
                history_cave,
                b"\0" * (RECORD_STRIDE * (HISTORY_DEPTH - PRIMARY_HISTORY_DEPTH)),
            )
            write_exact(client, pid, command_address, b"\0" * 0x1000)
            try:
                suspended = suspend_process(pid)
            except OSError as error:
                # active64 denies process-wide suspension after the client is
                # connected. The existing validated target hooks use the same
                # bounded driver write while live; retain the exact original
                # bytes so this remains fully reversible.
                patch_mode = f"live-driver-write:{error}"
            patch_region(client, pid, process_event, patch)
            patched = True
        except Exception:
            if patched:
                patch_region(client, pid, process_event, original)
            client.free_process_memory(pid, history_cave)
            client.free_process_memory(pid, cave)
            raise
        finally:
            if suspended is not None:
                resume_process(suspended)
        state: dict[str, object] = {
            "version": 9,
            "pid": pid,
            "image_base": image_base,
            "process_event": process_event,
            "original_hex": original.hex(),
            "patch_hex": patch.hex(),
            "cave": cave,
            "cave_size": cave_size,
            "stub_size": len(stub),
            "data": data_address,
            "history_cave": history_cave,
            "history_cave_size": history_cave_size,
            "record_addresses": record_addresses,
            "command_address": command_address,
            "command_params_address": command_address + 0x100,
            "patch_mode": patch_mode,
            "functions": functions,
            "target_ui_name_indices": list(TARGET_UI_NAME_INDICES),
        }
        save_state(state)
    print(json.dumps(state, indent=2))


def read_capture(
    client: Lu4MemoryClient, state: dict[str, object]
) -> dict[str, object]:
    pid = int(state["pid"])
    data_address = int(state["data"])
    for _ in range(20):
        raw = read_exact(client, pid, data_address, DATA_SIZE)
        busy = struct.unpack_from("<I", raw, 0)[0]
        sequence = struct.unpack_from("<Q", raw, 8)[0]
        if busy == 0:
            confirm = struct.unpack(
                "<Q", read_exact(client, pid, data_address + 8, 8)
            )[0]
            if confirm == sequence:
                receiver, function, params = struct.unpack_from("<QQQ", raw, 0x10)
                name_index = struct.unpack_from("<I", raw, 0x68)[0]
                object_id = struct.unpack_from("<i", raw, 0x70)[0]
                side_code = raw[0x74]
                source_rows = struct.unpack_from("<Q", raw, 0x78)[0]
                count, capacity, copied_count = struct.unpack_from(
                    "<iii", raw, 0x80
                )
                caller = struct.unpack_from("<Q", raw, 0x90)[0]
                upstream_caller = struct.unpack_from("<Q", raw, 0x98)[0]
                saved_rdi, saved_rsi = struct.unpack_from("<QQ", raw, 0xA0)
                converter_caller = struct.unpack_from("<Q", raw, 0xB0)[0]
                parser_caller = struct.unpack_from("<Q", raw, 0xB8)[0]
                alternate_converter_caller = struct.unpack_from("<Q", raw, 0xC0)[0]
                suppress_ui = raw[0xC8] != 0
                suppress_target_ui = raw[0xC9] != 0
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
                                "item_object_id": struct.unpack_from(
                                    "<i", raw, offset
                                )[0],
                                "item_id": struct.unpack_from(
                                    "<i", raw, offset + 0x04
                                )[0],
                                "count": struct.unpack_from(
                                    "<i", raw, offset + 0x08
                                )[0],
                                "enchant_level": struct.unpack_from(
                                    "<i", raw, offset + 0x14
                                )[0],
                                "price": struct.unpack_from(
                                    "<i", raw, offset + 0x2C
                                )[0],
                                "buy_count": struct.unpack_from(
                                    "<i", raw, offset + 0x30
                                )[0],
                                "base_price": struct.unpack_from(
                                    "<i", raw, offset + 0x34
                                )[0],
                            }
                        )
                return {
                    "sequence": sequence,
                    "receiver": f"0x{receiver:X}",
                    "function": f"0x{function:X}",
                    "function_name_index": name_index,
                    "function_name": names.get(name_index, ""),
                    "params_pointer": f"0x{params:X}",
                    "process_event_caller": f"0x{caller:X}",
                    "receive_thunk_caller": f"0x{upstream_caller:X}",
                    "saved_rdi": f"0x{saved_rdi:X}",
                    "source_struct": f"0x{saved_rsi - 0x10:X}" if saved_rsi >= 0x10 else "0x0",
                    "converter_caller": f"0x{converter_caller:X}",
                    "parser_caller": f"0x{parser_caller:X}",
                    "alternate_converter_caller": f"0x{alternate_converter_caller:X}",
                    "suppress_ui": suppress_ui,
                    "suppress_target_ui": suppress_target_ui,
                    "params_hex": raw[0x28:0x68].hex(" "),
                    "params_qwords": list(struct.unpack_from("<8Q", raw, 0x28)),
                    "object_id": object_id,
                    "side": {1: "buy", 2: "sell"}.get(side_code, ""),
                    "source_rows": f"0x{source_rows:X}",
                    "count": count,
                    "capacity": capacity,
                    "copied_count": copied_count,
                    "rows": rows,
                }
        time.sleep(0.001)
    raise RuntimeError("could not read a coherent capture record")


def status() -> None:
    state = load_state()
    pid = int(state["pid"])
    with Lu4MemoryClient() as client:
        result = read_capture(client, state)
        result.update(
            {
                "pid": pid,
                "patch_matches": read_exact(
                    client,
                    pid,
                    int(state["process_event"]),
                    len(PROLOGUE),
                ).hex()
                == str(state["patch_hex"]),
            }
        )
    print(json.dumps(result, indent=2))


def set_suppress_ui(enabled: bool) -> None:
    state = load_state()
    pid = int(state["pid"])
    address = int(state["data"]) + 0xC8
    value = b"\x01" if enabled else b"\x00"
    with Lu4MemoryClient() as client:
        write_exact(client, pid, address, value)
        if read_exact(client, pid, address, 1) != value:
            raise RuntimeError("failed to update shop UI suppression flag")
    print(json.dumps({"pid": pid, "suppress_ui": enabled}, indent=2))


def set_suppress_target_ui(enabled: bool) -> None:
    state = load_state()
    pid = int(state["pid"])
    address = int(state["data"]) + 0xC9
    value = b"\x01" if enabled else b"\x00"
    with Lu4MemoryClient() as client:
        write_exact(client, pid, address, value)
        if read_exact(client, pid, address, 1) != value:
            raise RuntimeError("failed to update target UI suppression flag")
    print(json.dumps({"pid": pid, "suppress_target_ui": enabled}, indent=2))


def wait_capture(after: int, timeout: float) -> None:
    state = load_state()
    deadline = time.monotonic() + timeout
    with Lu4MemoryClient() as client:
        while time.monotonic() < deadline:
            result = read_capture(client, state)
            if int(result["sequence"]) > after:
                print(json.dumps(result, indent=2))
                return
            time.sleep(0.002)
    raise TimeoutError(f"no shop ProcessEvent after sequence {after}")


def uninstall() -> None:
    state = load_state()
    pid = int(state["pid"])
    process_event = int(state["process_event"])
    original = bytes.fromhex(str(state["original_hex"]))
    patch = bytes.fromhex(str(state["patch_hex"]))
    with Lu4MemoryClient() as client:
        current = read_exact(client, pid, process_event, len(original))
        if current != patch:
            raise RuntimeError("ProcessEvent patch no longer matches saved state")
        suspended = None
        try:
            try:
                suspended = suspend_process(pid)
            except OSError:
                suspended = None
            patch_region(client, pid, process_event, original)
        finally:
            if suspended is not None:
                resume_process(suspended)
        # Process-wide suspension is not always available for this client.
        # A thread may already have entered the old trampoline when the entry
        # point is restored. Keep its code and capture data mapped until the
        # process exits; freeing either allocation here is a use-after-free.
    STATE_PATH.unlink()
    print("restored ProcessEvent shop prologue; retired capture allocations remain mapped")


def main() -> int:
    parser = argparse.ArgumentParser(description="Capture LU4 private-shop ProcessEvent params")
    sub = parser.add_subparsers(dest="command", required=True)
    install_parser = sub.add_parser("install")
    install_parser.add_argument("pid", type=int)
    sub.add_parser("status")
    suppress_parser = sub.add_parser("suppress-ui")
    suppress_parser.add_argument("mode", choices=("on", "off"))
    target_ui_parser = sub.add_parser("suppress-target-ui")
    target_ui_parser.add_argument("mode", choices=("on", "off"))
    wait_parser = sub.add_parser("wait")
    wait_parser.add_argument("--after", type=int, default=0)
    wait_parser.add_argument("--timeout", type=float, default=20.0)
    sub.add_parser("uninstall")
    args = parser.parse_args()
    if args.command == "install":
        install(args.pid)
    elif args.command == "status":
        status()
    elif args.command == "suppress-ui":
        set_suppress_ui(args.mode == "on")
    elif args.command == "suppress-target-ui":
        set_suppress_target_ui(args.mode == "on")
    elif args.command == "wait":
        wait_capture(args.after, args.timeout)
    elif args.command == "uninstall":
        uninstall()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
