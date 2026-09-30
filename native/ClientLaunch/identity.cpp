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
#include "monitor_identity.h"
#include "registry_api_identity.h"
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

#include "identity_registry_values.inc"

ULONG WINAPI hooked_adapters(ULONG family, ULONG flags, PVOID reserved, PIP_ADAPTER_ADDRESSES adapters, PULONG size) {
    ULONG result = real_adapters(family, flags, reserved, adapters, size);
    TraceEvent("adapters_result", result);
    if (result == NO_ERROR) {
        ULONG index = 0;
        for (auto* current = adapters; current; current = current->Next)
            if (current->PhysicalAddressLength == 6) {
                MapAdapterMac(mac, current->AdapterName, current->PhysicalAddress); ++index;
            }
        TraceEvent("adapters_modified", index);
    }
    return result;
}

ULONG WINAPI hooked_adapters_info(PIP_ADAPTER_INFO adapters, PULONG size) {
    ULONG result = real_adapters_info(adapters, size);
    if (result == NO_ERROR) {
        ULONG index = 0;
        for (auto* current = adapters; current; current = current->Next) {
            if (current->AddressLength != 6) continue;
            MapAdapterMac(mac, current->AdapterName, current->Address); ++index;
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
    if (!InitializeMonitorIdentity(uuid_text)) { failure = L"monitor identity seed"; return false; }
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
    if (!InstallRegistryApiIdentityHooks()) { failure = L"native registry identity hooks"; return false; }
    if (!InstallDiskIdentityHooks()) { failure = L"DeviceIoControl disk identity"; return false; }
    if (!InstallRouterIdentityHooks()) { failure = L"SendARP router identity"; return false; }
    if (!InstallDeviceIdentityHooks(uuid_text)) { failure = L"USB/HID identity hooks"; return false; }
    if (!InstallWmiIdentityHooks()) { failure = L"WMI identity hook"; return false; }
    return true;
}

const wchar_t* IdentityHookError() { return failure; }
#include "identity_verify.inc"
