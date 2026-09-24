#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <objbase.h>
#include <cstdint>
#include <cstring>
#include <string>
#include <vector>
#include "firmware_snapshot.h"

namespace {
std::wstring read_env(const wchar_t* name) {
    DWORD required = GetEnvironmentVariableW(name, nullptr, 0);
    if (!required || required > 129) return {};
    std::wstring result(required, L'\0');
    DWORD copied = GetEnvironmentVariableW(name, result.data(), required);
    if (!copied || copied >= required) return {};
    result.resize(copied);
    return result;
}

std::string ascii_env(const wchar_t* name) {
    std::string result;
    for (wchar_t ch : read_env(name)) {
        if (ch < 32 || ch > 126) return {};
        result.push_back(static_cast<char>(ch));
    }
    return result;
}

bool hex_bytes(const std::wstring& value, BYTE* out, size_t count) {
    if (value.size() != count * 2) return false;
    for (size_t index = 0; index < count; ++index) {
        wchar_t pair[3]{value[index * 2], value[index * 2 + 1], 0};
        wchar_t* end = nullptr;
        unsigned long parsed = wcstoul(pair, &end, 16);
        if (end != pair + 2 || parsed > 255) return false;
        out[index] = static_cast<BYTE>(parsed);
    }
    return true;
}

bool patch_string(BYTE* strings, BYTE* end, BYTE index,
                  const std::string& replacement) {
    if (!index || replacement.empty()) return false;
    BYTE* value = strings;
    for (BYTE current = 1; current < index && value < end; ++current) {
        while (value < end && *value) ++value;
        ++value;
    }
    if (value >= end || !*value) return false;
    BYTE* tail = value;
    while (tail < end && *tail) ++tail;
    for (size_t i = 0; value + i < tail; ++i)
        value[i] = replacement[i % replacement.size()];
    return true;
}
}

bool BuildEarlyFirmwareSnapshot(std::vector<BYTE>& bytes,
                                unsigned& patched_fields) {
    bytes.clear();
    patched_fields = 0;
    GUID uuid{};
    const auto uuid_text = read_env(L"PRICECHECK_HW_UUID");
    if (uuid_text.empty() ||
        CLSIDFromString((L"{" + uuid_text + L"}").c_str(), &uuid) != NOERROR)
        return false;
    BYTE processor_id[8]{};
    if (!hex_bytes(read_env(L"PRICECHECK_HW_PROCESSOR_ID"), processor_id, 8))
        return false;
    const auto system_serial = ascii_env(L"PRICECHECK_HW_SYSTEM_SERIAL");
    const auto board_serial = ascii_env(L"PRICECHECK_HW_BOARD_SERIAL");
    const auto chassis_serial = ascii_env(L"PRICECHECK_HW_CHASSIS_SERIAL");
    const auto processor_serial = ascii_env(L"PRICECHECK_HW_PROCESSOR_SERIAL");
    const auto memory_serial = ascii_env(L"PRICECHECK_HW_MEMORY_SERIAL");
    if (system_serial.empty() || board_serial.empty() || chassis_serial.empty() ||
        processor_serial.empty() || memory_serial.empty()) return false;

    UINT required = GetSystemFirmwareTable('RSMB', 0, nullptr, 0);
    if (required < 8 || required > 1024 * 1024) return false;
    bytes.resize(required);
    if (GetSystemFirmwareTable('RSMB', 0, bytes.data(), required) != required) {
        bytes.clear();
        return false;
    }
    DWORD table_length = 0;
    memcpy(&table_length, bytes.data() + 4, sizeof(table_length));
    if (table_length > required - 8) {
        bytes.clear();
        return false;
    }
    BYTE* current = bytes.data() + 8;
    BYTE* end = current + table_length;
    while (current + 4 <= end) {
        const BYTE type = current[0];
        const BYTE length = current[1];
        if (length < 4 || current + length > end) break;
        BYTE* strings = current + length;
        BYTE* next = strings;
        while (next + 1 < end && !(next[0] == 0 && next[1] == 0)) ++next;
        if (next + 1 >= end) break;
        switch (type) {
        case 1:
            if (length >= 24) {
                memcpy(current + 8, &uuid, sizeof(uuid));
                ++patched_fields;
            }
            if (length > 7 && patch_string(strings, next, current[7], system_serial))
                ++patched_fields;
            break;
        case 2:
            if (length > 7 && patch_string(strings, next, current[7], board_serial))
                ++patched_fields;
            break;
        case 3:
            if (length > 7 && patch_string(strings, next, current[7], chassis_serial))
                ++patched_fields;
            break;
        case 4:
            if (length >= 16) {
                memcpy(current + 8, processor_id, sizeof(processor_id));
                ++patched_fields;
            }
            if (length > 0x20 && patch_string(strings, next,
                                               current[0x20], processor_serial))
                ++patched_fields;
            break;
        case 17:
            if (length > 0x18 && patch_string(strings, next,
                                               current[0x18], memory_serial))
                ++patched_fields;
            break;
        }
        current = next + 2;
        if (type == 127) break;
    }
    return patched_fields >= 2;
}
