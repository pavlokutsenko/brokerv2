#define WIN32_LEAN_AND_MEAN
#define _WIN32_WINNT 0x0A00
#include <winsock2.h>
#include <windows.h>
#include <iphlpapi.h>
#include <objbase.h>
#include <intrin.h>
#include <cstdint>
#include <cwchar>
#include <string>
#include <algorithm>
#include "minhook/include/MinHook.h"
#include "identity.h"
#include "disk_identity.h"
#include "router_identity.h"
#include "device_identity.h"
#include "wmi_identity.h"
#include "trace.h"

namespace {
using FirmwareFn = UINT(WINAPI*)(DWORD, DWORD, PVOID, DWORD);
using RegistryWFn = LSTATUS(WINAPI*)(HKEY, LPCWSTR, LPDWORD, LPDWORD, LPBYTE, LPDWORD);
using RegistryAFn = LSTATUS(WINAPI*)(HKEY, LPCSTR, LPDWORD, LPDWORD, LPBYTE, LPDWORD);
using AdaptersFn = ULONG(WINAPI*)(ULONG, ULONG, PVOID, PIP_ADAPTER_ADDRESSES, PULONG);
using AdaptersInfoFn = ULONG(WINAPI*)(PIP_ADAPTER_INFO, PULONG);
using VolumeWFn = BOOL(WINAPI*)(LPCWSTR, LPWSTR, DWORD, LPDWORD, LPDWORD, LPDWORD, LPWSTR, DWORD);
using VolumeAFn = BOOL(WINAPI*)(LPCSTR, LPSTR, DWORD, LPDWORD, LPDWORD, LPDWORD, LPSTR, DWORD);
FirmwareFn real_firmware = nullptr;
RegistryWFn real_registry_w = nullptr;
RegistryAFn real_registry_a = nullptr;
AdaptersFn real_adapters = nullptr;
AdaptersInfoFn real_adapters_info = nullptr;
VolumeWFn real_volume_w = nullptr;
VolumeAFn real_volume_a = nullptr;
GUID system_uuid{};
std::wstring machine_guid;
std::string machine_guid_a;
std::string board_serial;
std::string system_serial;
std::string chassis_serial;
std::string processor_serial;
std::wstring processor_model;
std::string processor_model_a;
BYTE processor_revision[8]{};
std::string memory_serial;
std::wstring hw_profile_guid;
std::wstring product_id;
std::string hw_profile_guid_a;
std::string product_id_a;
std::wstring sus_client_id;
std::wstring sqm_machine_id;
std::wstring video_id;
std::wstring computer_name;
std::string sus_client_id_a;
std::string sqm_machine_id_a;
std::string video_id_a;
std::string computer_name_a;
DWORD install_date = 0;
BYTE processor_id[8]{};
BYTE mac[6]{};
DWORD volume_serial = 0;
const wchar_t* failure = L"identity config invalid";

std::wstring env(const wchar_t* name) {
    DWORD size = GetEnvironmentVariableW(name, nullptr, 0);
    if (size == 0 || size > 4096) return {};
    std::wstring result(size, L'\0');
    DWORD copied = GetEnvironmentVariableW(name, result.data(), size);
    if (copied == 0 || copied >= size) return {};
    result.resize(copied);
    return result;
}

std::string ascii(const std::wstring& value) {
    std::string result;
    for (wchar_t ch : value) {
        if (ch < 32 || ch > 126) return {};
        result += static_cast<char>(ch);
    }
    return result;
}

void replace_smbios_string(BYTE* strings, BYTE* end, BYTE index, const std::string& replacement) {
    if (index == 0 || replacement.empty()) return;
    BYTE* value = strings;
    for (BYTE current = 1; current < index && value < end; ++current) {
        while (value < end && *value) ++value;
        ++value;
    }
    if (value >= end || !*value) return;
    BYTE* tail = value;
    while (tail < end && *tail) ++tail;
    for (size_t i = 0; value + i < tail; ++i) value[i] = replacement[i % replacement.size()];
}

bool parse_hex_bytes(const std::wstring& input, BYTE* target, size_t count) {
    if (input.size() != count * 2) return false;
    for (size_t i = 0; i < count; ++i) {
        wchar_t pair[3]{input[i * 2], input[i * 2 + 1], 0};
        wchar_t* end = nullptr;
        unsigned long value = wcstoul(pair, &end, 16);
        if (end != pair + 2 || value > 255) return false;
        target[i] = static_cast<BYTE>(value);
    }
    return true;
}

bool registry_key_ends_with(HKEY key, const wchar_t* suffix) {
    using NtQueryKeyFn = LONG(NTAPI*)(HANDLE, int, void*, ULONG, ULONG*);
    static const HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
    static const auto query = reinterpret_cast<NtQueryKeyFn>(
        ntdll ? GetProcAddress(ntdll, "NtQueryKey") : nullptr);
    if (!query) return false;
    alignas(wchar_t) BYTE buffer[1024]{};
    ULONG returned = 0;
    if (query(key, 3, buffer, sizeof(buffer), &returned) < 0) return false;
    const ULONG name_bytes = *reinterpret_cast<const ULONG*>(buffer);
    if (name_bytes % sizeof(wchar_t) || name_bytes > sizeof(buffer) - sizeof(ULONG)) return false;
    const auto* name = reinterpret_cast<const wchar_t*>(buffer + sizeof(ULONG));
    const size_t count = name_bytes / sizeof(wchar_t);
    const wchar_t* root = L"\\REGISTRY\\MACHINE\\";
    const size_t root_length = wcslen(root);
    const size_t suffix_length = wcslen(suffix);
    return count >= root_length && count >= suffix_length &&
        _wcsnicmp(name, root, root_length) == 0 &&
        _wcsnicmp(name + count - suffix_length, suffix, suffix_length) == 0;
}

void patch_firmware(BYTE* buffer, DWORD size) {
    if (size < 8) return;
    DWORD table_size = *reinterpret_cast<DWORD*>(buffer + 4);
    if (table_size > size - 8) return;
    BYTE* current = buffer + 8;
    BYTE* end = current + table_size;
    while (current + 4 <= end) {
        BYTE type = current[0], length = current[1];
        if (length < 4 || current + length > end) break;
        BYTE* strings = current + length;
        BYTE* next = strings;
        while (next + 1 < end && !(next[0] == 0 && next[1] == 0)) ++next;
        if (next + 1 >= end) break;
        switch (type) {
        case 1:
            if (length >= 24) memcpy(current + 8, &system_uuid, sizeof(system_uuid));
            if (length > 7) replace_smbios_string(strings, next, current[7], system_serial);
            break;
        case 2:
            if (length > 7) replace_smbios_string(strings, next, current[7], board_serial);
            break;
        case 3:
            if (length > 7) replace_smbios_string(strings, next, current[7], chassis_serial);
            break;
        case 4:
            if (length >= 16) memcpy(current + 8, processor_id, sizeof(processor_id));
            if (length > 0x20) replace_smbios_string(strings, next, current[0x20], processor_serial);
            break;
        case 17:
            if (length > 0x18) replace_smbios_string(strings, next, current[0x18], memory_serial);
            break;
        }
        current = next + 2;
        if (type == 127) break;
    }
}

UINT WINAPI hooked_firmware(DWORD provider, DWORD table, PVOID buffer, DWORD size) {
    TraceEvent("firmware_provider", provider);
    if (provider == 'RSMB') {
        const auto* caller = _ReturnAddress();
        HMODULE module = nullptr;
        if (GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
            GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(caller), &module)) {
            wchar_t path[MAX_PATH]{};
            if (GetModuleFileNameW(module, path, MAX_PATH)) {
                const wchar_t* name = wcsrchr(path, L'\\');
                name = name ? name + 1 : path;
                const unsigned kind = _wcsicmp(name, L"clmods64.dll") == 0 ? 1u :
                    _wcsicmp(name, L"lu4.bin") == 0 ? 2u : 3u;
                TraceEvent("firmware_caller_kind", kind);
                TraceEvent("firmware_caller_rva", static_cast<unsigned>(
                    reinterpret_cast<uintptr_t>(caller) - reinterpret_cast<uintptr_t>(module)));
            }
        }
    }
    UINT result = real_firmware(provider, table, buffer, size);
    if (provider == 'RSMB' && table == 0 && buffer && result > 0 && result <= size)
        patch_firmware(static_cast<BYTE*>(buffer), result);
    return result;
}

