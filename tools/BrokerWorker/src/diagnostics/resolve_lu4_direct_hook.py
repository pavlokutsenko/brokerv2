from __future__ import annotations

import argparse
import json
import struct
import sys
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT_ROOT / "client"))

from lu4_memory_client import Lu4MemoryClient  # noqa: E402


HOOK_SIGNATURES = {
    "direct": bytes.fromhex(
        "40 55 56 57 41 54 41 55 41 56 41 57 48 81 EC 10 01 00 00 48 8D"
    ),
    "post_send": bytes.fromhex(
        "4C 8B 74 24 58 48 8B 6C 24 50 48 85 DB 74 1A"
    ),
}
IMAGE_SCN_MEM_EXECUTE = 0x20000000
MAX_CHUNK = 1024 * 1024


def read_exact(client: Lu4MemoryClient, pid: int, address: int, size: int) -> bytes:
    data = client.read(pid, address, size)
    if len(data) != size:
        raise RuntimeError(
            f"short read at 0x{address:X}: received {len(data)} of {size} bytes"
        )
    return data


def executable_sections(headers: bytes) -> list[dict[str, int | str]]:
    pe_offset = struct.unpack_from("<I", headers, 0x3C)[0]
    if headers[pe_offset : pe_offset + 4] != b"PE\0\0":
        raise RuntimeError("target image has no PE signature")

    section_count = struct.unpack_from("<H", headers, pe_offset + 6)[0]
    optional_size = struct.unpack_from("<H", headers, pe_offset + 20)[0]
    section_table = pe_offset + 24 + optional_size
    sections: list[dict[str, int | str]] = []
    for index in range(section_count):
        offset = section_table + index * 40
        name = headers[offset : offset + 8].split(b"\0", 1)[0].decode(
            "ascii", "replace"
        )
        virtual_size, virtual_address, raw_size = struct.unpack_from(
            "<III", headers, offset + 8
        )
        characteristics = struct.unpack_from("<I", headers, offset + 36)[0]
        if characteristics & IMAGE_SCN_MEM_EXECUTE:
            sections.append(
                {
                    "name": name,
                    "rva": virtual_address,
                    "size": max(virtual_size, raw_size),
                }
            )
    return sections


def scan_section(
    client: Lu4MemoryClient,
    pid: int,
    base: int,
    section_rva: int,
    section_size: int,
) -> list[tuple[str, int]]:
    matches: list[tuple[str, int]] = []
    overlap = max(map(len, HOOK_SIGNATURES.values())) - 1
    carry = b""
    offset = 0
    while offset < section_size:
        requested = min(MAX_CHUNK, section_size - offset)
        chunk = read_exact(client, pid, base + section_rva + offset, requested)
        data = carry + chunk
        for signature_name, signature in HOOK_SIGNATURES.items():
            start = 0
            while True:
                found = data.find(signature, start)
                if found < 0:
                    break
                matches.append(
                    (
                        signature_name,
                        base + section_rva + offset - len(carry) + found,
                    )
                )
                start = found + 1
        carry = data[-overlap:]
        offset += requested
    return matches


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Read-only resolver for the LU4 DirectHook v2 patch point."
    )
    parser.add_argument("pid", type=int)
    parser.add_argument("--json", type=Path)
    args = parser.parse_args()

    with Lu4MemoryClient() as client:
        base = client.process_base(args.pid)
        headers = read_exact(client, args.pid, base, 0x1000)
        matches: list[dict[str, int | str]] = []
        sections = executable_sections(headers)
        for section in sections:
            for signature_name, address in scan_section(
                client,
                args.pid,
                base,
                int(section["rva"]),
                int(section["size"]),
            ):
                prologue = read_exact(client, args.pid, address, 64)
                matches.append(
                    {
                        "kind": signature_name,
                        "section": str(section["name"]),
                        "address": address,
                        "rva": address - base,
                        "prologue_hex": prologue.hex(" "),
                    }
                )

    result = {
        "pid": args.pid,
        "image_base": base,
        "signatures_hex": {
            name: signature.hex(" ")
            for name, signature in HOOK_SIGNATURES.items()
        },
        "executable_sections": sections,
        "matches": matches,
        "all_signatures_unique": all(
            sum(match["kind"] == name for match in matches) == 1
            for name in HOOK_SIGNATURES
        ),
    }
    rendered = json.dumps(result, indent=2)
    print(rendered)
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(rendered + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
