"""Validated per-image Unreal discovery for an owned market-walk process."""
from __future__ import annotations

import json
import os
from pathlib import Path
import struct

from inspect_world import Survey, pointer, read_object, validate_world_slot
from scan_lu4_actors import scan_world
from discover_unreal_globals import scan_globals, validate_fname_pool, validate_gobjects


SPECS = {
    "move": ("Move to Location by Keyboard", "ControllerPC_C", "Function", 32),
    "stop": ("StopIfMove", "ControllerPC_C", "Function", 8),
    "camera": ("Get Move Camera Direction", "ControllerPC_C", "Function", 24),
    "shop_buy": ("PlayerShopBuyItemsList", "LU4GameHUD", "Function", 24),
    "shop_new_buy": ("PlayerShopNewBuyItemsList", "LU4GameHUD", "Function", 40),
    "shop_new_sell": ("PlayerShopNewSellItemsList", "LU4GameHUD", "Function", 48),
    "shop_sell": ("PlayerShopSellItemsList", "LU4GameHUD", "Function", 32),
    "trace_objects": ("CapsuleTraceSingleForObjects", "KismetSystemLibrary", "Function", 401),
    "trace_profile": ("CapsuleTraceSingleByProfile", "KismetSystemLibrary", "Function", 393),
    "trader_class": ("CharacterPlayer_C", "/Game/LU4_BaseClasses/CharacterPlayer", "BlueprintGeneratedClass", 0),
    "target_ui_self": ("MyTargetSelected", "LU4GameHUD", "Function", 8),
    "target_ui_other": ("TargetSelected", "LU4GameHUD", "Function", 12),
}


def _cache_path(pe):
    stamp = f"{pe['timestamp']:08x}-{pe['image_size']:08x}"
    root = Path(os.environ["LOCALAPPDATA"]) / "PriceCheckCollector" / "research" / "walk-runtime"
    return root / f"{stamp}.json"


def _validated_tables(mem, base, rvas):
    try:
        objects = base + int(rvas["gobjects"])
        names = base + int(rvas["fnames"])
        if (validate_gobjects(mem, objects, mem.read(objects, 0x30), 0) is None
                or validate_fname_pool(mem, names, mem.read(names, 0x30), 0) is None):
            return False
        survey = Survey(mem, base, int(rvas["fnames"]))
        return survey.name(read_object(mem, objects, 0)) == "/Script/CoreUObject"
    except (KeyError, ValueError, OSError, RuntimeError):
        return False


def _validated_function(mem, base, rvas, survey, key, index):
    name, outer, cls, size = SPECS[key]
    obj = read_object(mem, base + rvas["gobjects"], int(index))
    if not pointer(obj) or mem.i32(obj + 0x0C, -1) != int(index):
        return 0
    if (survey.name(obj) != name or survey.name(mem.u64(obj + 0x10)) != cls
            or survey.name(mem.u64(obj + 0x20)) != outer):
        return 0
    if cls == "Function" and mem.unpack("<H", obj + 0xB6, -1) != size:
        return 0
    return obj


def _scan_functions(mem, base, rvas, survey):
    slot = base + rvas["gobjects"]
    count = mem.i32(slot + 0x14, -1)
    chunks = mem.u64(slot)
    if not pointer(chunks) or not 10000 <= count <= 4_000_000:
        raise RuntimeError("invalid GObjects count during function discovery")
    by_name = {}
    for key, spec in SPECS.items():
        by_name.setdefault(spec[0], []).append(key)
    found = {}
    for chunk_index in range((count + 65535) // 65536):
        chunk = mem.u64(chunks + chunk_index * 8)
        if not pointer(chunk):
            raise RuntimeError("invalid GObjects chunk during function discovery")
        amount_in_chunk = min(65536, count - chunk_index * 65536)
        for start in range(0, amount_in_chunk, 32768):
            amount = min(32768, amount_in_chunk - start)
            data = mem.read(chunk + start * 0x18, amount * 0x18)
            for within in range(amount):
                obj = struct.unpack_from("<Q", data, within * 0x18)[0]
                if not pointer(obj):
                    continue
                name = survey.name(obj)
                if name not in by_name:
                    continue
                index = chunk_index * 65536 + start + within
                for key in by_name[name]:
                    if _validated_function(mem, base, rvas, survey, key, index):
                        if key in found:
                            raise RuntimeError(f"ambiguous reflected target: {key}")
                        found[key] = index
    if set(found) != set(SPECS):
        raise RuntimeError(f"missing reflected targets: {sorted(set(SPECS) - set(found))}")
    return found


def resolve_runtime(mem, base, pe):
    """Use live-validated cache, then bounded read-only discovery on a new build."""
    cache = _cache_path(pe)
    try:
        saved = json.loads(cache.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        saved = None
    if saved:
        rvas = saved.get("rvas", {})
        functions = saved.get("functions", {})
        if _validated_tables(mem, base, rvas):
            survey = Survey(mem, base, rvas["fnames"])
            world = validate_world_slot(mem, base + rvas["gworld"], base, pe["image_size"])
            if (world and survey.name(mem.u64(world["world"] + 0x10)) == "World"
                    and set(functions) == set(SPECS)
                    and all(_validated_function(mem, base, rvas, survey, key, index)
                            for key, index in functions.items())):
                return rvas, functions, world, survey

    objects, names, _ = scan_globals(mem, base, pe)
    if len(objects) != 1 or len(names) != 1:
        raise RuntimeError("Unreal table discovery is not unique")
    rvas = {"gobjects": objects[0]["rva"], "fnames": names[0]["rva"]}
    if not _validated_tables(mem, base, rvas):
        raise RuntimeError("discovered Unreal tables failed structural validation")
    world = scan_world(mem, base, pe)
    if not world:
        raise RuntimeError("world/controller discovery failed")
    rvas["gworld"] = world["gworld_rva"]
    survey = Survey(mem, base, rvas["fnames"])
    if survey.name(mem.u64(world["world"] + 0x10)) != "World":
        raise RuntimeError("discovered world class failed validation")
    functions = _scan_functions(mem, base, rvas, survey)
    cache.parent.mkdir(parents=True, exist_ok=True)
    temporary = cache.with_suffix(".tmp")
    temporary.write_text(json.dumps({"pe_timestamp": pe["timestamp"],
                                     "image_size": pe["image_size"], "rvas": rvas,
                                     "functions": functions}, indent=2) + "\n", encoding="utf-8")
    temporary.replace(cache)
    mem.pages.clear()
    return rvas, functions, world, survey
