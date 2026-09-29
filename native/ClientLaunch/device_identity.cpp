#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <setupapi.h>
#include <cfgmgr32.h>
#include <initguid.h>
#include <devpkey.h>
#include <cwctype>
#include <string>
#include <unordered_map>
#include "minhook/include/MinHook.h"
#include "device_identity.h"
#include "trace.h"

namespace {
using SetupIdW = BOOL(WINAPI*)(HDEVINFO, PSP_DEVINFO_DATA, PWSTR, DWORD, PDWORD);
using SetupIdA = BOOL(WINAPI*)(HDEVINFO, PSP_DEVINFO_DATA, PSTR, DWORD, PDWORD);
using SetupPropertyW = BOOL(WINAPI*)(HDEVINFO, PSP_DEVINFO_DATA, const DEVPROPKEY*,
    DEVPROPTYPE*, PBYTE, DWORD, PDWORD, DWORD);
using OpenInfoW = BOOL(WINAPI*)(HDEVINFO, PCWSTR, HWND, DWORD, PSP_DEVINFO_DATA);
using OpenInfoA = BOOL(WINAPI*)(HDEVINFO, PCSTR, HWND, DWORD, PSP_DEVINFO_DATA);
using CmIdW = CONFIGRET(WINAPI*)(DEVINST, PWSTR, ULONG, ULONG);
using CmIdA = CONFIGRET(WINAPI*)(DEVINST, PSTR, ULONG, ULONG);
using CmPropertyW = CONFIGRET(WINAPI*)(DEVINST, const DEVPROPKEY*, DEVPROPTYPE*, PBYTE, PULONG, ULONG);
using CmLocateW = CONFIGRET(WINAPI*)(PDEVINST, DEVINSTID_W, ULONG);
using CmLocateA = CONFIGRET(WINAPI*)(PDEVINST, DEVINSTID_A, ULONG);

SetupIdW real_setup_id_w = nullptr;
SetupIdA real_setup_id_a = nullptr;
SetupPropertyW real_setup_property_w = nullptr;
OpenInfoW real_open_info_w = nullptr;
OpenInfoA real_open_info_a = nullptr;
CmIdW real_cm_id_w = nullptr;
CmIdA real_cm_id_a = nullptr;
CmPropertyW real_cm_property_w = nullptr;
CmLocateW real_cm_locate_w = nullptr;
CmLocateA real_cm_locate_a = nullptr;
std::wstring identity_seed;
std::unordered_map<std::wstring, std::wstring> reverse_ids;
SRWLOCK id_gate = SRWLOCK_INIT;

bool target_id(const std::wstring& id) {
    return (_wcsnicmp(id.c_str(), L"USB\\", 4) == 0 ||
            _wcsnicmp(id.c_str(), L"HID\\", 4) == 0) &&
        id.find(L'\\', 4) != std::wstring::npos;
}

std::wstring to_wide(const char* text) {
    std::wstring result;
    if (text) while (*text) result += static_cast<unsigned char>(*text++);
    return result;
}

std::string to_ascii(const std::wstring& text) {
    std::string result;
    for (wchar_t ch : text) result += static_cast<char>(ch);
    return result;
}

std::wstring normalized_key(std::wstring id) {
    for (auto& ch : id) ch = towupper(ch);
    return id;
}

std::wstring original_id(const std::wstring& requested) {
    AcquireSRWLockShared(&id_gate);
    const auto found = reverse_ids.find(normalized_key(requested));
    const std::wstring result = found == reverse_ids.end() ? requested : found->second;
    ReleaseSRWLockShared(&id_gate);
    return result;
}

bool instance_key(const DEVPROPKEY* key) {
    return key && key->pid == DEVPKEY_Device_InstanceId.pid &&
        IsEqualGUID(key->fmtid, DEVPKEY_Device_InstanceId.fmtid);
}

void map_property(PBYTE data, DWORD bytes) {
    if (!data || bytes < sizeof(wchar_t) || bytes % sizeof(wchar_t)) return;
    const auto* input = reinterpret_cast<const wchar_t*>(data);
    const size_t capacity = bytes / sizeof(wchar_t);
    const size_t length = wcsnlen_s(input, capacity);
    if (length >= capacity) return;
    const auto mapped = MapDeviceInstanceId(std::wstring(input, length));
    if (mapped.size() == length) memcpy(data, mapped.c_str(), (length + 1) * sizeof(wchar_t));
}

BOOL WINAPI hooked_setup_id_w(HDEVINFO set, PSP_DEVINFO_DATA info, PWSTR output,
                               DWORD capacity, PDWORD required) {
    wchar_t original[MAX_DEVICE_ID_LEN]{};
    if (!real_setup_id_w(set, info, original, MAX_DEVICE_ID_LEN, nullptr))
        return real_setup_id_w(set, info, output, capacity, required);
    const auto mapped = MapDeviceInstanceId(original);
    const DWORD needed = static_cast<DWORD>(mapped.size() + 1);
    if (required) *required = needed;
    if (!output || capacity < needed) { SetLastError(ERROR_INSUFFICIENT_BUFFER); return FALSE; }
    memcpy(output, mapped.c_str(), needed * sizeof(wchar_t));
    TraceEvent("setup_device_id", target_id(original) ? 1 : 0);
    return TRUE;
}

BOOL WINAPI hooked_setup_id_a(HDEVINFO set, PSP_DEVINFO_DATA info, PSTR output,
                               DWORD capacity, PDWORD required) {
    char original[MAX_DEVICE_ID_LEN]{};
    if (!real_setup_id_a(set, info, original, MAX_DEVICE_ID_LEN, nullptr))
        return real_setup_id_a(set, info, output, capacity, required);
    const auto mapped = to_ascii(MapDeviceInstanceId(to_wide(original)));
    const DWORD needed = static_cast<DWORD>(mapped.size() + 1);
    if (required) *required = needed;
    if (!output || capacity < needed) { SetLastError(ERROR_INSUFFICIENT_BUFFER); return FALSE; }
    memcpy(output, mapped.c_str(), needed);
    return TRUE;
}

BOOL WINAPI hooked_setup_property_w(HDEVINFO set, PSP_DEVINFO_DATA info, const DEVPROPKEY* key,
                                     DEVPROPTYPE* type, PBYTE data, DWORD capacity,
                                     PDWORD required, DWORD flags) {
    const BOOL result = real_setup_property_w(set, info, key, type, data, capacity, required, flags);
    if (result && instance_key(key) && type && *type == DEVPROP_TYPE_STRING)
        map_property(data, capacity);
    return result;
}

BOOL WINAPI hooked_open_info_w(HDEVINFO set, PCWSTR requested, HWND owner,
                                DWORD flags, PSP_DEVINFO_DATA info) {
    const auto original = requested ? original_id(requested) : std::wstring();
    return real_open_info_w(set, requested ? original.c_str() : nullptr, owner, flags, info);
}

BOOL WINAPI hooked_open_info_a(HDEVINFO set, PCSTR requested, HWND owner,
                                DWORD flags, PSP_DEVINFO_DATA info) {
    const auto original = requested ? to_ascii(original_id(to_wide(requested))) : std::string();
    return real_open_info_a(set, requested ? original.c_str() : nullptr, owner, flags, info);
}

CONFIGRET WINAPI hooked_cm_id_w(DEVINST node, PWSTR output, ULONG capacity, ULONG flags) {
    const CONFIGRET result = real_cm_id_w(node, output, capacity, flags);
    if (result == CR_SUCCESS && output) {
        const auto mapped = MapDeviceInstanceId(output);
        if (mapped.size() + 1 <= capacity) memcpy(output, mapped.c_str(), (mapped.size() + 1) * sizeof(wchar_t));
    }
    return result;
}

CONFIGRET WINAPI hooked_cm_id_a(DEVINST node, PSTR output, ULONG capacity, ULONG flags) {
    const CONFIGRET result = real_cm_id_a(node, output, capacity, flags);
    if (result == CR_SUCCESS && output) {
        const auto mapped = to_ascii(MapDeviceInstanceId(to_wide(output)));
        if (mapped.size() + 1 <= capacity) memcpy(output, mapped.c_str(), mapped.size() + 1);
    }
    return result;
}

CONFIGRET WINAPI hooked_cm_property_w(DEVINST node, const DEVPROPKEY* key, DEVPROPTYPE* type,
                                       PBYTE data, PULONG size, ULONG flags) {
    const CONFIGRET result = real_cm_property_w(node, key, type, data, size, flags);
    if (result == CR_SUCCESS && instance_key(key) && type && *type == DEVPROP_TYPE_STRING && size)
        map_property(data, *size);
    return result;
}

CONFIGRET WINAPI hooked_cm_locate_w(PDEVINST node, DEVINSTID_W requested, ULONG flags) {
    const auto original = requested ? original_id(requested) : std::wstring();
    return real_cm_locate_w(node, requested ? const_cast<PWSTR>(original.c_str()) : nullptr, flags);
}

CONFIGRET WINAPI hooked_cm_locate_a(PDEVINST node, DEVINSTID_A requested, ULONG flags) {
    const auto original = requested ? to_ascii(original_id(to_wide(requested))) : std::string();
    return real_cm_locate_a(node, requested ? const_cast<PSTR>(original.c_str()) : nullptr, flags);
}
} // namespace

