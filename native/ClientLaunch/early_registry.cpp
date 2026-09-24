#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <psapi.h>
#include <cstdint>
#include <cstring>
#include <cwctype>
#include <initializer_list>
#include <vector>
#include "early_registry.h"
#include "trace.h"
#pragma comment(lib, "Psapi.lib")

namespace {
DWORD query_value_thunk_rva() {
    const auto image = reinterpret_cast<const BYTE*>(GetModuleHandleW(L"kernelbase.dll"));
    if (!image) return 0;
    const auto dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(image);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return 0;
    const auto nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(image + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return 0;
    const auto imports = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!imports.VirtualAddress) return 0;
    const auto descriptors = reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(image + imports.VirtualAddress);
    for (const auto* descriptor = descriptors; descriptor->Name; ++descriptor) {
        if (_stricmp(reinterpret_cast<const char*>(image + descriptor->Name), "ntdll.dll") != 0 ||
            !descriptor->OriginalFirstThunk || !descriptor->FirstThunk) continue;
        const auto original = reinterpret_cast<const IMAGE_THUNK_DATA64*>(image + descriptor->OriginalFirstThunk);
        for (DWORD index = 0; original[index].u1.AddressOfData; ++index) {
            if (IMAGE_SNAP_BY_ORDINAL64(original[index].u1.Ordinal)) continue;
            const auto named = reinterpret_cast<const IMAGE_IMPORT_BY_NAME*>(
                image + original[index].u1.AddressOfData);
            if (strcmp(named->Name, "NtQueryValueKey") == 0)
                return descriptor->FirstThunk + index * sizeof(uintptr_t);
        }
    }
    return 0;
}

bool read_machine_guid(wchar_t (&guid)[37]) {
    if (GetEnvironmentVariableW(L"PRICECHECK_HW_MACHINE_GUID", guid, 37) != 36)
        return false;
    for (unsigned index = 0; index < 36; ++index) {
        const wchar_t value = guid[index];
        if (index == 8 || index == 13 || index == 18 || index == 23) {
            if (value != L'-') return false;
        } else if (!iswxdigit(value)) return false;
    }
    return true;
}

bool read_resolved_import(HANDLE process, uintptr_t slot, uintptr_t& original) {
    for (unsigned attempt = 0; attempt < 80; ++attempt) {
        SIZE_T transferred = 0;
        MEMORY_BASIC_INFORMATION region{};
        wchar_t path[MAX_PATH]{};
        if (ReadProcessMemory(process, reinterpret_cast<void*>(slot), &original,
            sizeof(original), &transferred) && transferred == sizeof(original) && original &&
            VirtualQueryEx(process, reinterpret_cast<void*>(original), &region, sizeof(region)) &&
            region.Type == MEM_IMAGE &&
            GetMappedFileNameW(process, region.AllocationBase, path, MAX_PATH)) {
            const wchar_t* name = wcsrchr(path, L'\\');
            if (_wcsicmp(name ? name + 1 : path, L"ntdll.dll") == 0) return true;
        }
        Sleep(50);
    }
    return false;
}
}

