from __future__ import annotations

import argparse
import json
import struct
from pathlib import Path

import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
from unicorn import UC_ARCH_X86, UC_MODE_64, UC_PROT_ALL, Uc
from unicorn.x86_const import (
    UC_X86_REG_R8,
    UC_X86_REG_R9,
    UC_X86_REG_RAX,
    UC_X86_REG_RCX,
    UC_X86_REG_RDX,
    UC_X86_REG_RSP,
)


PROJECT_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_IMAGE = PROJECT_ROOT / "diagnostics" / "herzbot-live-reconstructed.exe"
MAIN_GENERATOR_RVA = 0x003EFF00
POST_GENERATOR_RVA = 0x003EF790
CPU_OPT_LEVEL_RVA = 0x0068BBE8

OUTPUT_ADDRESS = 0x0000000200000000
ORIGINAL_BYTES_ADDRESS = 0x0000000200002000
COUNT_ADDRESS = 0x0000000200003000
STACK_ADDRESS = 0x0000000300000000
RETURN_ADDRESS = 0x0000000400000000


def map_generator(image: Path) -> tuple[Uc, int]:
    pe = pefile.PE(str(image))
    image_base = pe.OPTIONAL_HEADER.ImageBase
    image_size = (pe.OPTIONAL_HEADER.SizeOfImage + 0xFFF) & ~0xFFF
    emulator = Uc(UC_ARCH_X86, UC_MODE_64)
    emulator.mem_map(image_base, image_size, UC_PROT_ALL)
    emulator.mem_write(
        image_base, pe.get_memory_mapped_image(ImageBase=image_base)
    )
    # The generator has scalar and AVX2 copies of the same relocation loop.
    # Selecting the scalar path makes emulation independent of host AVX state.
    emulator.mem_write(image_base + CPU_OPT_LEVEL_RVA, struct.pack("<I", 0))
    for address, size in (
        (OUTPUT_ADDRESS, 0x2000),
        (ORIGINAL_BYTES_ADDRESS, 0x1000),
        (COUNT_ADDRESS, 0x1000),
        (STACK_ADDRESS, 0x20000),
        (RETURN_ADDRESS, 0x1000),
    ):
        emulator.mem_map(address, size, UC_PROT_ALL)
    return emulator, image_base


def prepare_call(emulator: Uc, args: list[int]) -> None:
    stack_pointer = STACK_ADDRESS + 0x1F000
    emulator.mem_write(stack_pointer, struct.pack("<Q", RETURN_ADDRESS))
    registers = (UC_X86_REG_RCX, UC_X86_REG_RDX, UC_X86_REG_R8, UC_X86_REG_R9)
    for register, value in zip(registers, args[:4]):
        emulator.reg_write(register, value)
    for index, value in enumerate(args[4:]):
        emulator.mem_write(
            stack_pointer + 0x28 + index * 8, struct.pack("<Q", value)
        )
    emulator.reg_write(UC_X86_REG_RSP, stack_pointer)


def run_main_generator(
    image: Path,
    cave: int,
    hook: int,
    send_address: int,
    send_first_five: bytes,
) -> tuple[bytes, int]:
    emulator, image_base = map_generator(image)
    emulator.mem_write(ORIGINAL_BYTES_ADDRESS, send_first_five)
    prepare_call(
        emulator,
        [
            OUTPUT_ADDRESS,
            cave,
            hook,
            ORIGINAL_BYTES_ADDRESS,
            send_address + 5,
            COUNT_ADDRESS,
        ],
    )
    emulator.emu_start(
        image_base + MAIN_GENERATOR_RVA, RETURN_ADDRESS, count=2_000_000
    )
    length = emulator.reg_read(UC_X86_REG_RAX) & 0xFFFFFFFF
    trampoline_offset = struct.unpack(
        "<I", emulator.mem_read(COUNT_ADDRESS, 4)
    )[0]
    return bytes(emulator.mem_read(OUTPUT_ADDRESS, length)), trampoline_offset


