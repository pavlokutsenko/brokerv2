from __future__ import annotations

import argparse
import ctypes
import math
import os
import struct
from ctypes import wintypes


DEVICE_PATH = r"\\.\LU4Memory"
VERSION = 3
FILE_DEVICE = 0x8337
METHOD_BUFFERED = 0
FILE_READ_DATA = 1
FILE_WRITE_DATA = 2
OPEN_EXISTING = 3
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value
HEADER_SIZE = 24
TARGET_PACKET_FORMAT = "<BiiiiB"
TARGET_PACKET_SIZE = struct.calcsize(TARGET_PACKET_FORMAT)
MOVE_TO_LOCATION_PACKET_FORMAT = "<BBiiiiii"
MOVE_TO_LOCATION_PACKET_SIZE = struct.calcsize(MOVE_TO_LOCATION_PACKET_FORMAT)


def ctl_code(function: int, access: int) -> int:
    return (FILE_DEVICE << 16) | (access << 14) | (function << 2) | METHOD_BUFFERED


IOCTL_GET_BASE = ctl_code(0x800, FILE_READ_DATA)
IOCTL_READ = ctl_code(0x801, FILE_READ_DATA)
IOCTL_WRITE = ctl_code(0x802, FILE_WRITE_DATA)
IOCTL_SUBMIT_TARGET = ctl_code(0x803, FILE_WRITE_DATA)
IOCTL_CLAIM_TARGET = ctl_code(0x804, FILE_READ_DATA)
IOCTL_COMPLETE_TARGET = ctl_code(0x805, FILE_WRITE_DATA)
IOCTL_QUERY_TARGET = ctl_code(0x806, FILE_READ_DATA)
IOCTL_QUERY_ACTIVE64 = ctl_code(0x807, FILE_READ_DATA)
IOCTL_READ_ACTIVE64 = ctl_code(0x808, FILE_READ_DATA)
IOCTL_ENCRYPT_PACKET = ctl_code(0x809, FILE_WRITE_DATA)
IOCTL_ALLOCATE_PROCESS_MEMORY = ctl_code(0x80A, FILE_WRITE_DATA)
IOCTL_PROTECT_PROCESS_MEMORY = ctl_code(0x80B, FILE_WRITE_DATA)
IOCTL_FREE_PROCESS_MEMORY = ctl_code(0x80C, FILE_WRITE_DATA)
TARGET_COMMAND_FORMAT = "<IIQiiiiIIII"
TARGET_COMMAND_SIZE = struct.calcsize(TARGET_COMMAND_FORMAT)
PACKET_CRYPTO_FORMAT = "<IIQIIQQiI"
PACKET_CRYPTO_SIZE = struct.calcsize(PACKET_CRYPTO_FORMAT)
VIRTUAL_MEMORY_FORMAT = "<IIQQIIQ"
VIRTUAL_MEMORY_SIZE = struct.calcsize(VIRTUAL_MEMORY_FORMAT)
PAGE_READONLY = 0x02
PAGE_READWRITE = 0x04
PAGE_EXECUTE_READ = 0x20
PAGE_EXECUTE_READWRITE = 0x40

kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
kernel32.CreateFileW.argtypes = [
    wintypes.LPCWSTR,
    wintypes.DWORD,
    wintypes.DWORD,
    ctypes.c_void_p,
    wintypes.DWORD,
    wintypes.DWORD,
    wintypes.HANDLE,
]
kernel32.CreateFileW.restype = wintypes.HANDLE
kernel32.DeviceIoControl.argtypes = [
    wintypes.HANDLE,
    wintypes.DWORD,
    ctypes.c_void_p,
    wintypes.DWORD,
    ctypes.c_void_p,
    wintypes.DWORD,
    ctypes.POINTER(wintypes.DWORD),
    ctypes.c_void_p,
]
kernel32.DeviceIoControl.restype = wintypes.BOOL
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.CloseHandle.restype = wintypes.BOOL


