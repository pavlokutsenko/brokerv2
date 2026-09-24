#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <psapi.h>
#include <cstdint>
#include <cstring>
#include <cstdio>
#include <cwchar>
#include <initializer_list>
#include <vector>
#include "early_smart.h"
#include "trace.h"
#pragma comment(lib, "Psapi.lib")

namespace {
bool read_import(HANDLE process, uintptr_t slot, uintptr_t& original) {
    SIZE_T transferred = 0;
    MEMORY_BASIC_INFORMATION region{};
    wchar_t path[MAX_PATH]{};
    for (int attempt = 0; attempt < 40; ++attempt) {
        if (ReadProcessMemory(process, reinterpret_cast<void*>(slot), &original,
                              sizeof(original), &transferred) &&
            transferred == sizeof(original) && original &&
            VirtualQueryEx(process, reinterpret_cast<void*>(original), &region,
                           sizeof(region)) && region.Type == MEM_IMAGE &&
            GetMappedFileNameW(process, region.AllocationBase, path, MAX_PATH)) {
            const wchar_t* name = wcsrchr(path, L'\\');
            name = name ? name + 1 : path;
            if (_wcsicmp(name, L"kernel32.dll") == 0 ||
                _wcsicmp(name, L"kernelbase.dll") == 0)
                return true;
        }
        Sleep(50);
    }
    return false;
}

bool read_hex_mac(const wchar_t* variable, BYTE (&mac)[6]) {
    wchar_t text[13]{};
    if (GetEnvironmentVariableW(variable, text, 13) != 12)
        return false;
    auto nibble = [](wchar_t ch) -> int {
        if (ch >= L'0' && ch <= L'9') return ch - L'0';
        if (ch >= L'A' && ch <= L'F') return ch - L'A' + 10;
        if (ch >= L'a' && ch <= L'f') return ch - L'a' + 10;
        return -1;
    };
    for (size_t index = 0; index < 6; ++index) {
        const int high = nibble(text[index * 2]);
        const int low = nibble(text[index * 2 + 1]);
        if (high < 0 || low < 0) return false;
        mac[index] = static_cast<BYTE>((high << 4) | low);
    }
    return true;
}

bool read_probe_mac(BYTE (&mac)[6]) {
    return read_hex_mac(L"PRICECHECK_TEST_MAC_NEEDLE_HEX", mac);
}

DWORD first_mac_offset(const BYTE* data, size_t size, const BYTE (&mac)[6]) {
    for (size_t offset = 0; offset + 6 <= size; ++offset)
        if (memcmp(data + offset, mac, 6) == 0) return static_cast<DWORD>(offset);
    return 0xFFFFFFFFu;
}
}