LSTATUS copy_registry_w(const std::wstring& replacement, LPDWORD type, LPBYTE data, LPDWORD size) {
    if (!size) return ERROR_INVALID_PARAMETER;
    DWORD needed = static_cast<DWORD>((replacement.size() + 1) * sizeof(wchar_t));
    if (type) *type = REG_SZ;
    DWORD capacity = *size;
    *size = needed;
    if (!data) return ERROR_SUCCESS;
    if (capacity < needed) return ERROR_MORE_DATA;
    memcpy(data, replacement.c_str(), needed);
    return ERROR_SUCCESS;
}

LSTATUS copy_registry_dword(LPDWORD type, LPBYTE data, LPDWORD size) {
    if (!size) return ERROR_INVALID_PARAMETER;
    DWORD capacity = *size;
    *size = sizeof(DWORD);
    if (type) *type = REG_DWORD;
    if (!data) return ERROR_SUCCESS;
    if (capacity < sizeof(DWORD)) return ERROR_MORE_DATA;
    memcpy(data, &install_date, sizeof(DWORD));
    return ERROR_SUCCESS;
}

LSTATUS copy_processor_revision(LPDWORD type, LPBYTE data, LPDWORD size) {
    if (!size) return ERROR_INVALID_PARAMETER;
    const DWORD capacity = *size;
    *size = sizeof(processor_revision);
    if (type) *type = REG_BINARY;
    if (!data) return ERROR_SUCCESS;
    if (capacity < sizeof(processor_revision)) return ERROR_MORE_DATA;
    memcpy(data, processor_revision, sizeof(processor_revision));
    return ERROR_SUCCESS;
}