std::wstring MapDeviceInstanceId(const std::wstring& original) {
    if (!target_id(original) || identity_seed.empty()) return original;
    AcquireSRWLockShared(&id_gate);
    const bool already_mapped = reverse_ids.find(normalized_key(original)) != reverse_ids.end();
    ReleaseSRWLockShared(&id_gate);
    if (already_mapped) return original;
    const size_t segment = original.find(L'\\', 4) + 1;
    uint64_t hash = 14695981039346656037ull;
    for (wchar_t ch : identity_seed + L"|" + original) {
        hash ^= static_cast<uint16_t>(towupper(ch));
        hash *= 1099511628211ull;
    }
    std::wstring mapped = original;
    constexpr wchar_t hex[] = L"0123456789ABCDEF";
    for (size_t index = segment; index < mapped.size(); ++index) {
        const wchar_t ch = mapped[index];
        if (!((ch >= L'0' && ch <= L'9') || (ch >= L'A' && ch <= L'Z') ||
              (ch >= L'a' && ch <= L'z'))) continue;
        hash ^= hash >> 12;
        hash ^= hash << 25;
        hash ^= hash >> 27;
        mapped[index] = hex[(hash * 2685821657736338717ull) >> 60];
    }
    const auto key = normalized_key(mapped);
    AcquireSRWLockExclusive(&id_gate);
    const auto found = reverse_ids.find(key);
    if (found != reverse_ids.end() && _wcsicmp(found->second.c_str(), original.c_str()) != 0) {
        ReleaseSRWLockExclusive(&id_gate);
        TraceEvent("device_id_collision", 1);
        return original;
    }
    reverse_ids.emplace(key, original);
    ReleaseSRWLockExclusive(&id_gate);
    return mapped;
}