void* InstallEarlySmartHook(HANDLE process, uintptr_t module,
                            const BYTE (&ata_serial)[20]) {
    const uintptr_t slot = module + 0x4C098;
    uintptr_t original = 0;
    if (!read_import(process, slot, original)) {
        TraceEvent("early_smart_import_missing", 1);
        return nullptr;
    }
    constexpr size_t recorded_codes = 16;
    constexpr size_t capture_bytes = 0x4BA;
    constexpr size_t capture_slots = 8;
    constexpr size_t capture_01c_bytes = 0xB0;
    constexpr size_t counter_bytes = sizeof(DWORD) * (4 + recorded_codes * 4) +
                                     (sizeof(uint64_t) + sizeof(DWORD)) * capture_slots;
    const bool capture_044 =
        GetEnvironmentVariableW(L"PRICECHECK_EARLY_044_CAPTURE", nullptr, 0) > 1;
    const size_t capture_storage_bytes = capture_044 ?
        capture_bytes * capture_slots + sizeof(DWORD) * 2 + capture_01c_bytes : 0;
    void* counters = VirtualAllocEx(process, nullptr,
        counter_bytes + capture_storage_bytes,
        MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    if (!counters) { TraceEvent("early_smart_counter_error", GetLastError()); return nullptr; }
    const uintptr_t counter = reinterpret_cast<uintptr_t>(counters);
    std::vector<unsigned char> stub;
    auto emit = [&](std::initializer_list<unsigned char> bytes) {
        stub.insert(stub.end(), bytes);
    };
    auto imm64 = [&](uintptr_t value) {
        const auto* bytes = reinterpret_cast<const unsigned char*>(&value);
        stub.insert(stub.end(), bytes, bytes + sizeof(value));
    };
    auto branch_exit = [&](unsigned char condition, std::vector<size_t>& branches) {
        emit({0x0F, condition, 0, 0, 0, 0});
        branches.push_back(stub.size() - 4);
    };
    auto copy_stack_arg = [&](unsigned char source, unsigned char destination) {
        emit({0x48, 0x8B, 0x84, 0x24, source, 0, 0, 0});
        if (source == 0x80) emit({0x48, 0x89, 0xC6}); // remember output pointer
        if (source == 0x88) emit({0x89, 0xC7});       // remember output capacity
        emit({0x48, 0x89, 0x44, 0x24, destination});
    };
    // Win64 arguments five through eight must be copied to the new call frame.
    emit({0x53, 0x56, 0x57, 0x48, 0x83, 0xEC, 0x40, 0x89, 0xD3});
    copy_stack_arg(0x80, 0x20);
    copy_stack_arg(0x88, 0x28);
    copy_stack_arg(0x90, 0x30);
    copy_stack_arg(0x98, 0x38);
    emit({0x48, 0xB8}); imm64(original);
    emit({0xFF, 0xD0, 0x49, 0xBA}); imm64(counter);
    emit({0xB9, 1, 0, 0, 0, 0xF0, 0x41, 0x0F, 0xC1, 0x0A});
    emit({0x83, 0xF9, static_cast<unsigned char>(recorded_codes)});
    emit({0x0F, 0x83, 0, 0, 0, 0});
    const size_t skip_record = stub.size() - 4;
    emit({0x41, 0x89, 0x5C, 0x8A, 0x0C});
    emit({0x41, 0x89, 0x7C, 0x8A, 0x4C});
    emit({0x41, 0x89, 0x84, 0x8A, 0x90, 0x00, 0x00, 0x00});
    emit({0x41, 0x89, 0xC3, 0x48, 0x85, 0xF6, 0x0F, 0x95, 0xC0});
    emit({0x0F, 0xB6, 0xC0, 0x41, 0x89, 0x84, 0x8A, 0xD0, 0x00, 0x00, 0x00});
    emit({0x44, 0x89, 0xD8});
    const size_t after_record = stub.size();
    const auto record_distance = static_cast<int32_t>(after_record - skip_record - sizeof(int32_t));
    memcpy(stub.data() + skip_record, &record_distance, sizeof(record_distance));
    if (capture_044) {
        std::vector<size_t> skip_capture;
        emit({0x81, 0xFB, 0x44, 0x20, 0x22, 0x00});
        branch_exit(0x85, skip_capture);
        emit({0x85, 0xC0});
        branch_exit(0x84, skip_capture);
        emit({0x81, 0xFF, 0xBA, 0x04, 0x00, 0x00});
        branch_exit(0x82, skip_capture);
        emit({0x48, 0x85, 0xF6});
        branch_exit(0x84, skip_capture);
        emit({0x41, 0x89, 0xC3}); // keep the real BOOL in r11d
        emit({0x41, 0x8B, 0x82, 0x8C, 0x00, 0x00, 0x00}); // capture count
        emit({0x83, 0xF8, static_cast<unsigned char>(capture_slots)});
        std::vector<size_t> skip_copy;
        branch_exit(0x83, skip_copy);
        emit({0x48, 0x8B, 0x4C, 0x24, 0x30, 0x48, 0x85, 0xC9});
        std::vector<size_t> skip_returned;
        branch_exit(0x84, skip_returned);
        emit({0x8B, 0x09, 0x48, 0xBF});
        imm64(counter + sizeof(DWORD) * (4 + recorded_codes * 4) +
              sizeof(uint64_t) * capture_slots);
        emit({0x89, 0x0C, 0x87}); // returned byte count by capture index
        const size_t after_returned = stub.size();
        for (size_t offset : skip_returned) {
            const auto distance = static_cast<int32_t>(after_returned - offset - sizeof(int32_t));
            memcpy(stub.data() + offset, &distance, sizeof(distance));
        }
        emit({0x48, 0x8B, 0x8C, 0x24, 0xA8, 0x00, 0x00, 0x00});
        emit({0x48, 0xBF}); imm64(module);
        emit({0x48, 0x29, 0xF9, 0x48, 0xBF});
        imm64(counter + sizeof(DWORD) * (4 + recorded_codes * 4));
        emit({0x48, 0x89, 0x0C, 0xC7}); // store caller RVA by capture index
        emit({0x69, 0xC0, 0xBA, 0x04, 0x00, 0x00, 0x48, 0xBF});
        imm64(counter + counter_bytes);
        emit({0x48, 0x01, 0xC7, 0xB9, 0xBA, 0x04, 0x00, 0x00, 0xF3, 0xA4});
        emit({0xF0, 0x41, 0xFF, 0x82, 0x8C, 0x00, 0x00, 0x00});
        const size_t after_copy = stub.size();
        for (size_t offset : skip_copy) {
            const auto distance = static_cast<int32_t>(after_copy - offset - sizeof(int32_t));
            memcpy(stub.data() + offset, &distance, sizeof(distance));
        }
        emit({0x44, 0x89, 0xD8}); // restore BOOL
        const size_t after_044 = stub.size();
        for (size_t offset : skip_capture) {
            const auto distance = static_cast<int32_t>(
                after_044 - offset - sizeof(int32_t));
            memcpy(stub.data() + offset, &distance, sizeof(distance));
        }

        std::vector<size_t> skip_01c;
        emit({0x81, 0xFB, 0x1C, 0x20, 0x22, 0x00});
        branch_exit(0x85, skip_01c);
        emit({0x85, 0xC0});
        branch_exit(0x84, skip_01c);
        emit({0x81, 0xFF, 0xB0, 0x00, 0x00, 0x00});
        branch_exit(0x82, skip_01c);
        emit({0x48, 0x85, 0xF6});
        branch_exit(0x84, skip_01c);
        emit({0x41, 0x89, 0xC3, 0x48, 0xBF});
        imm64(counter + counter_bytes + capture_bytes * capture_slots + sizeof(DWORD) * 2);
        emit({0xB9, 0xB0, 0x00, 0x00, 0x00, 0xF3, 0xA4, 0x49, 0xBA});
        imm64(counter + counter_bytes + capture_bytes * capture_slots);
        emit({0xF0, 0x41, 0xFF, 0x02});
        emit({0x48, 0x8B, 0x44, 0x24, 0x30, 0x48, 0x85, 0xC0});
        std::vector<size_t> skip_01c_returned;
        branch_exit(0x84, skip_01c_returned);
        emit({0x8B, 0x00, 0x41, 0x89, 0x42, 0x04});
        const size_t after_01c_returned = stub.size();
        for (size_t offset : skip_01c_returned) {
            const auto distance = static_cast<int32_t>(after_01c_returned - offset - sizeof(int32_t));
            memcpy(stub.data() + offset, &distance, sizeof(distance));
        }
        emit({0x44, 0x89, 0xD8});
        const size_t after_01c = stub.size();
        for (size_t offset : skip_01c) {
            const auto distance = static_cast<int32_t>(after_01c - offset - sizeof(int32_t));
            memcpy(stub.data() + offset, &distance, sizeof(distance));
        }
    }
    std::vector<size_t> exit_branches;
    emit({0x85, 0xC0});
    branch_exit(0x84, exit_branches); // original call failed
    emit({0x81, 0xFB, 0x88, 0xC0, 0x07, 0x00});
    branch_exit(0x85, exit_branches); // not SMART_RCV_DRIVE_DATA
    emit({0x49, 0xBA}); imm64(counter + sizeof(DWORD));
    emit({0xF0, 0x41, 0xFF, 0x02, 0x83, 0xFF, 0x38});
    branch_exit(0x82, exit_branches); // not enough output capacity
    emit({0x48, 0x85, 0xF6});
    branch_exit(0x84, exit_branches); // null output buffer
    emit({0x89, 0xC7}); // save the real BOOL result before using RAX
    emit({0x48, 0xB8});
    stub.insert(stub.end(), ata_serial, ata_serial + 8);
    emit({0x48, 0x89, 0x46, 0x24, 0x48, 0xB8});
    stub.insert(stub.end(), ata_serial + 8, ata_serial + 16);
    emit({0x48, 0x89, 0x46, 0x2C, 0xB8});
    stub.insert(stub.end(), ata_serial + 16, ata_serial + 20);
    emit({0x89, 0x46, 0x34, 0x89, 0xF8, 0x49, 0xBA});
    imm64(counter + sizeof(DWORD) * 2);
    emit({0xF0, 0x41, 0xFF, 0x02});
    const size_t exit = stub.size();
    emit({0x48, 0x83, 0xC4, 0x40, 0x5F, 0x5E, 0x5B, 0xC3});
    for (size_t offset : exit_branches) {
        const auto distance = static_cast<int32_t>(exit - offset - sizeof(int32_t));
        memcpy(stub.data() + offset, &distance, sizeof(distance));
    }

    void* remote = VirtualAllocEx(process, nullptr, stub.size(),
        MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    if (!remote) {
        TraceEvent("early_smart_alloc_error", GetLastError());
        VirtualFreeEx(process, counters, 0, MEM_RELEASE);
        return nullptr;
    }
    SIZE_T transferred = 0;
    DWORD previous = 0;
    if (!WriteProcessMemory(process, remote, stub.data(), stub.size(), &transferred) ||
        transferred != stub.size() ||
        !VirtualProtectEx(process, remote, stub.size(), PAGE_EXECUTE_READ, &previous)) {
        TraceEvent("early_smart_code_error", GetLastError());
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        VirtualFreeEx(process, counters, 0, MEM_RELEASE);
        return nullptr;
    }
    if (!VirtualProtectEx(process, reinterpret_cast<void*>(slot), sizeof(uintptr_t),
                          PAGE_READWRITE, &previous)) {
        TraceEvent("early_smart_protect_error", GetLastError());
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
        TraceEvent("early_smart_write_error", GetLastError());
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        VirtualFreeEx(process, counters, 0, MEM_RELEASE);
        return nullptr;
    }
    TraceEvent("early_smart_installed", 1);
    return counters;
}

void TraceEarlySmartCounters(HANDLE process, void* counters) {
    if (!counters) return;
    DWORD values[68]{};
    SIZE_T transferred = 0;
    if (ReadProcessMemory(process, counters, values, sizeof(values), &transferred) &&
        transferred == sizeof(values)) {
        TraceEvent("early_ioctl_calls", values[0]);
        TraceEvent("early_smart_calls", values[1]);
        TraceEvent("early_smart_spoofed", values[2]);
        const unsigned observed = values[0] < 16 ? values[0] : 16;
        for (unsigned index = 0; index < observed; ++index) {
            TraceEvent("early_ioctl_code", values[3 + index]);
            TraceEvent("early_ioctl_output_size", values[19 + index]);
            TraceEvent("early_ioctl_success", values[36 + index]);
            TraceEvent("early_ioctl_output_pointer", values[52 + index]);
        }
        TraceEvent("early_044_captures", values[35]);
        if (GetEnvironmentVariableW(L"PRICECHECK_EARLY_044_CAPTURE", nullptr, 0) <= 1)
            return;
        constexpr size_t capture_bytes = 0x4BA;
        constexpr size_t capture_slots = 8;
        BYTE samples[capture_slots][capture_bytes]{};
        uint64_t callers[capture_slots]{};
        const auto callers_address = reinterpret_cast<BYTE*>(counters) + sizeof(values);
        const unsigned captures = values[35] < capture_slots ? values[35] : capture_slots;
        if (captures > 0 && ReadProcessMemory(process, callers_address, callers,
                                               captures * sizeof(uint64_t), &transferred) &&
            transferred == captures * sizeof(uint64_t)) {
            for (unsigned index = 0; index < captures; ++index)
                TraceEvent("early_044_caller_rva", static_cast<DWORD>(callers[index]));
        }
        DWORD returned[capture_slots]{};
        const auto returned_address = callers_address + sizeof(callers);
        if (captures > 0 && ReadProcessMemory(process, returned_address, returned,
                                               captures * sizeof(DWORD), &transferred) &&
            transferred == captures * sizeof(DWORD)) {
            for (unsigned index = 0; index < captures; ++index)
                TraceEvent("early_044_bytes_returned", returned[index]);
        }
        const auto sample_address = returned_address + sizeof(returned);
        if (captures > 0 &&
            ReadProcessMemory(process, sample_address, samples, captures * capture_bytes,
                              &transferred) && transferred == captures * capture_bytes) {
            BYTE probe_mac[6]{};
            const bool mac_probe = read_probe_mac(probe_mac);
            for (unsigned sample_index = 0; sample_index < captures; ++sample_index) {
                unsigned printable = 0, zeroes = 0;
                uint64_t hash = 14695981039346656037ull;
                for (BYTE value : samples[sample_index]) {
                    if (value >= 32 && value <= 126) ++printable;
                    if (value == 0) ++zeroes;
                    hash = (hash ^ value) * 1099511628211ull;
                }
                TraceEvent("early_044_printable", printable);
                TraceEvent("early_044_zeroes", zeroes);
                TraceEvent("early_044_hash_low", static_cast<DWORD>(hash));
                TraceEvent("early_044_hash_high", static_cast<DWORD>(hash >> 32));
                if (mac_probe) {
                    TraceEvent("early_044_mac_sample", sample_index);
                    TraceEvent("early_044_mac_match_offset", first_mac_offset(
                        samples[sample_index], returned[sample_index] < capture_bytes ?
                            returned[sample_index] : capture_bytes, probe_mac));
                }
                if (returned[sample_index] == capture_bytes) {
                    DWORD trailer[2]{};
                    memcpy(trailer, samples[sample_index] + capture_bytes - sizeof(trailer),
                           sizeof(trailer));
                    TraceEvent("early_044_trailer_low", trailer[0]);
                    TraceEvent("early_044_trailer_high", trailer[1]);
                    if (GetEnvironmentVariableW(L"PRICECHECK_EARLY_044_BLOCKS", nullptr, 0) > 1) {
                        TraceEvent("early_044_block_sample", sample_index);
                        for (size_t block = 0; block < (capture_bytes + 15) / 16; ++block) {
                            uint64_t block_hash = 14695981039346656037ull;
                            const size_t begin = block * 16;
                            const size_t end = begin + 16 < capture_bytes ? begin + 16 : capture_bytes;
                            for (size_t offset = begin; offset < end; ++offset)
                                block_hash = (block_hash ^ samples[sample_index][offset]) *
                                             1099511628211ull;
                            TraceEvent("early_044_block_hash_low", static_cast<DWORD>(block_hash));
                            TraceEvent("early_044_block_hash_high", static_cast<DWORD>(block_hash >> 32));
                        }
                    }
                }
            }
            for (unsigned first = 0; first < captures; ++first) {
                for (unsigned second = first + 1; second < captures; ++second) {
                    unsigned equal = 0;
                    for (size_t offset = 0; offset < capture_bytes; ++offset)
                        if (samples[first][offset] == samples[second][offset]) ++equal;
                    TraceEvent("early_044_equal_bytes", equal);
                }
            }
        }
        const auto onec_address = sample_address + capture_bytes * capture_slots;
        DWORD onec_status[2]{};
        if (ReadProcessMemory(process, onec_address, onec_status,
                              sizeof(onec_status), &transferred) &&
            transferred == sizeof(onec_status)) {
            TraceEvent("early_01c_captures", onec_status[0]);
            TraceEvent("early_01c_bytes_returned", onec_status[1]);
            if (onec_status[0] > 0) {
                BYTE sample[0xB0]{};
                if (ReadProcessMemory(process, onec_address + sizeof(onec_status),
                                      sample, sizeof(sample), &transferred) &&
                    transferred == sizeof(sample)) {
                    if (GetEnvironmentVariableW(L"PRICECHECK_TEST_01C_RAW_CAPTURE", nullptr, 0) > 1) {
                        wchar_t path[MAX_PATH]{};
                        if (swprintf_s(path, L"C:\\broker\\workspace\\two-client-isolation\\early01c-%lu.bin",
                                       GetCurrentProcessId()) > 0) {
                            HANDLE file = CreateFileW(path, GENERIC_WRITE, 0, nullptr,
                                CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
                            if (file != INVALID_HANDLE_VALUE) {
                                DWORD written = 0;
                                if (WriteFile(file, sample, sizeof(sample), &written, nullptr))
                                    TraceEvent("early_01c_raw_bytes_written", written);
                                CloseHandle(file);
                            }
                        }
                    }
                    unsigned printable = 0, zeroes = 0;
                    uint64_t hash = 14695981039346656037ull;
                    for (BYTE value : sample) {
                        if (value >= 32 && value <= 126) ++printable;
                        if (value == 0) ++zeroes;
                        hash = (hash ^ value) * 1099511628211ull;
                    }
                    TraceEvent("early_01c_printable", printable);
                    TraceEvent("early_01c_zeroes", zeroes);
                    TraceEvent("early_01c_hash_low", static_cast<DWORD>(hash));
                    TraceEvent("early_01c_hash_high", static_cast<DWORD>(hash >> 32));
                    BYTE probe_mac[6]{};
                    if (read_probe_mac(probe_mac))
                        TraceEvent("early_01c_mac_match_offset", first_mac_offset(
                            sample, onec_status[1] < sizeof(sample) ?
                                onec_status[1] : sizeof(sample), probe_mac));
                    for (unsigned block = 0; block < sizeof(sample) / 16; ++block) {
                        uint64_t block_hash = 14695981039346656037ull;
                        for (unsigned offset = 0; offset < 16; ++offset)
                            block_hash = (block_hash ^ sample[block * 16 + offset]) *
                                         1099511628211ull;
                        TraceEvent("early_01c_block_hash_low", static_cast<DWORD>(block_hash));
                        TraceEvent("early_01c_block_hash_high", static_cast<DWORD>(block_hash >> 32));
                        if (block == 8 &&
                            GetEnvironmentVariableW(L"PRICECHECK_EARLY_01C_BLOCK_CAPTURE", nullptr, 0) > 1) {
                            DWORD words[4]{};
                            memcpy(words, sample + block * 16, sizeof(words));
                            for (DWORD word : words)
                                TraceEvent("early_01c_block8_word", word);
                        }
                    }
                }
            }
        }
    }
}
