"""Read-only profile/PID/coordinate verification through the running LU4Memory.

Does not load a driver, install a hook, launch clients, or print credentials.
The pointer chain and version guard match LocalPlayerPositionReader.cs.
"""

import argparse
import ctypes as c
import json
import math
import os
import struct
from pathlib import Path


class Device:
    def __init__(self):
        self.kernel = c.WinDLL("kernel32", use_last_error=True)
        self.kernel.CreateFileW.argtypes = (c.c_wchar_p, c.c_uint32, c.c_uint32,
                                           c.c_void_p, c.c_uint32, c.c_uint32, c.c_void_p)
        self.kernel.CreateFileW.restype = c.c_void_p
        self.kernel.CloseHandle.argtypes = (c.c_void_p,)
        self.kernel.DeviceIoControl.argtypes = (c.c_void_p, c.c_uint32, c.c_void_p,
                                               c.c_uint32, c.c_void_p, c.c_uint32,
                                               c.POINTER(c.c_uint32), c.c_void_p)
        self.kernel.DeviceIoControl.restype = c.c_int
        self.handle = self.kernel.CreateFileW(r"\\.\LU4Memory", 0x80000000, 0, None, 3, 0, None)
        if self.handle == c.c_void_p(-1).value:
            raise OSError(c.get_last_error(), "Open running LU4Memory")

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.kernel.CloseHandle(self.handle)

    def ioctl(self, function, data, size):
        source = c.create_string_buffer(data)
        result = c.create_string_buffer(size)
        count = c.c_uint32()
        code = (0x8337 << 16) | (1 << 14) | (function << 2)
        if not self.kernel.DeviceIoControl(self.handle, code, source, len(data),
                                           result, size, c.byref(count), None):
            raise OSError(c.get_last_error(), "Read-only driver request failed")
        if count.value != size:
            raise RuntimeError("Short read-only driver response")
        return result.raw

    def base(self, pid):
        return struct.unpack_from("<Q", self.ioctl(0x800, struct.pack("<IIQ", 3, pid, 0), 16), 8)[0]

    def read(self, pid, address, size):
        if not 0x10000 <= address < 0x800000000000 or not 0 < size <= 4096:
            raise ValueError("Invalid bounded user-mode read")
        response = self.ioctl(0x801, struct.pack("<IIQII", 3, pid, address, size, 0), 24 + size)
        if struct.unpack_from("<I", response, 20)[0] != size:
            raise RuntimeError("Short user-mode copy")
        return response[24:]

    def pointer(self, pid, address):
        return struct.unpack("<Q", self.read(pid, address, 8))[0]


def inspect(device, profile):
    pid = profile.get("LastProcessId")
    if not isinstance(pid, int) or pid <= 0:
        raise ValueError("Profile has no bound PID")
    base = device.base(pid)
    header = device.read(pid, base, 4096)
    pe = struct.unpack_from("<I", header, 0x3C)[0]
    if pe > 4096 - 84 or header[pe:pe + 4] != b"PE\0\0":
        raise ValueError("Invalid client header")
    if (struct.unpack_from("<I", header, pe + 8)[0],
            struct.unpack_from("<I", header, pe + 80)[0]) != (0x956E0D97, 0xDCEB000):
        raise ValueError("Unsupported client version")
    value = device.pointer(pid, base + 0x81F6A80)
    for offset in (0x1D8, 0x38, 0, 0x30, 0x2D0, 0x1A0):
        value = device.pointer(pid, value + offset)
    xyz = struct.unpack("<ddd", device.read(pid, value + 0x1F0, 24))
    valid = all(math.isfinite(x) and abs(x) < 1e9 for x in xyz) and any(x != 0 for x in xyz[:2])
    return {"profile": profile["Name"], "pid": pid,
            "configured_world": profile.get("LoginServerName"),
            "player_position_valid": valid}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--profile", action="append", required=True)
    args = parser.parse_args()
    path = Path(os.environ["LOCALAPPDATA"]) / "PriceCheckCollector/profiles.json"
    profiles = json.loads(path.read_text(encoding="utf-8-sig"))
    selected = [next(p for p in profiles if p["Name"] == name) for name in args.profile]
    pids = [p.get("LastProcessId") for p in selected]
    if len(set(pids)) != len(pids):
        raise ValueError("Two requested profiles share a PID")
    with Device() as device:
        result = []
        for profile in selected:
            try:
                result.append(inspect(device, profile))
            except (OSError, ValueError, RuntimeError) as error:
                result.append({"profile": profile["Name"], "pid": profile.get("LastProcessId"),
                               "player_position_valid": False, "error": str(error)})
    print(json.dumps(result))
    if not all(item["player_position_valid"] for item in result):
        raise SystemExit(1)