bool InstallDeviceIdentityHooks(const std::wstring& seed) {
    identity_seed = seed;
    if (identity_seed.empty() || !LoadLibraryW(L"setupapi.dll") || !LoadLibraryW(L"cfgmgr32.dll")) return false;
#define ADD(module, name, hook, real) \
    if (MH_CreateHookApi(module, name, hook, reinterpret_cast<void**>(&real)) != MH_OK) return false
    ADD(L"setupapi.dll", "SetupDiGetDeviceInstanceIdW", hooked_setup_id_w, real_setup_id_w);
    ADD(L"setupapi.dll", "SetupDiGetDeviceInstanceIdA", hooked_setup_id_a, real_setup_id_a);
    ADD(L"setupapi.dll", "SetupDiGetDevicePropertyW", hooked_setup_property_w, real_setup_property_w);
    ADD(L"setupapi.dll", "SetupDiOpenDeviceInfoW", hooked_open_info_w, real_open_info_w);
    ADD(L"setupapi.dll", "SetupDiOpenDeviceInfoA", hooked_open_info_a, real_open_info_a);
    ADD(L"cfgmgr32.dll", "CM_Get_Device_IDW", hooked_cm_id_w, real_cm_id_w);
    ADD(L"cfgmgr32.dll", "CM_Get_Device_IDA", hooked_cm_id_a, real_cm_id_a);
    ADD(L"cfgmgr32.dll", "CM_Get_DevNode_PropertyW", hooked_cm_property_w, real_cm_property_w);
    ADD(L"cfgmgr32.dll", "CM_Locate_DevNodeW", hooked_cm_locate_w, real_cm_locate_w);
    ADD(L"cfgmgr32.dll", "CM_Locate_DevNodeA", hooked_cm_locate_a, real_cm_locate_a);
#undef ADD
    return true;
}
