"""Offline replay of one captured world request. Never prints buffer contents.

Requires pefile and unicorn. Capture files must stay outside the repository.
This emulates a specific driver build; it does not modify a live process/driver.
"""

import argparse
import hashlib
import json
import struct
from pathlib import Path

import pefile
import unicorn as uc
from unicorn.x86_const import (
    UC_X86_REG_RAX, UC_X86_REG_RCX, UC_X86_REG_RDX, UC_X86_REG_R8,
    UC_X86_REG_R9, UC_X86_REG_RIP, UC_X86_REG_RSP,
)

RECORD_SIZE = 0x54480
SLOT = 0x4068
GLOBAL_RVA = 0x12AB000
BASE_RECORD = 0x400000000


def sha(data):
    return hashlib.sha256(data).hexdigest()


def rc4(key, data):
    state = list(range(256))
    j = 0
    for i in range(256):
        j = (j + state[i] + key[i % len(key)]) & 255
        state[i], state[j] = state[j], state[i]
    i = j = 0
    output = bytearray()
    for byte in data:
        i = (i + 1) & 255
        j = (j + state[i]) & 255
        state[i], state[j] = state[j], state[i]
        output.append(byte ^ state[(state[i] + state[j]) & 255])
    return bytes(output)


def ranges(offsets):
    result = []
    for value in sorted(offsets):
        if result and result[-1][1] == value:
            result[-1][1] += 1
        else:
            result.append([value, value + 1])
    return [{"offset": hex(start), "length": end - start} for start, end in result]


def private_capture(path):
    path = Path(path).resolve()
    repository = Path(__file__).resolve().parents[2]
    if path == repository or repository in path.parents:
        raise ValueError("Raw capture must be outside the repository")
    return path


