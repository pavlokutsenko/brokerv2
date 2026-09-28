#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include "login_runtime.h"

namespace {
LoginRuntime cached{};
std::uintptr_t cached_base = 0;
int failure = -40;

bool pointer(std::uintptr_t value) {
    return value >= 0x1000000000ULL && value <= 0x00007FFFFFFFFFFFULL && (value & 7) == 0;
}

bool name_at(std::uintptr_t pool, std::uint32_t id, const char* expected) {
    const auto block_index = id >> 16;
    if (block_index > 4096) return false;
    auto block = *reinterpret_cast<std::uintptr_t*>(pool + 0x10 + block_index * 8);
    if (!pointer(block)) return false;
    auto entry = block + static_cast<std::uintptr_t>(id & 0xFFFF) * 2;
    auto header = *reinterpret_cast<std::uint16_t*>(entry);
    const auto length = header >> 6;
    return (header & 1) == 0 && length == std::strlen(expected) &&
        std::memcmp(reinterpret_cast<void*>(entry + 2), expected, length) == 0;
}

bool name_of(std::uintptr_t pool, std::uintptr_t object, const char* expected) {
    return pointer(object) && name_at(pool, *reinterpret_cast<std::uint32_t*>(object + 0x18), expected);
}

bool valid_names(std::uintptr_t slot) {
    const auto block_count = *reinterpret_cast<std::int32_t*>(slot + 8);
    const auto cursor = *reinterpret_cast<std::int32_t*>(slot + 12);
    const auto first = *reinterpret_cast<std::uintptr_t*>(slot + 0x10);
    if (block_count < 1 || block_count > 4096 || cursor < 2 || cursor > 0x20000 ||
        !pointer(first) || !name_at(slot, 0, "None")) return false;
    // Two independent reflected core names distinguish the real pool from a
    // coincidental pointer to a string containing "None".
    int known = 0;
    auto entry = first;
    for (int index = 0; index < 64; ++index) {
        const auto header = *reinterpret_cast<std::uint16_t*>(entry);
        const auto length = header >> 6;
        if (!length || length > 96) break;
        if (!(header & 1)) {
            const auto text = reinterpret_cast<const char*>(entry + 2);
            if ((length == 12 && std::memcmp(text, "ByteProperty", 12) == 0) ||
                (length == 11 && std::memcmp(text, "IntProperty", 11) == 0) ||
                (length == 12 && std::memcmp(text, "BoolProperty", 12) == 0) ||
                (length == 13 && std::memcmp(text, "FloatProperty", 13) == 0)) ++known;
        }
        const auto bytes = 2 + length * ((header & 1) ? 2 : 1);
        entry += bytes + (bytes & 1);
    }
    return known >= 2;
}

bool valid_objects(std::uintptr_t slot) {
    const auto chunks = *reinterpret_cast<std::uintptr_t*>(slot);
    const auto max_elements = *reinterpret_cast<std::int32_t*>(slot + 0x10);
    const auto count = *reinterpret_cast<std::int32_t*>(slot + 0x14);
    const auto max_chunks = *reinterpret_cast<std::int32_t*>(slot + 0x18);
    const auto chunk_count = *reinterpret_cast<std::int32_t*>(slot + 0x1C);
    if (!pointer(chunks) || max_chunks < 1 || max_chunks > 0x5FF ||
        chunk_count < 1 || chunk_count > max_chunks || chunk_count > 64 || count < 10000 ||
        count > chunk_count * 0x10000 || max_elements != max_chunks * 0x10000) return false;
    const auto first = *reinterpret_cast<std::uintptr_t*>(chunks);
    if (!pointer(first)) return false;
    for (int index = 0; index < 3; ++index) {
        const auto object = *reinterpret_cast<std::uintptr_t*>(first + index * 0x18);
        if (!pointer(object) || *reinterpret_cast<std::int32_t*>(object + 0x0C) != index) return false;
    }
    return true;
}

bool discover_tables(std::uintptr_t base, std::uintptr_t end,
                     std::uintptr_t& objects, std::uintptr_t& names) {
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    if (nt->FileHeader.NumberOfSections == 0 || nt->FileHeader.NumberOfSections > 64 ||
        reinterpret_cast<std::uintptr_t>(IMAGE_FIRST_SECTION(nt) + nt->FileHeader.NumberOfSections) >
            base + 0x1000) return false;
    const auto* sections = IMAGE_FIRST_SECTION(nt);
    int object_matches = 0, name_matches = 0;
    for (unsigned section_index = 0; section_index < nt->FileHeader.NumberOfSections; ++section_index) {
        const auto& section = sections[section_index];
        if (!(section.Characteristics & IMAGE_SCN_MEM_READ) ||
            !(section.Characteristics & IMAGE_SCN_MEM_WRITE)) continue;
        const auto start = base + section.VirtualAddress;
        const auto size = static_cast<std::uintptr_t>(section.Misc.VirtualSize);
        if (start < base || start >= end || size > end - start) return false;
        for (auto slot = start; slot + 0x30 <= start + size; slot += 8) {
            // The cheap scalar checks precede any pointer dereference. Invalid
            // candidates are contained here; none can take down the client.
            __try {
                const auto candidate = *reinterpret_cast<std::uintptr_t*>(slot);
                const auto count = *reinterpret_cast<std::int32_t*>(slot + 0x14);
                const auto chunks = *reinterpret_cast<std::int32_t*>(slot + 0x1C);
                if (pointer(candidate) && count > 10000 && count < 4000000 &&
                    chunks > 0 && chunks <= 64 && valid_objects(slot)) {
                    objects = slot;
                    ++object_matches;
                }
                const auto block_count = *reinterpret_cast<std::int32_t*>(slot + 8);
                const auto cursor = *reinterpret_cast<std::int32_t*>(slot + 12);
                const auto block = *reinterpret_cast<std::uintptr_t*>(slot + 0x10);
                if (block_count >= 1 && block_count <= 4096 && cursor >= 2 &&
                    cursor <= 0x20000 && pointer(block) && valid_names(slot)) {
                    names = slot;
                    ++name_matches;
                }
            } __except (EXCEPTION_EXECUTE_HANDLER) { }
        }
    }
    return object_matches == 1 && name_matches == 1;
}

bool unique(void*& target, void* found) {
    if (target && target != found) return false;
    target = found;
    return true;
}

bool discover_targets(std::uintptr_t objects, std::uintptr_t names, LoginRuntime& result) {
    result.objects_slot = objects;
    result.chunks = *reinterpret_cast<std::uintptr_t*>(objects);
    result.count = *reinterpret_cast<std::int32_t*>(objects + 0x14);
    const auto chunk_count = *reinterpret_cast<std::int32_t*>(objects + 0x1C);
    if (result.count < 10000 || result.count > chunk_count * 0x10000 || chunk_count > 64) return false;
    // Static reflected functions and class defaults are registered before
    // dynamic actors. No game-version index is used for the lookup.
    const int static_limit = result.count < 100000 ? result.count : 100000;
    for (int index = 0; index < static_limit; ++index) {
        const auto chunk = *reinterpret_cast<std::uintptr_t*>(result.chunks + (index / 65536) * 8);
        if (!pointer(chunk)) return false;
        const auto object = *reinterpret_cast<std::uintptr_t*>(chunk + (index % 65536) * 0x18);
        if (!pointer(object)) continue;
        __try {
            if (*reinterpret_cast<std::int32_t*>(object + 0x0C) != index) continue;
            const auto outer = *reinterpret_cast<std::uintptr_t*>(object + 0x20);
            const auto found = reinterpret_cast<void*>(object);
            if (name_of(names, object, "ConnectToLoginServer") && name_of(names, outer, "NetLoginLibrary")) {
                if (!unique(result.connect, found)) return false;
            } else if (name_of(names, object, "SelectGameServer") && name_of(names, outer, "LU4LoginMode")) {
                if (!unique(result.select_server, found)) return false;
            } else if (name_of(names, object, "SelectCharacter") && name_of(names, outer, "LU4LobbyHUD")) {
                if (!unique(result.select_character, found)) return false;
            } else if (name_of(names, object, "LobbyCharacters") && name_of(names, outer, "LU4LobbyHUD")) {
                if (!unique(result.hud_roster, found)) return false;
            } else if (name_of(names, object, "LobbyCharacters") && name_of(names, outer, "LU4LobbyMode")) {
                if (!unique(result.mode_roster, found)) return false;
            } else if (name_of(names, object, "Default__NetLoginLibrary")) {
                if (!unique(result.library_cdo, found)) return false;
            }
        } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
        if (result.connect && result.select_server && result.select_character &&
            result.library_cdo && result.hud_roster && result.mode_roster) break;
    }
    if (!result.connect || !result.select_server || !result.select_character ||
        !result.library_cdo || !result.hud_roster || !result.mode_roster) return false;
    const auto connect = reinterpret_cast<std::uintptr_t>(result.connect);
    const auto select_server = reinterpret_cast<std::uintptr_t>(result.select_server);
    const auto select_character = reinterpret_cast<std::uintptr_t>(result.select_character);
    const auto cdo = reinterpret_cast<std::uintptr_t>(result.library_cdo);
    const auto hud = reinterpret_cast<std::uintptr_t>(result.hud_roster);
    const auto mode = reinterpret_cast<std::uintptr_t>(result.mode_roster);
    return *reinterpret_cast<std::uint16_t*>(connect + 0xB6) == 32 &&
        (*reinterpret_cast<std::uint32_t*>(connect + 0xB0) & 0x2000) != 0 &&
        *reinterpret_cast<std::uintptr_t*>(connect + 0x20) ==
            *reinterpret_cast<std::uintptr_t*>(cdo + 0x10) &&
        *reinterpret_cast<std::uint16_t*>(select_server + 0xB6) == 4 &&
        *reinterpret_cast<std::uint16_t*>(select_character + 0xB6) == 4 &&
        *reinterpret_cast<std::uint16_t*>(hud + 0xB6) == 16 &&
        *reinterpret_cast<std::uint16_t*>(mode + 0xB6) == 16;
}
}