unsigned registry_kind_w(LPCWSTR name) {
    if (!name) return 0;
    const wchar_t* known[] = {L"MachineGuid", L"HwProfileGuid", L"ProductId", L"SusClientId",
        L"VideoIdentifier", L"ComputerName", L"InstallDate", L"MachineId", L"EDID",
        L"HardwareID", L"ProcessorNameString", L"Update Revision", L"BaseBoardVersion",
        L"SystemProductName", L"FriendlyName", L"DeviceDesc"};
    for (unsigned i = 0; i < _countof(known); ++i)
        if (_wcsicmp(name, known[i]) == 0) return i + 1;
    return 0;
}

unsigned registry_kind_a(LPCSTR name) {
    if (!name) return 0;
    const char* known[] = {"MachineGuid", "HwProfileGuid", "ProductId", "SusClientId",
        "VideoIdentifier", "ComputerName", "InstallDate", "MachineId", "EDID",
        "HardwareID", "ProcessorNameString", "Update Revision", "BaseBoardVersion",
        "SystemProductName", "FriendlyName", "DeviceDesc"};
    for (unsigned i = 0; i < _countof(known); ++i)
        if (_stricmp(name, known[i]) == 0) return i + 1;
    return 0;
}

LSTATUS WINAPI hooked_registry_w(HKEY key, LPCWSTR name, LPDWORD reserved, LPDWORD type, LPBYTE data, LPDWORD size) {
    if (unsigned kind = registry_kind_w(name)) TraceEvent("registry_value_w", kind);
    if (name && _wcsicmp(name, L"MachineId") == 0 &&
        registry_key_ends_with(key, L"\\SOFTWARE\\Microsoft\\SQMClient"))
        return copy_registry_w(sqm_machine_id, type, data, size);
    if (name && (_wcsicmp(name, L"ProcessorNameString") == 0 ||
                 _wcsicmp(name, L"Update Revision") == 0) &&
        registry_key_ends_with(key, L"\\HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0")) {
        if (_wcsicmp(name, L"ProcessorNameString") == 0)
            return copy_registry_w(processor_model, type, data, size);
        if (_wcsicmp(name, L"Update Revision") == 0)
            return copy_processor_revision(type, data, size);
    }
    if (name && _wcsicmp(name, L"MachineGuid") == 0) return copy_registry_w(machine_guid, type, data, size);
    if (name && _wcsicmp(name, L"HwProfileGuid") == 0) return copy_registry_w(hw_profile_guid, type, data, size);
    if (name && _wcsicmp(name, L"ProductId") == 0) return copy_registry_w(product_id, type, data, size);
    if (name && _wcsicmp(name, L"SusClientId") == 0) return copy_registry_w(sus_client_id, type, data, size);
    if (name && _wcsicmp(name, L"VideoIdentifier") == 0) return copy_registry_w(video_id, type, data, size);
    if (name && _wcsicmp(name, L"ComputerName") == 0) return copy_registry_w(computer_name, type, data, size);
    if (name && _wcsicmp(name, L"InstallDate") == 0) return copy_registry_dword(type, data, size);
    return real_registry_w(key, name, reserved, type, data, size);
}