def replay(driver, capture, override_globals=None, output_sink=None):
    capture = private_capture(capture)
    raw_image = Path(driver).read_bytes()
    metadata = json.loads((capture / "capture.json").read_text())
    if sha(raw_image) != metadata["driver_sha256"]:
        raise ValueError("Driver version differs from capture")
    record = (capture / "before.record").read_bytes()
    after = (capture / "after.record").read_bytes()
    source = (capture / "before.user").read_bytes()
    wire = (capture / "after.user").read_bytes()
    globals_data = override_globals or (capture / "globals.bin").read_bytes()
    if (len(record), len(after), len(source), len(wire), len(globals_data)) != (
            RECORD_SIZE, RECORD_SIZE, 69, 69, 4096):
        raise ValueError("Unexpected capture dimensions")
    if wire != after[SLOT:SLOT + 69] or record[0x58:0x60] != after[0x58:0x60]:
        raise ValueError("Capture record identity / wire validation failed")
    if struct.unpack_from("<H", source)[0] != 37:
        raise ValueError("Expected a 37-byte source packet")
    pe = pefile.PE(data=raw_image)
    base = pe.OPTIONAL_HEADER.ImageBase
    image_size = (pe.OPTIONAL_HEADER.SizeOfImage + 4095) & ~4095
    imports = {item.address - base: item.name for entry in pe.DIRECTORY_ENTRY_IMPORT
               for item in entry.imports}
    if base != 0x140000000 or imports.get(0x144CC8) != b"__chkstk":
        raise ValueError("Unsupported image layout")
    machine = uc.Uc(uc.UC_ARCH_X86, uc.UC_MODE_64)
    machine.mem_map(base, image_size)
    machine.mem_write(base, pe.get_memory_mapped_image())
    stack, record_base, args, sentinel = 0x300000000, BASE_RECORD, 0x500000000, 0x600000000
    for address, size in ((stack, 0x100000), (record_base, 0x55000),
                          (args, 0x1000), (sentinel, 0x1000)):
        machine.mem_map(address, size)
    rsp = stack + 0x80008
    machine.reg_write(UC_X86_REG_RSP, rsp)
    machine.mem_write(rsp, struct.pack("<Q", sentinel))
    for register, value in ((UC_X86_REG_RCX, record_base), (UC_X86_REG_RDX, 0),
                            (UC_X86_REG_R8, record_base + SLOT), (UC_X86_REG_R9, args)):
        machine.reg_write(register, value)
    machine.mem_write(args, struct.pack("<I", 37))
    # Caller-compatible capacity / output scratch pointers. Not captured registers.
    for offset, value in ((0x28, 0x4000), (0x30, args + 8), (0x38, args + 16),
                          (0x40, args + 24), (0x48, args + 32)):
        machine.mem_write(rsp + offset, struct.pack("<Q", value))
    machine.mem_write(record_base, record)
    machine.mem_write(record_base + SLOT, source[:37])
    machine.mem_write(base + GLOBAL_RVA, globals_data)
    global_reads, record_reads, field_stores, calls = set(), set(), set(), set()
    field = None
    instruction_count = 0
    previous_was_call = False
    call_cache = {}
    stub_counts = {"__chkstk": 0, "security_cookie": 0}

    def return_from_stub():
        sp = machine.reg_read(UC_X86_REG_RSP)
        machine.reg_write(UC_X86_REG_RIP, struct.unpack("<Q", machine.mem_read(sp, 8))[0])
        machine.reg_write(UC_X86_REG_RSP, sp + 8)

    def code_hook(machine, pc, size, _):
        nonlocal field, instruction_count, previous_was_call
        instruction_count += 1
        if previous_was_call:
            calls.add(pc - base)
        if pc not in call_cache:
            opcode = bytes(machine.mem_read(pc, min(size, 2)))
            call_cache[pc] = (opcode[0] == 0xE8 or
                              (len(opcode) == 2 and opcode[0] == 0xFF and
                               (opcode[1] >> 3) & 7 == 2))
        previous_was_call = call_cache[pc]
        if pc == base + 0x95250 and machine.reg_read(UC_X86_REG_RDX) == record_base + SLOT:
            field = bytes(machine.mem_read(record_base + SLOT + 37, 32))
        if pc == base + 0x142D60:
            stub_counts["__chkstk"] += 1
            return_from_stub()
        elif pc == base + 0x142790:
            cookie = struct.unpack("<Q", machine.mem_read(base + 0x163980, 8))[0]
            if machine.reg_read(UC_X86_REG_RCX) != cookie:
                raise RuntimeError("Emulated security cookie mismatch")
            stub_counts["security_cookie"] += 1
            return_from_stub()
        elif not base <= pc < base + image_size:
            raise RuntimeError("Execution left mapped driver image")

    def memory_hook(machine, access, address, size, value, _):
        if access == uc.UC_MEM_READ:
            if base + GLOBAL_RVA <= address < base + GLOBAL_RVA + 4096:
                global_reads.update(range(address - base, address - base + size))
            if record_base <= address < record_base + RECORD_SIZE:
                record_reads.update(range(address - record_base, address - record_base + size))
        elif record_base + SLOT + 37 <= address < record_base + SLOT + 69 and size >= 8:
            field_stores.add(machine.reg_read(UC_X86_REG_RIP) - base)

    machine.hook_add(uc.UC_HOOK_CODE, code_hook)
    machine.hook_add(uc.UC_HOOK_MEM_READ | uc.UC_HOOK_MEM_WRITE, memory_hook)
    machine.emu_start(base + 0x95620, sentinel, count=1000000, timeout=10000000)
    completed = machine.reg_read(UC_X86_REG_RIP) == sentinel
    output = bytes(machine.mem_read(record_base + SLOT, 69))
    decoded_global = rc4(globals_data[0xDA4:0xDB8], globals_data[0xDB8:0xDDE])
    summary = {
        "driver_sha256": sha(raw_image), "completed": completed,
        "instructions": instruction_count, "return_byte": machine.reg_read(UC_X86_REG_RAX) & 255,
        "output_length": struct.unpack("<I", machine.mem_read(args, 4))[0],
        "wire_exact": output == wire, "output_sha256": sha(output),
        "field_before_final_transforms_sha256": sha(field) if field else None,
        "field_equals_decoded_global_1_33": field == decoded_global[1:33],
        "global_reads": ranges(global_reads), "record_reads": ranges(record_reads),
        "field_store_rvas": [hex(pc) for pc in sorted(field_stores)],
        "call_target_rvas": [hex(pc) for pc in sorted(calls)], "compiler_stubs": stub_counts,
    }
    if output_sink is not None and completed and summary["return_byte"] == 1:
        output_sink(output)
    return summary, field, decoded_global


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--driver", type=Path, required=True)
    parser.add_argument("--capture", type=Path, required=True)
    args = parser.parse_args()
    summary, _, _ = replay(args.driver, args.capture)
    print(json.dumps(summary, indent=2))
    if not summary["completed"] or not summary["wire_exact"]:
        raise SystemExit(1)