def run_post_generator(image: Path, cave: int, hook: int) -> bytes:
    emulator, image_base = map_generator(image)
    prepare_call(emulator, [OUTPUT_ADDRESS, cave, hook])
    emulator.emu_start(
        image_base + POST_GENERATOR_RVA, RETURN_ADDRESS, count=1_000_000
    )
    length = emulator.reg_read(UC_X86_REG_RAX) & 0xFFFFFFFF
    return bytes(emulator.mem_read(OUTPUT_ADDRESS, length))


def indirect_jumps(code: bytes) -> list[dict[str, int]]:
    marker = b"\xFF\x25\x00\x00\x00\x00"
    result: list[dict[str, int]] = []
    start = 0
    while True:
        offset = code.find(marker, start)
        if offset < 0:
            return result
        target = struct.unpack_from("<Q", code, offset + len(marker))[0]
        result.append({"offset": offset, "target": target})
        start = offset + 1


def disassemble(code: bytes, virtual_address: int) -> str:
    engine = Cs(CS_ARCH_X86, CS_MODE_64)
    return "\n".join(
        f"{instruction.address:016X}  {instruction.bytes.hex(' '):<32} "
        f"{instruction.mnemonic} {instruction.op_str}".rstrip()
        for instruction in engine.disasm(code, virtual_address)
    )


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Offline emulation of Herz LU4 DirectHook code generators."
    )
    parser.add_argument("--image", type=Path, default=DEFAULT_IMAGE)
    parser.add_argument("--output-dir", type=Path, default=PROJECT_ROOT / "diagnostics")
    parser.add_argument("--cave", type=lambda value: int(value, 0), default=0x1111111100000000)
    parser.add_argument("--direct-hook", type=lambda value: int(value, 0), default=0x2222222200000000)
    parser.add_argument("--post-hook", type=lambda value: int(value, 0), default=0x2222222201000000)
    parser.add_argument("--send", type=lambda value: int(value, 0), default=0x3333333300000000)
    parser.add_argument("--send-first-five", default="90 90 90 90 90")
    args = parser.parse_args()

    send_first_five = bytes.fromhex(args.send_first_five)
    if len(send_first_five) != 5:
        raise SystemExit("--send-first-five must contain exactly five bytes")

    main_stub, trampoline_offset = run_main_generator(
        args.image, args.cave, args.direct_hook, args.send, send_first_five
    )
    post_stub = run_post_generator(args.image, args.cave, args.post_hook)
    args.output_dir.mkdir(parents=True, exist_ok=True)

    main_path = args.output_dir / "herz_lu4_direct_stub.bin"
    post_path = args.output_dir / "herz_lu4_post_send_stub.bin"
    main_path.write_bytes(main_stub)
    post_path.write_bytes(post_stub)
    (args.output_dir / "herz_lu4_direct_stub.asm").write_text(
        disassemble(main_stub[:0x24F], args.cave + 0x80) + "\n",
        encoding="utf-8",
    )
    (args.output_dir / "herz_lu4_post_send_stub.asm").write_text(
        disassemble(post_stub[:0x57], args.cave + 0xE00) + "\n",
        encoding="utf-8",
    )

    result = {
        "source_image": str(args.image),
        "synthetic_inputs": {
            "cave": args.cave,
            "direct_hook": args.direct_hook,
            "post_hook": args.post_hook,
            "send": args.send,
            "send_first_five_hex": send_first_five.hex(" "),
        },
        "direct": {
            "length": len(main_stub),
            "install_offset": 0x80,
            "trampoline_offset_in_stub": trampoline_offset,
            "trampoline_offset_in_cave": 0x80 + trampoline_offset,
            "command_offset_in_cave": 0x300,
            "indirect_jumps": indirect_jumps(main_stub),
            "sha256": __import__("hashlib").sha256(main_stub).hexdigest(),
        },
        "post_send": {
            "length": len(post_stub),
            "install_offset": 0xE00,
            "indirect_jumps": indirect_jumps(post_stub),
            "sha256": __import__("hashlib").sha256(post_stub).hexdigest(),
        },
    }
    rendered = json.dumps(result, indent=2)
    (args.output_dir / "herz_lu4_stub_layout.json").write_text(
        rendered + "\n", encoding="utf-8"
    )
    print(rendered)


if __name__ == "__main__":
    main()