LSTATUS WINAPI hooked_registry_a(HKEY key, LPCSTR name, LPDWORD reserved, LPDWORD type, LPBYTE data, LPDWORD size) {
    if (unsigned kind = registry_kind_a(name)) TraceEvent("registry_value_a", kind);
    const std::string* replacement = nullptr;
    if (name && _stricmp(name, "MachineId") == 0 &&
        registry_key_ends_with(key, L"\\SOFTWARE\\Microsoft\\SQMClient"))
        replacement = &sqm_machine_id_a;
    if (name && (_stricmp(name, "ProcessorNameString") == 0 ||
                 _stricmp(name, "Update Revision") == 0) &&
        registry_key_ends_with(key, L"\\HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0")) {
        if (_stricmp(name, "ProcessorNameString") == 0) replacement = &processor_model_a;
        if (_stricmp(name, "Update Revision") == 0)
            return copy_processor_revision(type, data, size);
    }
    if (name && _stricmp(name, "MachineGuid") == 0) replacement = &machine_guid_a;
    if (name && _stricmp(name, "HwProfileGuid") == 0) replacement = &hw_profile_guid_a;
    if (name && _stricmp(name, "ProductId") == 0) replacement = &product_id_a;
    if (name && _stricmp(name, "SusClientId") == 0) replacement = &sus_client_id_a;
    if (name && _stricmp(name, "VideoIdentifier") == 0) replacement = &video_id_a;
    if (name && _stricmp(name, "ComputerName") == 0) replacement = &computer_name_a;
    if (name && _stricmp(name, "InstallDate") == 0) return copy_registry_dword(type, data, size);
    if (replacement) {
        if (!size) return ERROR_INVALID_PARAMETER;
        DWORD needed = static_cast<DWORD>(replacement->size() + 1);
        if (type) *type = REG_SZ;
        DWORD capacity = *size;
        *size = needed;
        if (!data) return ERROR_SUCCESS;
        if (capacity < needed) return ERROR_MORE_DATA;
        memcpy(data, replacement->c_str(), needed);
        return ERROR_SUCCESS;
    }
    return real_registry_a(key, name, reserved, type, data, size);
}

ULONG WINAPI hooked_adapters(ULONG family, ULONG flags, PVOID reserved, PIP_ADAPTER_ADDRESSES adapters, PULONG size) {
    ULONG result = real_adapters(family, flags, reserved, adapters, size);
    TraceEvent("adapters_result", result);
    if (result == NO_ERROR) {
        BYTE index = 0;
        for (auto* current = adapters; current; current = current->Next)
            if (current->PhysicalAddressLength >= 6) {
                memcpy(current->PhysicalAddress, mac, 6);
                current->PhysicalAddress[5] = static_cast<BYTE>(mac[5] + index++);
            }
        TraceEvent("adapters_modified", index);
    }
    return result;
}

