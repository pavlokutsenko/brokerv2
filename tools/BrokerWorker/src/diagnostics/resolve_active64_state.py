from __future__ import annotations

import argparse
import json
import struct
import sys
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT_ROOT / "client"))

from lu4_memory_client import Lu4MemoryClient  # noqa: E402


SLOT_TABLE_OFFSET = 0x122E40
SLOT_COUNT = 0x32
SLOT_STRIDE = 0x54480
SLOT_VALID_OFFSET = 0x50
SLOT_PID_OFFSET = 0x60
STATE_TABLE_OFFSET = 0x23FC4
STATE_COUNT = 6
STATE_SIZE = 0x108
HERZ_LU4_STATE_INDEX = 1
DIRECT_STATE_TO_SLOT_DELTA = 0x240CC
READ_CHUNK_SIZE = 1024 * 1024


def parse_state(raw: bytes, index: int, module_offset: int) -> dict[str, object]:
    if len(raw) != STATE_SIZE:
        raise RuntimeError(
            f"state #{index} short read: expected {STATE_SIZE}, got {len(raw)}"
        )
    i_value, j_value = struct.unpack_from("<II", raw)
    sbox = raw[8:]
    permutation = len(sbox) == 256 and set(sbox) == set(range(256))
    return {
        "index": index,
        "module_offset": module_offset,
        "i": i_value,
        "j": j_value,
        "i_in_byte_range": i_value <= 0xFF,
        "j_in_byte_range": j_value <= 0xFF,
        "sbox_is_permutation": permutation,
        "valid": permutation and i_value <= 0xFF and j_value <= 0xFF,
        "sbox_prefix_hex": sbox[:16].hex(" "),
    }


