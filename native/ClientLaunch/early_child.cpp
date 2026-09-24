#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <psapi.h>
#include <array>
#include <cstdint>
#include <cstring>
#include <cwchar>
#include <memory>
#include <string>
#include <vector>
#include "minhook/include/MinHook.h"
#include "early_child.h"
#include "early_smart.h"
#include "early_registry.h"
#include "disk_serial.h"
#include "firmware_snapshot.h"
#include "trace.h"
#pragma comment(lib, "Psapi.lib")

namespace {
using CreateProcessFn = BOOL(WINAPI*)(LPCWSTR, LPWSTR, LPSECURITY_ATTRIBUTES,
    LPSECURITY_ATTRIBUTES, BOOL, DWORD, LPVOID, LPCWSTR, LPSTARTUPINFOW,
    LPPROCESS_INFORMATION);
CreateProcessFn real_create_process = nullptr;
std::wstring agent_path;

uintptr_t find_mapped_module(HANDLE process, const wchar_t* wanted) {
    SYSTEM_INFO system{};
    GetNativeSystemInfo(&system);
    const auto maximum = reinterpret_cast<uintptr_t>(system.lpMaximumApplicationAddress);
    uintptr_t address = reinterpret_cast<uintptr_t>(system.lpMinimumApplicationAddress);
    for (unsigned steps = 0; address < maximum && steps < 100000; ++steps) {
        MEMORY_BASIC_INFORMATION region{};
        if (!VirtualQueryEx(process, reinterpret_cast<void*>(address), &region, sizeof(region))) {
            address += 0x10000;
            continue;
        }
        if (region.Type == MEM_IMAGE && region.BaseAddress == region.AllocationBase) {
            wchar_t path[MAX_PATH]{};
            if (GetMappedFileNameW(process, region.AllocationBase, path, MAX_PATH)) {
                const wchar_t* name = wcsrchr(path, L'\\');
                if (_wcsicmp(name ? name + 1 : path, wanted) == 0)
                    return reinterpret_cast<uintptr_t>(region.AllocationBase);
            }
        }
        const uintptr_t next = reinterpret_cast<uintptr_t>(region.BaseAddress) + region.RegionSize;
        if (next <= address) break;
        address = next;
    }
    return 0;
}

uintptr_t find_child_module(HANDLE process, DWORD pid, const wchar_t* wanted) {
    for (int attempt = 0; attempt < 120; ++attempt) {
        HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
        if (snapshot != INVALID_HANDLE_VALUE) {
            MODULEENTRY32W entry{};
            entry.dwSize = sizeof(entry);
            if (Module32FirstW(snapshot, &entry)) do {
                if (_wcsicmp(entry.szModule, wanted) == 0) {
                    const auto base = reinterpret_cast<uintptr_t>(entry.modBaseAddr);
                    CloseHandle(snapshot);
                    return base;
                }
            } while (Module32NextW(snapshot, &entry));
            CloseHandle(snapshot);
        }
        if (attempt >= 8)
            if (const uintptr_t mapped = find_mapped_module(process, wanted)) return mapped;
        Sleep(100);
    }
    return 0;
}

bool read_launch_mac(BYTE (&mac)[6]) {
    wchar_t text[13]{};
    if (GetEnvironmentVariableW(L"PRICECHECK_HW_MAC", text, 13) != 12)
        return false;
    for (size_t index = 0; index < 6; ++index) {
        wchar_t pair[3]{text[index * 2], text[index * 2 + 1], 0};
        wchar_t* end = nullptr;
        unsigned long value = wcstoul(pair, &end, 16);
        if (end != pair + 2 || value > 255) return false;
        mac[index] = static_cast<BYTE>(value);
    }
    return true;
}

void* install_early_adapter_hook(HANDLE process, uintptr_t module, const BYTE (&mac)[6]) {
    const uintptr_t slot = module + 0x4C030;
    uintptr_t original = 0;
    SIZE_T transferred = 0;
    MEMORY_BASIC_INFORMATION region{};
    wchar_t path[MAX_PATH]{};
    bool ready = false;
    for (int attempt = 0; attempt < 40; ++attempt) {
        if (ReadProcessMemory(process, reinterpret_cast<void*>(slot), &original,
                              sizeof(original), &transferred) &&
            transferred == sizeof(original) && original &&
            VirtualQueryEx(process, reinterpret_cast<void*>(original), &region, sizeof(region)) &&
            region.Type == MEM_IMAGE &&
            GetMappedFileNameW(process, region.AllocationBase, path, MAX_PATH)) {
            const wchar_t* name = wcsrchr(path, L'\\');
            if (_wcsicmp(name ? name + 1 : path, L"iphlpapi.dll") == 0) {
                ready = true;
                break;
            }
        }
        Sleep(50);
    }
    if (!ready) { TraceEvent("early_adapter_import_missing", 1); return nullptr; }
    void* counters = VirtualAllocEx(process, nullptr, sizeof(DWORD) * 2,
        MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    if (!counters) { TraceEvent("early_adapter_counter_error", GetLastError()); return nullptr; }
    const auto counter = reinterpret_cast<uintptr_t>(counters);
    std::vector<unsigned char> stub;
    auto emit = [&](std::initializer_list<unsigned char> bytes) {
        stub.insert(stub.end(), bytes);
    };
    auto imm64 = [&](uintptr_t value) {
        const auto* bytes = reinterpret_cast<const unsigned char*>(&value);
        stub.insert(stub.end(), bytes, bytes + sizeof(value));
    };
    auto branch_exit = [&](unsigned char condition) {
        emit({0x0F, condition, 0, 0, 0, 0});
        return stub.size() - 4;
    };
    emit({0x56, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x89, 0xCE});
    emit({0x48, 0xB8}); imm64(original);
    emit({0xFF, 0xD0, 0x49, 0xBA}); imm64(counter);
    emit({0xF0, 0x41, 0xFF, 0x02, 0x85, 0xC0});
    const size_t skip_error = branch_exit(0x85);
    emit({0x48, 0x85, 0xF6});
    const size_t skip_empty = branch_exit(0x84);
    emit({0xC7, 0x86, 0x98, 0x01, 0x00, 0x00});
    stub.insert(stub.end(), mac, mac + 4);
    emit({0x66, 0xC7, 0x86, 0x9C, 0x01, 0x00, 0x00});
    stub.insert(stub.end(), mac + 4, mac + 6);
    emit({0x49, 0xBA}); imm64(counter + sizeof(DWORD));
    emit({0xF0, 0x41, 0xFF, 0x02});
    const size_t exit = stub.size();
    emit({0x48, 0x83, 0xC4, 0x20, 0x5E, 0xC3});
    for (size_t offset : {skip_error, skip_empty}) {
        const auto distance = static_cast<int32_t>(exit - offset - sizeof(int32_t));
        memcpy(stub.data() + offset, &distance, sizeof(distance));
    }
    void* remote = VirtualAllocEx(process, nullptr, stub.size(),
        MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    if (!remote) {
        TraceEvent("early_adapter_alloc_error", GetLastError());
        VirtualFreeEx(process, counters, 0, MEM_RELEASE);
        return nullptr;
    }
    DWORD previous = 0;
    if (!WriteProcessMemory(process, remote, stub.data(), stub.size(), &transferred) ||
        transferred != stub.size() ||
        !VirtualProtectEx(process, remote, stub.size(), PAGE_EXECUTE_READ, &previous)) {
        TraceEvent("early_adapter_code_error", GetLastError());
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        VirtualFreeEx(process, counters, 0, MEM_RELEASE);
        return nullptr;
    }
    if (!VirtualProtectEx(process, reinterpret_cast<void*>(slot), sizeof(uintptr_t),
                          PAGE_READWRITE, &previous)) {
        TraceEvent("early_adapter_protect_error", GetLastError());
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        VirtualFreeEx(process, counters, 0, MEM_RELEASE);
        return nullptr;
    }
    const auto replacement = reinterpret_cast<uintptr_t>(remote);
    const bool installed = WriteProcessMemory(process, reinterpret_cast<void*>(slot),
        &replacement, sizeof(replacement), &transferred) && transferred == sizeof(replacement);
    DWORD ignored = 0;
    VirtualProtectEx(process, reinterpret_cast<void*>(slot), sizeof(uintptr_t), previous, &ignored);
    if (!installed) {
        TraceEvent("early_adapter_write_error", GetLastError());
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        VirtualFreeEx(process, counters, 0, MEM_RELEASE);
        return nullptr;
    }
    TraceEvent("early_adapter_installed", 1);
    return counters;
}

void test_resolver_pass_through(HANDLE process, DWORD pid, void* registry_counters) {
    const uintptr_t module = find_child_module(process, pid, L"clmods64.dll");
    if (!module) { TraceEvent("early_iat_module_missing", GetLastError()); return; }
    TraceEvent("early_iat_module_found", 1);
    const bool spoof_identity =
        GetEnvironmentVariableW(L"PRICECHECK_EARLY_FIRMWARE_SPOOF", nullptr, 0) > 1;
    void* smart_counters = nullptr;
    void* adapter_counters = nullptr;
    if (spoof_identity) {
        BYTE ata_serial[20]{};
        if (!BuildAtaSerialFromEnvironment(ata_serial)) {
            TraceEvent("early_smart_serial_invalid", 1);
            return;
        }
        smart_counters = InstallEarlySmartHook(process, module, ata_serial);
        if (!smart_counters) return;
        BYTE mac[6]{};
        if (!read_launch_mac(mac)) {
            TraceEvent("early_adapter_mac_invalid", 1);
            return;
        }
        adapter_counters = install_early_adapter_hook(process, module, mac);
        if (!adapter_counters) return;
    }
    const uintptr_t slot = module + 0x4C040;
    uintptr_t original = 0;
    SIZE_T transferred = 0;
    MEMORY_BASIC_INFORMATION target_region{};
    wchar_t target_path[MAX_PATH]{};
    bool ready = false;
    unsigned last_state = 0;
    for (int attempt = 0; attempt < 40; ++attempt) {
        if (!ReadProcessMemory(process, reinterpret_cast<void*>(slot), &original,
                               sizeof(original), &transferred) || transferred != sizeof(original))
            last_state = 1;
        else if (!original) last_state = 2;
        else if (!VirtualQueryEx(process, reinterpret_cast<void*>(original),
                                &target_region, sizeof(target_region))) last_state = 3;
        else if (target_region.Type != MEM_IMAGE) last_state = 4;
        else if (!GetMappedFileNameW(process, target_region.AllocationBase, target_path, MAX_PATH)) last_state = 5;
        else { ready = true; break; }
        Sleep(50);
    }
    if (!ready) { TraceEvent("early_iat_unexpected", last_state); return; }
    const wchar_t* target_name = wcsrchr(target_path, L'\\');
    if (_wcsicmp(target_name ? target_name + 1 : target_path, L"kernel32.dll") != 0) {
        TraceEvent("early_iat_unexpected", 2);
        return;
    }

    const bool spoof_firmware = spoof_identity;
    std::vector<BYTE> snapshot;
    void* remote_firmware = nullptr;
    if (spoof_firmware) {
        unsigned patched_fields = 0;
        if (!BuildEarlyFirmwareSnapshot(snapshot, patched_fields)) {
            TraceEvent("early_firmware_snapshot_error", 1);
            return;
        }
        remote_firmware = VirtualAllocEx(process, nullptr, snapshot.size(),
            MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
        if (!remote_firmware) {
            TraceEvent("early_firmware_alloc_error", GetLastError());
            return;
        }
        if (!WriteProcessMemory(process, remote_firmware, snapshot.data(),
                                snapshot.size(), &transferred) ||
            transferred != snapshot.size()) {
            TraceEvent("early_firmware_write_error", GetLastError());
            VirtualFreeEx(process, remote_firmware, 0, MEM_RELEASE);
            return;
        }
        DWORD previous = 0;
        if (!VirtualProtectEx(process, remote_firmware, snapshot.size(),
                              PAGE_READONLY, &previous)) {
            TraceEvent("early_firmware_protect_error", GetLastError());
            VirtualFreeEx(process, remote_firmware, 0, MEM_RELEASE);
            return;
        }
        TraceEvent("early_firmware_snapshot_size", static_cast<DWORD>(snapshot.size()));
        TraceEvent("early_firmware_fields", patched_fields);
    }

    constexpr SIZE_T counter_count = 6;
    void* counter = VirtualAllocEx(process, nullptr,
        sizeof(DWORD) * counter_count + sizeof(uintptr_t),
        MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    if (!counter) {
        TraceEvent("early_iat_counter_error", GetLastError());
        if (remote_firmware) VirtualFreeEx(process, remote_firmware, 0, MEM_RELEASE);
        return;
    }
    const auto counter_address = reinterpret_cast<uintptr_t>(counter);
    const auto target_counter_address = counter_address + sizeof(DWORD);
    const auto firmware_counter_address = counter_address + sizeof(DWORD) * 2;
    const auto rsmb_counter_address = counter_address + sizeof(DWORD) * 3;
    const auto spoof_counter_address = counter_address + sizeof(DWORD) * 4;
    const auto ntquery_counter_address = counter_address + sizeof(DWORD) * 5;
    const auto firmware_original_slot = counter_address + sizeof(DWORD) * 6;
    const auto firmware_name_address = module + 0x5E828;
    std::vector<unsigned char> stub;
    auto emit = [&](std::initializer_list<unsigned char> bytes) {
        stub.insert(stub.end(), bytes);
    };
    auto mov_rax = [&](uintptr_t value) {
        emit({0x48, 0xB8});
        const size_t immediate = stub.size();
        const auto* bytes = reinterpret_cast<const unsigned char*>(&value);
        stub.insert(stub.end(), bytes, bytes + sizeof(value));
        return immediate;
    };
    auto mov_r10 = [&](uintptr_t value) {
        emit({0x49, 0xBA});
        const auto* bytes = reinterpret_cast<const unsigned char*>(&value);
        stub.insert(stub.end(), bytes, bytes + sizeof(value));
    };
    // Resolver: call the original GetProcAddress and replace only the
    // GetSystemFirmwareTable result requested by clmods64.dll.
    emit({0x53, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x89, 0xD3});
    mov_rax(counter_address);
    emit({0xF0, 0xFF, 0x00});
    mov_rax(original);
    emit({0xFF, 0xD0});
    // Observe whether clmods resolves NtQueryValueKey through this import.
    // This pass-through probe does not change the returned function pointer.
    const auto ntdll = GetModuleHandleW(L"ntdll.dll");
    const auto ntquery = ntdll ? GetProcAddress(ntdll, "NtQueryValueKey") : nullptr;
    if (ntquery) {
        mov_r10(reinterpret_cast<uintptr_t>(ntquery));
        emit({0x4C, 0x39, 0xD0, 0x75, 0x00});  // cmp rax, r10; jne after counter
        const size_t skip_ntquery_displacement = stub.size() - 1;
        mov_r10(ntquery_counter_address);
        emit({0xF0, 0x41, 0xFF, 0x02});
        stub[skip_ntquery_displacement] = static_cast<unsigned char>(
            stub.size() - skip_ntquery_displacement - 1);
    }
    mov_r10(firmware_name_address);
    emit({0x4C, 0x39, 0xD3, 0x75, 0x00});
    const size_t skip_target_displacement = stub.size() - 1;
    mov_r10(target_counter_address);
    emit({0xF0, 0x41, 0xFF, 0x02});
    mov_r10(firmware_original_slot);
    emit({0x49, 0x89, 0x02});
    const size_t callback_address_immediate = mov_rax(0);
    const size_t resolver_exit = stub.size();
    emit({0x48, 0x83, 0xC4, 0x20, 0x5B, 0xC3});
    const size_t callback_offset = stub.size();
    // Firmware callback preserves the real API's size and error behavior.
    // In spoof mode it replaces only a successful full RSMB table result.
    emit({0x53, 0x56, 0x57, 0x41, 0x54, 0x48, 0x83, 0xEC, 0x28});
    emit({0x89, 0xCB, 0x41, 0x89, 0xD4, 0x4C, 0x89, 0xC6, 0x44, 0x89, 0xCF});
    mov_r10(firmware_original_slot);
    emit({0x49, 0x8B, 0x02, 0xFF, 0xD0});
    mov_r10(firmware_counter_address);
    emit({0xF0, 0x41, 0xFF, 0x02});
    std::vector<size_t> callback_exit_branches;
    auto branch_exit = [&](unsigned char condition) {
        emit({0x0F, condition, 0, 0, 0, 0});
        callback_exit_branches.push_back(stub.size() - 4);
    };
    emit({0x81, 0xFB, 0x42, 0x4D, 0x53, 0x52});
    branch_exit(0x85);  // provider is not RSMB
    mov_r10(rsmb_counter_address);
    emit({0xF0, 0x41, 0xFF, 0x02});
    if (spoof_firmware) {
        emit({0x45, 0x85, 0xE4});
        branch_exit(0x85);  // nonzero table ID
        emit({0x48, 0x85, 0xF6});
        branch_exit(0x84);  // size-only query
        emit({0x3D});
        const DWORD length = static_cast<DWORD>(snapshot.size());
        const auto* length_bytes = reinterpret_cast<const unsigned char*>(&length);
        stub.insert(stub.end(), length_bytes, length_bytes + sizeof(length));
        branch_exit(0x85);  // the physical table size changed
        emit({0x39, 0xC7});
        branch_exit(0x82);  // caller buffer is too small
        emit({0x41, 0x89, 0xC4, 0x4C, 0x89, 0xE1, 0x48, 0x89, 0xF7});
        emit({0x48, 0xBE});
        const auto firmware_address = reinterpret_cast<uintptr_t>(remote_firmware);
        const auto* firmware_bytes = reinterpret_cast<const unsigned char*>(&firmware_address);
        stub.insert(stub.end(), firmware_bytes, firmware_bytes + sizeof(firmware_address));
        emit({0xF3, 0xA4, 0x44, 0x89, 0xE0});
        mov_r10(spoof_counter_address);
        emit({0xF0, 0x41, 0xFF, 0x02});
    }
    const size_t callback_exit = stub.size();
    emit({0x48, 0x83, 0xC4, 0x28, 0x41, 0x5C, 0x5F, 0x5E, 0x5B, 0xC3});
    const size_t target_distance = resolver_exit - skip_target_displacement - 1;
    if (target_distance > 127) {
        TraceEvent("early_iat_stub_branch_error", 1);
        VirtualFreeEx(process, counter, 0, MEM_RELEASE);
        if (remote_firmware) VirtualFreeEx(process, remote_firmware, 0, MEM_RELEASE);
        return;
    }
    stub[skip_target_displacement] = static_cast<unsigned char>(target_distance);
    for (size_t offset : callback_exit_branches) {
        const auto distance = static_cast<int32_t>(callback_exit - offset - sizeof(int32_t));
        memcpy(stub.data() + offset, &distance, sizeof(distance));
    }
    void* remote = VirtualAllocEx(process, nullptr, stub.size(),
        MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    if (!remote) {
        TraceEvent("early_iat_alloc_error", GetLastError());
        VirtualFreeEx(process, counter, 0, MEM_RELEASE);
        if (remote_firmware) VirtualFreeEx(process, remote_firmware, 0, MEM_RELEASE);
        return;
    }
    const auto callback_address = reinterpret_cast<uintptr_t>(remote) + callback_offset;
    memcpy(stub.data() + callback_address_immediate, &callback_address,
        sizeof(callback_address));
    DWORD code_protection = 0;
    if (!WriteProcessMemory(process, remote, stub.data(), stub.size(), &transferred) ||
        transferred != stub.size() ||
        !VirtualProtectEx(process, remote, stub.size(), PAGE_EXECUTE_READ, &code_protection)) {
        TraceEvent("early_iat_code_error", GetLastError());
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        VirtualFreeEx(process, counter, 0, MEM_RELEASE);
        if (remote_firmware) VirtualFreeEx(process, remote_firmware, 0, MEM_RELEASE);
        return;
    }
    DWORD old_protection = 0;
    if (!VirtualProtectEx(process, reinterpret_cast<void*>(slot), sizeof(uintptr_t),
                          PAGE_READWRITE, &old_protection)) {
        TraceEvent("early_iat_protect_error", GetLastError());
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        VirtualFreeEx(process, counter, 0, MEM_RELEASE);
        if (remote_firmware) VirtualFreeEx(process, remote_firmware, 0, MEM_RELEASE);
        return;
    }
    const auto replacement = reinterpret_cast<uintptr_t>(remote);
    const bool installed = WriteProcessMemory(process, reinterpret_cast<void*>(slot),
        &replacement, sizeof(replacement), &transferred) && transferred == sizeof(replacement);
    DWORD ignored = 0;
    VirtualProtectEx(process, reinterpret_cast<void*>(slot), sizeof(uintptr_t), old_protection, &ignored);
    if (!installed) {
        TraceEvent("early_iat_write_error", GetLastError());
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        VirtualFreeEx(process, counter, 0, MEM_RELEASE);
        if (remote_firmware) VirtualFreeEx(process, remote_firmware, 0, MEM_RELEASE);
        return;
    }
    TraceEvent("early_iat_installed", 1);
    for (int attempt = 0; attempt < 40; ++attempt) {
        Sleep(100);
        DWORD hits[counter_count]{};
        if (ReadProcessMemory(process, counter, &hits, sizeof(hits), &transferred) &&
            transferred == sizeof(hits) && hits[0] > 0) {
            Sleep(500);
            if (ReadProcessMemory(process, counter, &hits, sizeof(hits), &transferred) &&
                transferred == sizeof(hits)) {
                TraceEvent("early_iat_hits", hits[0]);
                TraceEvent("early_firmware_resolver_hits", hits[1]);
                TraceEvent("early_firmware_calls", hits[2]);
                TraceEvent("early_rsmb_calls", hits[3]);
                TraceEvent("early_firmware_spoofed", hits[4]);
                TraceEvent("early_ntquery_resolver_hits", hits[5]);
                TraceEarlySmartCounters(process, smart_counters);
                TraceEarlyRegistryCounters(process, registry_counters);
                if (adapter_counters) {
                    DWORD adapter_hits[2]{};
                    if (ReadProcessMemory(process, adapter_counters, adapter_hits,
                                          sizeof(adapter_hits), &transferred) &&
                        transferred == sizeof(adapter_hits)) {
                        TraceEvent("early_adapter_calls", adapter_hits[0]);
                        TraceEvent("early_adapter_spoofed", adapter_hits[1]);
                    }
                }
            }
            break;
        }
    }
    if (smart_counters) {
        Sleep(10000);
        TraceEvent("early_ioctl_late_sample", 1);
        TraceEarlySmartCounters(process, smart_counters);
        TraceEarlyRegistryCounters(process, registry_counters);
    }
    // The test-only import hook and its code/data pages live until the owned
    // child exits; the launcher may terminate before a helper can restore them.
}

struct PassThroughTask { HANDLE process; DWORD pid; };
DWORD WINAPI pass_through_worker(void* raw) {
    std::unique_ptr<PassThroughTask> task(static_cast<PassThroughTask*>(raw));
    void* registry_counters = nullptr;
    if (GetEnvironmentVariableW(L"PRICECHECK_EARLY_REGISTRY_SPOOF", nullptr, 0) > 1) {
        const uintptr_t kernelbase = find_child_module(task->process, task->pid,
            L"KernelBase.dll");
        if (kernelbase) registry_counters = InstallEarlyRegistryHook(task->process, kernelbase);
        else TraceEvent("early_registry_module_missing", 1);
    }
    test_resolver_pass_through(task->process, task->pid, registry_counters);
    TraceEarlyRegistryCounters(task->process, registry_counters);
    CloseHandle(task->process);
    return 0;
}

bool is_game_child(HANDLE process) {
    wchar_t image[MAX_PATH]{};
    DWORD length = MAX_PATH;
    if (!QueryFullProcessImageNameW(process, 0, image, &length)) return false;
    const wchar_t* basename = wcsrchr(image, L'\\');
    return _wcsicmp(basename ? basename + 1 : image, L"lu4.bin") == 0;
}

bool load_agent(HANDLE process) {
    SIZE_T bytes = (agent_path.size() + 1) * sizeof(wchar_t);
    void* remote = VirtualAllocEx(process, nullptr, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remote) { TraceEvent("early_alloc_error", GetLastError()); return false; }
    SIZE_T written = 0;
    bool okay = WriteProcessMemory(process, remote, agent_path.c_str(), bytes, &written) && written == bytes;
    if (!okay) { TraceEvent("early_write_error", GetLastError()); VirtualFreeEx(process, remote, 0, MEM_RELEASE); return false; }
    auto loader = reinterpret_cast<LPTHREAD_START_ROUTINE>(GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "LoadLibraryW"));
    if (!loader) { TraceEvent("early_loader_missing"); VirtualFreeEx(process, remote, 0, MEM_RELEASE); return false; }
    HANDLE thread = CreateRemoteThread(process, nullptr, 0, loader, remote, 0, nullptr);
    if (!thread) { TraceEvent("early_thread_error", GetLastError()); VirtualFreeEx(process, remote, 0, MEM_RELEASE); return false; }
    DWORD wait = WaitForSingleObject(thread, 10000);
    DWORD result = 0;
    okay = wait == WAIT_OBJECT_0 && GetExitCodeThread(thread, &result) && result != 0;
    TraceEvent("early_loader_result", okay ? 1 : 0);
    CloseHandle(thread);
    if (wait == WAIT_OBJECT_0) VirtualFreeEx(process, remote, 0, MEM_RELEASE);
    return okay;
}

BOOL WINAPI hooked_create_process(LPCWSTR application, LPWSTR command_line,
    LPSECURITY_ATTRIBUTES process_attributes, LPSECURITY_ATTRIBUTES thread_attributes,
    BOOL inherit_handles, DWORD flags, LPVOID environment, LPCWSTR directory,
    LPSTARTUPINFOW startup, LPPROCESS_INFORMATION child) {
    if (!child) return real_create_process(application, command_line, process_attributes,
        thread_attributes, inherit_handles, flags, environment, directory, startup, child);
    BOOL result = real_create_process(application, command_line, process_attributes,
        thread_attributes, inherit_handles, flags | CREATE_SUSPENDED, environment, directory, startup, child);
    if (!result) return result;
    bool game = is_game_child(child->hProcess);
    if (game) {
        TraceEvent("early_child_pid", child->dwProcessId);
        TraceEvent("early_child_flags", flags);
        TraceEvent("early_iat_requested",
            GetEnvironmentVariableW(L"PRICECHECK_EARLY_IAT_PASSTHROUGH", nullptr, 0));
        const bool skip_child_agent =
            GetEnvironmentVariableW(L"PRICECHECK_EARLY_CHILD_SKIP_AGENT", nullptr, 0) > 1;
        if (skip_child_agent) {
            TraceEvent("early_child_agent_skipped", 1);
            if (GetEnvironmentVariableW(L"PRICECHECK_EARLY_IAT_PASSTHROUGH", nullptr, 0) > 1) {
                HANDLE retained = nullptr;
                if (DuplicateHandle(GetCurrentProcess(), child->hProcess, GetCurrentProcess(),
                    &retained, 0, FALSE, DUPLICATE_SAME_ACCESS)) {
                    auto task = std::make_unique<PassThroughTask>();
                    task->process = retained;
                    task->pid = child->dwProcessId;
                    HANDLE worker = CreateThread(nullptr, 0, pass_through_worker, task.get(), 0, nullptr);
                    if (worker) {
                        task.release();
                        CloseHandle(worker);
                        TraceEvent("early_iat_worker_started", 1);
                    } else {
                        TraceEvent("early_iat_worker_error", GetLastError());
                        CloseHandle(retained);
                    }
                } else TraceEvent("early_iat_dup_error", GetLastError());
            }
        }
        else if (!load_agent(child->hProcess)) {
            TerminateProcess(child->hProcess, ERROR_ACCESS_DENIED);
            CloseHandle(child->hThread);
            CloseHandle(child->hProcess);
            ZeroMemory(child, sizeof(*child));
            SetLastError(ERROR_ACCESS_DENIED);
            return FALSE;
        }
    }
    if (!(flags & CREATE_SUSPENDED)) ResumeThread(child->hThread);
    return TRUE;
}
}

bool IsEarlyLaunchRoot() {
    if (GetEnvironmentVariableW(L"PRICECHECK_EARLY_CHILD_INJECT", nullptr, 0) <= 1) return false;
    wchar_t image[MAX_PATH]{};
    DWORD length = GetModuleFileNameW(nullptr, image, MAX_PATH);
    if (!length || length >= MAX_PATH) return false;
    const wchar_t* basename = wcsrchr(image, L'\\');
    return _wcsicmp(basename ? basename + 1 : image, L"lu4-win64-shipping.exe") == 0;
}

bool InstallEarlyChildHook() {
    HMODULE self = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
        GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&InstallEarlyChildHook), &self)) return false;
    wchar_t path[MAX_PATH]{};
    DWORD length = GetModuleFileNameW(self, path, MAX_PATH);
    if (!length || length >= MAX_PATH) return false;
    agent_path.assign(path, length);
    return MH_CreateHookApi(L"kernel32.dll", "CreateProcessW", hooked_create_process,
        reinterpret_cast<void**>(&real_create_process)) == MH_OK;
}