ULONG WINAPI hooked_adapters_info(PIP_ADAPTER_INFO adapters, PULONG size) {
    ULONG result = real_adapters_info(adapters, size);
    if (result == NO_ERROR) {
        BYTE index = 0;
        for (auto* current = adapters; current; current = current->Next) {
            if (current->AddressLength < 6) continue;
            memcpy(current->Address, mac, 6);
            current->Address[5] = static_cast<BYTE>(mac[5] + index++);
        }
        TraceEvent("adapter_info_modified", index);
    }
    return result;
}

BOOL WINAPI hooked_volume_w(LPCWSTR root, LPWSTR name, DWORD name_size, LPDWORD serial,
                            LPDWORD max_component, LPDWORD flags, LPWSTR filesystem, DWORD filesystem_size) {
    BOOL result = real_volume_w(root, name, name_size, serial, max_component, flags, filesystem, filesystem_size);
    TraceEvent("volume_w_result", result ? 1 : 0);
    if (result && serial) *serial = volume_serial;
    return result;
}

BOOL WINAPI hooked_volume_a(LPCSTR root, LPSTR name, DWORD name_size, LPDWORD serial,
                            LPDWORD max_component, LPDWORD flags, LPSTR filesystem, DWORD filesystem_size) {
    BOOL result = real_volume_a(root, name, name_size, serial, max_component, flags, filesystem, filesystem_size);
    TraceEvent("volume_a_result", result ? 1 : 0);
    if (result && serial) *serial = volume_serial;
    return result;
}

bool parse_mac(const std::wstring& input) {
    if (input.size() != 12) return false;
    for (size_t i = 0; i < 6; ++i) {
        wchar_t pair[3]{input[i * 2], input[i * 2 + 1], 0};
        wchar_t* end = nullptr;
        unsigned long value = wcstoul(pair, &end, 16);
        if (end != pair + 2 || value > 255) return false;
        mac[i] = static_cast<BYTE>(value);
    }
    return true;
}
} // namespace