def read_module(
    client: Lu4MemoryClient, module_size: int
) -> tuple[bytes, list[dict[str, int]]]:
    unreadable = []

    def read_span(offset: int, size: int) -> bytes:
        try:
            chunk = client.read_active64(offset, size)
            if len(chunk) != size:
                raise RuntimeError(
                    f"active64 short read at 0x{offset:X}: "
                    f"expected {size}, got {len(chunk)}"
                )
            return chunk
        except OSError as error:
            if size <= 0x1000:
                unreadable.append(
                    {
                        "offset": offset,
                        "size": size,
                        "winerror": int(getattr(error, "winerror", 0) or 0),
                    }
                )
                return bytes(size)
            left_size = (size // 2) & ~0xFFF
            if left_size == 0:
                left_size = min(0x1000, size)
            return read_span(offset, left_size) + read_span(
                offset + left_size, size - left_size
            )

    chunks = []
    for offset in range(0, module_size, READ_CHUNK_SIZE):
        size = min(READ_CHUNK_SIZE, module_size - offset)
        chunks.append(read_span(offset, size))
    return b"".join(chunks), unreadable


def find_state_candidates(image: bytes) -> list[int]:
    """Find state starts whose trailing 256 bytes are an exact permutation."""
    if len(image) < STATE_SIZE:
        return []

    square = tuple(value * value for value in range(256))
    sbox_start = 8
    window = image[sbox_start : sbox_start + 256]
    rolling_sum = sum(window)
    rolling_square_sum = sum(square[value] for value in window)
    rolling_xor = 0
    for value in window:
        rolling_xor ^= value

    expected_sum = sum(range(256))
    expected_square_sum = sum(value * value for value in range(256))
    candidates = []
    last_start = len(image) - 256
    while sbox_start <= last_start:
        state_start = sbox_start - 8
        if (
            rolling_sum == expected_sum
            and rolling_square_sum == expected_square_sum
            and rolling_xor == 0
        ):
            i_value, j_value = struct.unpack_from("<II", image, state_start)
            if (
                i_value <= 0xFF
                and j_value <= 0xFF
                and len(set(image[sbox_start : sbox_start + 256])) == 256
            ):
                candidates.append(state_start)

        if sbox_start == last_start:
            break
        outgoing = image[sbox_start]
        incoming = image[sbox_start + 256]
        rolling_sum += incoming - outgoing
        rolling_square_sum += square[incoming] - square[outgoing]
        rolling_xor ^= outgoing ^ incoming
        sbox_start += 1

    return candidates


def contiguous_groups(candidates: list[int]) -> list[list[int]]:
    if not candidates:
        return []
    groups = [[candidates[0]]]
    for candidate in candidates[1:]:
        if candidate - groups[-1][-1] == STATE_SIZE:
            groups[-1].append(candidate)
        else:
            groups.append([candidate])
    return groups


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Read-only resolver for the LU4 RC4 state held by active64.sys"
    )
    parser.add_argument("pid", type=int)
    parser.add_argument("--json", type=Path)
    args = parser.parse_args()

    required_size = (
        SLOT_TABLE_OFFSET
        + (SLOT_COUNT - 1) * SLOT_STRIDE
        + STATE_TABLE_OFFSET
        + STATE_COUNT * STATE_SIZE
    )

    try:
        with Lu4MemoryClient() as client:
            module_base, module_size = client.active64_info()
            if module_size < required_size:
                raise RuntimeError(
                    f"active64 image is too small: 0x{module_size:X}; "
                    f"need at least 0x{required_size:X}"
                )

            matches: list[dict[str, object]] = []
            occupied: list[dict[str, int]] = []
            for slot_index in range(SLOT_COUNT):
                slot_offset = SLOT_TABLE_OFFSET + slot_index * SLOT_STRIDE
                header = client.read_active64(slot_offset, SLOT_PID_OFFSET + 4)
                if len(header) != SLOT_PID_OFFSET + 4:
                    raise RuntimeError(
                        f"slot #{slot_index} header short read: {len(header)}"
                    )
                valid = header[SLOT_VALID_OFFSET]
                process_id = struct.unpack_from("<I", header, SLOT_PID_OFFSET)[0]
                if valid or process_id:
                    occupied.append(
                        {
                            "slot_index": slot_index,
                            "valid": valid,
                            "pid": process_id,
                        }
                    )
                if valid == 0 or process_id != args.pid:
                    continue

                states = []
                for state_index in range(STATE_COUNT):
                    state_offset = (
                        slot_offset
                        + STATE_TABLE_OFFSET
                        + state_index * STATE_SIZE
                    )
                    state = parse_state(
                        client.read_active64(state_offset, STATE_SIZE),
                        state_index,
                        state_offset,
                    )
                    state["selected_by_herz"] = state_index == HERZ_LU4_STATE_INDEX
                    states.append(state)

                matches.append(
                    {
                        "slot_index": slot_index,
                        "slot_module_offset": slot_offset,
                        "slot_kernel_address": module_base + slot_offset,
                        "valid": valid,
                        "pid": process_id,
                        "herz_state_index": HERZ_LU4_STATE_INDEX,
                        "selected_state_module_offset": (
                            slot_offset
                            + STATE_TABLE_OFFSET
                            + HERZ_LU4_STATE_INDEX * STATE_SIZE
                        ),
                        "selected_state_kernel_address": (
                            module_base
                            + slot_offset
                            + STATE_TABLE_OFFSET
                            + HERZ_LU4_STATE_INDEX * STATE_SIZE
                        ),
                        "states": states,
                    }
                )

            scan = None
            if not matches:
                image, unreadable = read_module(client, module_size)
                candidates = find_state_candidates(image)
                groups = contiguous_groups(candidates)
                longest = max((len(group) for group in groups), default=0)
                best_groups = [group for group in groups if len(group) == longest]
                derived = []
                for group in best_groups:
                    selected_state_offset = group[0]
                    slot_offset = selected_state_offset - DIRECT_STATE_TO_SLOT_DELTA
                    if slot_offset < 0 or slot_offset + SLOT_STRIDE > module_size:
                        continue
                    valid = image[slot_offset + SLOT_VALID_OFFSET]
                    process_id = struct.unpack_from(
                        "<I", image, slot_offset + SLOT_PID_OFFSET
                    )[0]
                    states = []
                    for state_index in range(STATE_COUNT):
                        state_offset = (
                            slot_offset
                            + STATE_TABLE_OFFSET
                            + state_index * STATE_SIZE
                        )
                        state = parse_state(
                            image[state_offset : state_offset + STATE_SIZE],
                            state_index,
                            state_offset,
                        )
                        state["selected_by_herz"] = (
                            state_index == HERZ_LU4_STATE_INDEX
                        )
                        states.append(state)
                    derived.append(
                        {
                            "slot_module_offset": slot_offset,
                            "slot_kernel_address": module_base + slot_offset,
                            "valid": valid,
                            "pid_field": process_id,
                            "herz_state_index": HERZ_LU4_STATE_INDEX,
                            "selected_state_module_offset": selected_state_offset,
                            "selected_state_kernel_address": (
                                module_base + selected_state_offset
                            ),
                            "contiguous_candidate_count": len(group),
                            "contiguous_candidates": group,
                            "states": states,
                        }
                    )
                scan = {
                    "unreadable_ranges": unreadable,
                    "candidate_count": len(candidates),
                    "candidate_offsets": candidates,
                    "group_count": len(groups),
                    "longest_contiguous_group": longest,
                    "best_groups": best_groups,
                    "derived_slots": derived,
                }
                if len(derived) == 1:
                    matches = derived
    except OSError as error:
        if getattr(error, "winerror", None) == 1:
            raise SystemExit(
                "The loaded LU4Memory driver does not expose the active64 "
                "read-only IOCTLs. Load build\\candidate\\lu4_memory.sys "
                "before starting LU4, then run this diagnostic again."
            ) from error
        raise

    result = {
        "pid": args.pid,
        "active64_base": module_base,
        "active64_size": module_size,
        "layout": {
            "slot_table_offset": SLOT_TABLE_OFFSET,
            "slot_count": SLOT_COUNT,
            "slot_stride": SLOT_STRIDE,
            "valid_offset": SLOT_VALID_OFFSET,
            "pid_offset": SLOT_PID_OFFSET,
            "state_table_offset": STATE_TABLE_OFFSET,
            "state_count": STATE_COUNT,
            "state_size": STATE_SIZE,
            "herz_lu4_state_index": HERZ_LU4_STATE_INDEX,
        },
        "occupied_slots": occupied,
        "direct_state_scan": scan,
        "matching_slots": matches,
        "matching_slot_unique": len(matches) == 1,
        "selected_state_valid": (
            len(matches) == 1
            and bool(matches[0]["states"][HERZ_LU4_STATE_INDEX]["valid"])
        ),
    }
    rendered = json.dumps(result, indent=2)
    print(rendered)
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(rendered + "\n", encoding="utf-8")

    return 0 if result["matching_slot_unique"] and result["selected_state_valid"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