bool ResolveLoginRuntime(std::uintptr_t base, std::uintptr_t end, LoginRuntime& runtime) {
    if (cached_base == base && cached.chunks) {
        const auto count = *reinterpret_cast<std::int32_t*>(cached.objects_slot + 0x14);
        const auto chunk_count = *reinterpret_cast<std::int32_t*>(cached.objects_slot + 0x1C);
        if (count < 10000 || chunk_count < 1 || chunk_count > 64 ||
            count > chunk_count * 0x10000) { failure = -42; return false; }
        cached.chunks = *reinterpret_cast<std::uintptr_t*>(cached.objects_slot);
        cached.count = count;
        runtime = cached;
        return true;
    }
    std::uintptr_t objects = 0, names = 0;
    if (!discover_tables(base, end, objects, names)) { failure = -40; return false; }
    LoginRuntime resolved{};
    if (!discover_targets(objects, names, resolved)) { failure = -41; return false; }
    cached = resolved;
    cached_base = base;
    runtime = resolved;
    return true;
}

int LoginRuntimeFailure() { return failure; }

void* LoginObjectAt(const LoginRuntime& runtime, int index) {
    if (index < 0 || index >= runtime.count) return nullptr;
    const auto chunk = *reinterpret_cast<std::uintptr_t*>(runtime.chunks + (index / 65536) * 8);
    if (!chunk) return nullptr;
    return *reinterpret_cast<void**>(chunk + static_cast<std::uintptr_t>(index % 65536) * 0x18);
}