class Lu4MemoryClient:
    def __init__(self) -> None:
        self.handle = kernel32.CreateFileW(
            DEVICE_PATH,
            0x80000000 | 0x40000000,
            0,
            None,
            OPEN_EXISTING,
            0,
            None,
        )
        if self.handle == INVALID_HANDLE_VALUE:
            raise ctypes.WinError(ctypes.get_last_error())

    def close(self) -> None:
        if self.handle not in (None, INVALID_HANDLE_VALUE):
            kernel32.CloseHandle(self.handle)
            self.handle = None

    def __enter__(self) -> "Lu4MemoryClient":
        return self

    def __exit__(self, *_: object) -> None:
        self.close()

    def _ioctl(self, code: int, request: bytes, output_size: int) -> bytes:
        input_buffer = ctypes.create_string_buffer(request)
        output_buffer = ctypes.create_string_buffer(output_size)
        returned = wintypes.DWORD()
        ok = kernel32.DeviceIoControl(
            self.handle,
            code,
            input_buffer,
            len(request),
            output_buffer,
            output_size,
            ctypes.byref(returned),
            None,
        )
        if not ok:
            raise ctypes.WinError(ctypes.get_last_error())
        return output_buffer.raw[: returned.value]

    def process_base(self, pid: int) -> int:
        response = self._ioctl(IOCTL_GET_BASE, struct.pack("<IIQ", VERSION, pid, 0), 16)
        if len(response) != 16:
            raise RuntimeError(f"unexpected base response length: {len(response)}")
        version, response_pid, base = struct.unpack("<IIQ", response)
        if version != VERSION or response_pid != pid:
            raise RuntimeError("base response does not match request")
        return base

    def read(self, pid: int, address: int, size: int) -> bytes:
        request = struct.pack("<IIQII", VERSION, pid, address, size, 0)
        response = self._ioctl(IOCTL_READ, request, HEADER_SIZE + size)
        if len(response) < HEADER_SIZE:
            raise RuntimeError(f"short read response: {len(response)}")
        version, response_pid, response_address, requested, copied = struct.unpack_from("<IIQII", response)
        if (version, response_pid, response_address, requested) != (VERSION, pid, address, size):
            raise RuntimeError("read response does not match request")
        return response[HEADER_SIZE:HEADER_SIZE + copied]

    def write(self, pid: int, address: int, data: bytes) -> int:
        request = struct.pack("<IIQII", VERSION, pid, address, len(data), 0) + data
        response = self._ioctl(IOCTL_WRITE, request, HEADER_SIZE)
        if len(response) < HEADER_SIZE:
            raise RuntimeError(f"short write response: {len(response)}")
        return struct.unpack_from("<I", response, 20)[0]

    def submit_target(
        self,
        pid: int,
        sequence: int,
        object_id: int,
        x: float,
        y: float,
        z: float,
    ) -> tuple[int, ...]:
        request = struct.pack(
            TARGET_COMMAND_FORMAT,
            VERSION,
            pid,
            sequence,
            object_id,
            int(x),
            int(y),
            int(z),
            0,
            0,
            0,
            0,
        )
        response = self._ioctl(IOCTL_SUBMIT_TARGET, request, TARGET_COMMAND_SIZE)
        if len(response) != TARGET_COMMAND_SIZE:
            raise RuntimeError(f"unexpected target response length: {len(response)}")
        return struct.unpack(TARGET_COMMAND_FORMAT, response)

    def query_target(self, sequence: int = 0) -> tuple[int, ...]:
        request = struct.pack(
            TARGET_COMMAND_FORMAT,
            VERSION,
            0,
            sequence,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
        )
        response = self._ioctl(IOCTL_QUERY_TARGET, request, TARGET_COMMAND_SIZE)
        if len(response) != TARGET_COMMAND_SIZE:
            raise RuntimeError(f"unexpected target response length: {len(response)}")
        return struct.unpack(TARGET_COMMAND_FORMAT, response)

    def active64_info(self) -> tuple[int, int]:
        request = struct.pack("<IIQII", VERSION, 0, 0, 0, 0)
        response = self._ioctl(IOCTL_QUERY_ACTIVE64, request, 24)
        if len(response) != 24:
            raise RuntimeError(f"unexpected active64 response length: {len(response)}")
        version, reserved, base, size, reserved2 = struct.unpack("<IIQII", response)
        if version != VERSION or reserved != 0 or reserved2 != 0:
            raise RuntimeError("invalid active64 module response")
        return base, size

    def read_active64(self, offset: int, size: int) -> bytes:
        request = struct.pack("<IIQII", VERSION, 0, offset, size, 0)
        response = self._ioctl(IOCTL_READ_ACTIVE64, request, HEADER_SIZE + size)
        if len(response) < HEADER_SIZE:
            raise RuntimeError(f"short active64 response: {len(response)}")
        version, reserved, response_offset, requested, copied = struct.unpack_from(
            "<IIQII", response
        )
        if (version, reserved, response_offset, requested) != (
            VERSION,
            0,
            offset,
            size,
        ):
            raise RuntimeError("active64 read response does not match request")
        return response[HEADER_SIZE : HEADER_SIZE + copied]

    def encrypt_packet(
        self,
        pid: int,
        buffer_address: int,
        size: int,
        rolling_key_address: int,
        state_index: int = 0,
        state_offset: int = 0,
    ) -> int:
        """Apply Herz packet crypto; state index 1 enables the full RC4 path."""
        request = struct.pack(
            PACKET_CRYPTO_FORMAT,
            VERSION,
            pid,
            buffer_address,
            size,
            state_index,
            rolling_key_address,
            state_offset,
            0,
            0,
        )
        response = self._ioctl(
            IOCTL_ENCRYPT_PACKET,
            request,
            PACKET_CRYPTO_SIZE,
        )
        if len(response) != PACKET_CRYPTO_SIZE:
            raise RuntimeError(
                f"unexpected packet-crypto response length: {len(response)}"
            )
        fields = struct.unpack(PACKET_CRYPTO_FORMAT, response)
        if fields[:8] != (
            VERSION,
            pid,
            buffer_address,
            size,
            state_index,
            rolling_key_address,
            state_offset,
            0,
        ):
            raise RuntimeError("packet-crypto response does not match request")
        return fields[8]

    def allocate_process_memory(
        self,
        pid: int,
        size: int,
        protection: int = PAGE_EXECUTE_READWRITE,
    ) -> tuple[int, int]:
        request = struct.pack(
            VIRTUAL_MEMORY_FORMAT,
            VERSION,
            pid,
            0,
            size,
            protection,
            0,
            0,
        )
        response = self._ioctl(
            IOCTL_ALLOCATE_PROCESS_MEMORY,
            request,
            VIRTUAL_MEMORY_SIZE,
        )
        fields = self._unpack_virtual_memory_response(response, pid)
        return fields[2], fields[3]

    def protect_process_memory(
        self,
        pid: int,
        address: int,
        size: int,
        protection: int,
    ) -> tuple[int, int, int]:
        request = struct.pack(
            VIRTUAL_MEMORY_FORMAT,
            VERSION,
            pid,
            address,
            size,
            protection,
            0,
            0,
        )
        response = self._ioctl(
            IOCTL_PROTECT_PROCESS_MEMORY,
            request,
            VIRTUAL_MEMORY_SIZE,
        )
        fields = self._unpack_virtual_memory_response(response, pid)
        return fields[2], fields[3], fields[5]

    def free_process_memory(self, pid: int, address: int) -> None:
        request = struct.pack(
            VIRTUAL_MEMORY_FORMAT,
            VERSION,
            pid,
            address,
            0,
            0,
            0,
            0,
        )
        response = self._ioctl(
            IOCTL_FREE_PROCESS_MEMORY,
            request,
            VIRTUAL_MEMORY_SIZE,
        )
        fields = self._unpack_virtual_memory_response(response, pid)
        if fields[2] != 0 or fields[3] != 0:
            raise RuntimeError("free response did not clear address and size")

    @staticmethod
    def _unpack_virtual_memory_response(
        response: bytes, pid: int
    ) -> tuple[int, ...]:
        if len(response) != VIRTUAL_MEMORY_SIZE:
            raise RuntimeError(
                f"unexpected virtual-memory response length: {len(response)}"
            )
        fields = struct.unpack(VIRTUAL_MEMORY_FORMAT, response)
        if fields[0] != VERSION or fields[1] != pid or fields[6] != 0:
            raise RuntimeError("invalid virtual-memory response")
        return fields