bool InstallIdentityHooks() {
    auto uuid_text = env(L"PRICECHECK_HW_UUID");
    machine_guid = env(L"PRICECHECK_HW_MACHINE_GUID");
    machine_guid_a = ascii(machine_guid);
    board_serial = ascii(env(L"PRICECHECK_HW_BOARD_SERIAL"));
    system_serial = ascii(env(L"PRICECHECK_HW_SYSTEM_SERIAL"));
    chassis_serial = ascii(env(L"PRICECHECK_HW_CHASSIS_SERIAL"));
    processor_serial = ascii(env(L"PRICECHECK_HW_PROCESSOR_SERIAL"));
    processor_model = env(L"PRICECHECK_HW_PROCESSOR_MODEL");
    processor_model_a = ascii(processor_model);
    const auto processor_revision_text = env(L"PRICECHECK_HW_PROCESSOR_REVISION");
    memory_serial = ascii(env(L"PRICECHECK_HW_MEMORY_SERIAL"));
    hw_profile_guid = env(L"PRICECHECK_HW_PROFILE_GUID");
    product_id = env(L"PRICECHECK_HW_PRODUCT_ID");
    sus_client_id = env(L"PRICECHECK_HW_SUS_CLIENT_ID");
    sqm_machine_id = env(L"PRICECHECK_HW_SQM_MACHINE_ID");
    sqm_machine_id_a = ascii(sqm_machine_id);
    video_id = env(L"PRICECHECK_HW_VIDEO_ID");
    computer_name = env(L"PRICECHECK_HW_COMPUTER_NAME");
    hw_profile_guid_a = ascii(hw_profile_guid);
    product_id_a = ascii(product_id);
    sus_client_id_a = ascii(sus_client_id);
    video_id_a = ascii(video_id);
    computer_name_a = ascii(computer_name);
    auto install_date_text = env(L"PRICECHECK_HW_INSTALL_DATE");
    auto serial_text = env(L"PRICECHECK_HW_VOLUME_SERIAL");
    if (uuid_text.empty() || machine_guid_a.empty() || board_serial.empty() || serial_text.empty() ||
        system_serial.empty() || chassis_serial.empty() || processor_serial.empty() ||
        processor_model_a.empty() || sqm_machine_id_a.empty() || memory_serial.empty() ||
        hw_profile_guid_a.empty() || product_id_a.empty() || sus_client_id_a.empty() ||
        video_id_a.empty() || computer_name_a.empty() || install_date_text.empty()) return false;
    if (CLSIDFromString((L"{" + uuid_text + L"}").c_str(), &system_uuid) != NOERROR) { failure = L"UUID parse failed"; return false; }
    if (!parse_mac(env(L"PRICECHECK_HW_MAC"))) { failure = L"MAC parse failed"; return false; }
    if (!parse_hex_bytes(env(L"PRICECHECK_HW_PROCESSOR_ID"), processor_id, sizeof(processor_id))) { failure = L"processor ID parse failed"; return false; }
    if (!parse_hex_bytes(processor_revision_text, processor_revision, sizeof(processor_revision))) {
        failure = L"processor revision parse failed"; return false;
    }
    wchar_t* end = nullptr;
    volume_serial = wcstoul(serial_text.c_str(), &end, 16);
    if (!end || *end) { failure = L"volume serial parse failed"; return false; }
    end = nullptr;
    install_date = wcstoul(install_date_text.c_str(), &end, 10);
    if (!end || *end || install_date == 0) { failure = L"install date parse failed"; return false; }
    if (MH_CreateHookApi(L"kernel32.dll", "GetSystemFirmwareTable", hooked_firmware, reinterpret_cast<void**>(&real_firmware)) != MH_OK) { failure = L"GetSystemFirmwareTable"; return false; }
    if (MH_CreateHookApi(L"advapi32.dll", "RegQueryValueExW", hooked_registry_w, reinterpret_cast<void**>(&real_registry_w)) != MH_OK) { failure = L"RegQueryValueExW"; return false; }
    if (MH_CreateHookApi(L"advapi32.dll", "RegQueryValueExA", hooked_registry_a, reinterpret_cast<void**>(&real_registry_a)) != MH_OK) { failure = L"RegQueryValueExA"; return false; }
    if (!LoadLibraryW(L"iphlpapi.dll") ||
        MH_CreateHookApi(L"iphlpapi.dll", "GetAdaptersAddresses", hooked_adapters, reinterpret_cast<void**>(&real_adapters)) != MH_OK) { failure = L"GetAdaptersAddresses"; return false; }
    if (MH_CreateHookApi(L"iphlpapi.dll", "GetAdaptersInfo", hooked_adapters_info,
                         reinterpret_cast<void**>(&real_adapters_info)) != MH_OK) {
        failure = L"GetAdaptersInfo";
        return false;
    }
    if (MH_CreateHookApi(L"kernel32.dll", "GetVolumeInformationW", hooked_volume_w, reinterpret_cast<void**>(&real_volume_w)) != MH_OK) { failure = L"GetVolumeInformationW"; return false; }
    if (MH_CreateHookApi(L"kernel32.dll", "GetVolumeInformationA", hooked_volume_a, reinterpret_cast<void**>(&real_volume_a)) != MH_OK) { failure = L"GetVolumeInformationA"; return false; }
    if (!InstallDiskIdentityHooks()) { failure = L"DeviceIoControl disk identity"; return false; }
    if (!InstallRouterIdentityHooks()) { failure = L"SendARP router identity"; return false; }
    if (!InstallDeviceIdentityHooks(uuid_text)) { failure = L"USB/HID identity hooks"; return false; }
    if (!InstallWmiIdentityHooks()) { failure = L"WMI identity hook"; return false; }
    return true;
}

const wchar_t* IdentityHookError() { return failure; }
#include "identity_verify.inc"