void* InstallEarlyRegistryHook(HANDLE process, uintptr_t kernelbase) {
    wchar_t guid[37]{};
    if (!read_machine_guid(guid)) {
        TraceEvent("early_registry_guid_invalid", 1);
        return nullptr;
    }
    const DWORD thunk_rva = query_value_thunk_rva();
    if (!thunk_rva || !kernelbase) {
        TraceEvent("early_registry_import_missing", 1);
        return nullptr;
    }
    TraceEvent("early_registry_thunk_rva", thunk_rva);
    const uintptr_t slot = kernelbase + thunk_rva;
    uintptr_t original = 0;
    if (!read_resolved_import(process, slot, original)) {
        TraceEvent("early_registry_unresolved", 1);
        return nullptr;
    }

    void* remote_data = VirtualAllocEx(process, nullptr, 0x100,
        MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    if (!remote_data) {
        TraceEvent("early_registry_data_error", GetLastError());
        return nullptr;
    }
    const uintptr_t counter = reinterpret_cast<uintptr_t>(remote_data);
    const uintptr_t remote_guid = counter + 16;
    SIZE_T transferred = 0;
    if (!WriteProcessMemory(process, reinterpret_cast<void*>(remote_guid), guid,
        sizeof(guid), &transferred) || transferred != sizeof(guid)) {
        TraceEvent("early_registry_guid_write_error", GetLastError());
        VirtualFreeEx(process, remote_data, 0, MEM_RELEASE);
        return nullptr;
    }

    std::vector<unsigned char> stub;
    auto emit = [&](std::initializer_list<unsigned char> bytes) {
        stub.insert(stub.end(), bytes);
    };
    auto imm64 = [&](uintptr_t value) {
        const auto* bytes = reinterpret_cast<const unsigned char*>(&value);
        stub.insert(stub.end(), bytes, bytes + sizeof(value));
    };
    auto imm32 = [&](uint32_t value) {
        const auto* bytes = reinterpret_cast<const unsigned char*>(&value);
        stub.insert(stub.end(), bytes, bytes + sizeof(value));
    };
    auto imm16 = [&](uint16_t value) {
        const auto* bytes = reinterpret_cast<const unsigned char*>(&value);
        stub.insert(stub.end(), bytes, bytes + sizeof(value));
    };
    std::vector<size_t> skip;
    auto branch_exit = [&](unsigned char condition) {
        emit({0x0F, condition, 0, 0, 0, 0});
        skip.push_back(stub.size() - sizeof(int32_t));
    };

    // Preserve Win64 callee-saved registers, shadow space, and stack args 5/6.
    emit({0x53, 0x56, 0x57, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57,
          0x48, 0x83, 0xEC, 0x40,
          0x48, 0x89, 0xD6, 0x44, 0x89, 0xC3, 0x4C, 0x89, 0xCF,
          0x44, 0x8B, 0xA4, 0x24, 0xA0, 0, 0, 0,
          0x4C, 0x8B, 0xAC, 0x24, 0xA8, 0, 0, 0,
          0x48, 0x8B, 0x84, 0x24, 0xA0, 0, 0, 0,
          0x48, 0x89, 0x44, 0x24, 0x20,
          0x48, 0x8B, 0x84, 0x24, 0xA8, 0, 0, 0,
          0x48, 0x89, 0x44, 0x24, 0x28, 0x48, 0xB8});
    imm64(original);
    emit({0xFF, 0xD0, 0x41, 0x89, 0xC6, 0x48, 0xB8});
    imm64(counter);
    emit({0xF0, 0xFF, 0x00, 0x45, 0x85, 0xF6});
    branch_exit(0x85); // NTSTATUS must be success.
    emit({0x83, 0xFB, 0x02});
    branch_exit(0x85); // KEY_VALUE_PARTIAL_INFORMATION.
    emit({0x48, 0x85, 0xF6});
    branch_exit(0x84);
    emit({0x66, 0x83, 0x3E, 0x16}); // UNICODE_STRING length = 22 bytes.
    branch_exit(0x85);
    emit({0x48, 0x8B, 0x4E, 0x08, 0x48, 0x85, 0xC9});
    branch_exit(0x84);
    const wchar_t name[] = L"MachineGuid";
    uint64_t first = 0, second = 0;
    uint32_t third = 0;
    uint16_t fourth = 0;
    memcpy(&first, name, 8);
    memcpy(&second, name + 4, 8);
    memcpy(&third, name + 8, 4);
    memcpy(&fourth, name + 10, 2);
    emit({0x48, 0xB8}); imm64(first);
    emit({0x48, 0x39, 0x01}); branch_exit(0x85);
    emit({0x48, 0xB8}); imm64(second);
    emit({0x48, 0x39, 0x41, 0x08}); branch_exit(0x85);
    emit({0x81, 0x79, 0x10}); imm32(third); branch_exit(0x85);
    emit({0x66, 0x81, 0x79, 0x14}); imm16(fourth); branch_exit(0x85);
    emit({0x48, 0x85, 0xFF}); branch_exit(0x84);
    emit({0x41, 0x83, 0xFC, 0x56}); branch_exit(0x82);
    emit({0x4D, 0x85, 0xED}); branch_exit(0x84);
    emit({0x41, 0x83, 0x7D, 0x00, 0x56}); branch_exit(0x82);
    emit({0x83, 0x7F, 0x04, 0x01}); branch_exit(0x85);
    emit({0x83, 0x7F, 0x08, 0x4A}); branch_exit(0x85);
    emit({0x48, 0x8D, 0x7F, 0x0C, 0x48, 0xBE}); imm64(remote_guid);
    emit({0xB9, 0x4A, 0, 0, 0, 0xF3, 0xA4, 0x48, 0xB8}); imm64(counter + 4);
    emit({0xF0, 0xFF, 0x00});
    const size_t exit = stub.size();
    emit({0x44, 0x89, 0xF0, 0x48, 0x83, 0xC4, 0x40,
          0x41, 0x5F, 0x41, 0x5E, 0x41, 0x5D, 0x41, 0x5C,
          0x5F, 0x5E, 0x5B, 0xC3});
    for (size_t offset : skip) {
        const auto distance = static_cast<int32_t>(exit - offset - sizeof(int32_t));
        memcpy(stub.data() + offset, &distance, sizeof(distance));
    }

    void* remote_code = VirtualAllocEx(process, nullptr, stub.size(),
        MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    DWORD previous = 0;
    if (!remote_code || !WriteProcessMemory(process, remote_code, stub.data(), stub.size(),
        &transferred) || transferred != stub.size() ||
        !VirtualProtectEx(process, remote_code, stub.size(), PAGE_EXECUTE_READ, &previous)) {
        TraceEvent("early_registry_code_error", GetLastError());
        if (remote_code) VirtualFreeEx(process, remote_code, 0, MEM_RELEASE);
        VirtualFreeEx(process, remote_data, 0, MEM_RELEASE);
        return nullptr;
    }
    if (!VirtualProtectEx(process, reinterpret_cast<void*>(slot), sizeof(uintptr_t),
        PAGE_READWRITE, &previous)) {
        TraceEvent("early_registry_protect_error", GetLastError());
        VirtualFreeEx(process, remote_code, 0, MEM_RELEASE);
        VirtualFreeEx(process, remote_data, 0, MEM_RELEASE);
        return nullptr;
    }
    const uintptr_t replacement = reinterpret_cast<uintptr_t>(remote_code);
    const bool installed = WriteProcessMemory(process, reinterpret_cast<void*>(slot),
        &replacement, sizeof(replacement), &transferred) && transferred == sizeof(replacement);
    DWORD ignored = 0;
    VirtualProtectEx(process, reinterpret_cast<void*>(slot), sizeof(uintptr_t), previous, &ignored);
    if (!installed) {
        TraceEvent("early_registry_write_error", GetLastError());
        VirtualFreeEx(process, remote_code, 0, MEM_RELEASE);
        VirtualFreeEx(process, remote_data, 0, MEM_RELEASE);
        return nullptr;
    }
    TraceEvent("early_registry_installed", 1);
    TraceEvent("early_registry_counter_low", static_cast<unsigned>(counter));
    TraceEvent("early_registry_counter_high", static_cast<unsigned>(counter >> 32));
    return remote_data;
}

void TraceEarlyRegistryCounters(HANDLE process, void* counters) {
    if (!counters) return;
    DWORD values[2]{};
    SIZE_T transferred = 0;
    if (ReadProcessMemory(process, counters, values, sizeof(values), &transferred) &&
        transferred == sizeof(values)) {
        TraceEvent("early_registry_calls", values[0]);
        TraceEvent("early_registry_spoofed", values[1]);
    }
}