def build_target_packet(
    object_id: int,
    origin_x: float,
    origin_y: float,
    origin_z: float,
    force_attack: bool = False,
) -> bytes:
    """Build LU4 RequestAction using the local player's origin coordinates."""
    packet = struct.pack(
        TARGET_PACKET_FORMAT,
        0x0F,
        object_id,
        int(origin_x),
        int(origin_y),
        int(origin_z),
        int(force_attack),
    )
    if len(packet) != 18:
        raise AssertionError(f"unexpected target packet size: {len(packet)}")
    return packet


def build_move_to_location_packet(
    source_x: float,
    source_y: float,
    source_z: float,
    destination_x: float,
    destination_y: float,
    destination_z: float,
) -> bytes:
    """Build the LU4/Herz 26-byte MoveToLocation packet."""
    coordinates = (
        source_x,
        source_y,
        source_z,
        destination_x,
        destination_y,
        destination_z,
    )
    if any(not math.isfinite(value) for value in coordinates):
        raise ValueError("MoveToLocation coordinates must be finite")
    integers = tuple(int(value) for value in coordinates)
    if any(value < -0x80000000 or value > 0x7FFFFFFF for value in integers):
        raise ValueError("MoveToLocation coordinate exceeds int32")
    packet = struct.pack(
        MOVE_TO_LOCATION_PACKET_FORMAT,
        0x0C,
        0x01,
        *integers,
    )
    if len(packet) != MOVE_TO_LOCATION_PACKET_SIZE or len(packet) != 26:
        raise AssertionError(f"unexpected MoveToLocation packet size: {len(packet)}")
    return packet


def main() -> int:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)
    probe = subparsers.add_parser("probe")
    probe.add_argument("pid", type=int)
    subparsers.add_parser("probe-self")
    target_packet = subparsers.add_parser(
        "target-packet",
        help="build the recovered 18-byte target packet without sending it",
    )
    target_packet.add_argument("object_id", type=int)
    target_packet.add_argument("x", type=float)
    target_packet.add_argument("y", type=float)
    target_packet.add_argument("z", type=float)
    target_packet.add_argument("--force-attack", action="store_true")
    args = parser.parse_args()

    if args.command == "target-packet":
        packet = build_target_packet(
            args.object_id,
            args.x,
            args.y,
            args.z,
            args.force_attack,
        )
        print(f"size={len(packet)} bytes={packet.hex(' ')}")
        return 0

    with Lu4MemoryClient() as client:
        if args.command in ("probe", "probe-self"):
            pid = args.pid if args.command == "probe" else os.getpid()
            base = client.process_base(pid)
            signature = client.read(pid, base, 2)
            print(f"pid={pid} base=0x{base:X} signature={signature.hex(' ')}")
            return 0 if signature == b"MZ" else 2
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
