#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winternl.h>
#include <string>
#include <vector>
#include <cstring>
#include "minhook/include/MinHook.h"
#include "monitor_identity.h"
#include "registry_api_identity.h"

namespace {
using QueryFn = LONG(NTAPI*)(HANDLE, PUNICODE_STRING, int, void*, ULONG, ULONG*);
using EnumerateFn = LONG(NTAPI*)(HANDLE, ULONG, int, void*, ULONG, ULONG*);
QueryFn real_query = nullptr;
EnumerateFn real_enumerate = nullptr;
constexpr LONG small = static_cast<LONG>(0xC0000023);
constexpr LONG overflow = static_cast<LONG>(0x80000005);

std::wstring env(const wchar_t* name) {
    DWORD count = GetEnvironmentVariableW(name, nullptr, 0);
    if (!count || count > 4096) return {};
    std::wstring text(count, L'\0');
    DWORD copied = GetEnvironmentVariableW(name, text.data(), count);
    if (!copied || copied >= count) return {};
    text.resize(copied); return text;
}

std::wstring key_path(HKEY key) {
    using QueryKeyFn = LONG(NTAPI*)(HANDLE, int, void*, ULONG, ULONG*);
    static auto query = reinterpret_cast<QueryKeyFn>(GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "NtQueryKey"));
    alignas(wchar_t) BYTE buffer[4096]{};
    ULONG returned = 0;
    if (!query || query(key, 3, buffer, sizeof(buffer), &returned) < 0) return {};
    const ULONG bytes = *reinterpret_cast<ULONG*>(buffer);
    if (bytes % 2 || bytes > sizeof(buffer) - 4) return {};
    std::wstring result(reinterpret_cast<wchar_t*>(buffer + 4), bytes / 2);
    for (auto& ch : result) if (ch >= L'A' && ch <= L'Z') ch += 32;
    return result;
}

bool replacement(HKEY key, const std::wstring& name, DWORD& type, std::vector<BYTE>& data) {
    const wchar_t* known[] = { L"MachineGuid", L"HwProfileGuid", L"ProductId", L"SusClientId", L"MachineId",
        L"VideoIdentifier", L"ComputerName", L"ProcessorNameString", L"InstallDate", L"Update Revision" };
    bool matched = false;
    for (const auto* field : known) if (_wcsicmp(name.c_str(), field) == 0) { matched = true; break; }
    if (!matched) return false;
    const auto path = key_path(key);
    const wchar_t* variable = nullptr;
    if (_wcsicmp(name.c_str(), L"MachineGuid") == 0 &&
        path == L"\\registry\\machine\\software\\microsoft\\cryptography") variable = L"PRICECHECK_HW_MACHINE_GUID";
    else if (_wcsicmp(name.c_str(), L"HwProfileGuid") == 0 &&
        path.find(L"\\hardware profiles\\") != std::wstring::npos &&
        path.find(L"\\registry\\machine\\system\\") == 0) variable = L"PRICECHECK_HW_PROFILE_GUID";
    else if (_wcsicmp(name.c_str(), L"ProductId") == 0 &&
        path.find(L"\\microsoft\\windows nt\\currentversion") != std::wstring::npos) variable = L"PRICECHECK_HW_PRODUCT_ID";
    else if (_wcsicmp(name.c_str(), L"SusClientId") == 0 &&
        path.find(L"\\windowsupdate") != std::wstring::npos) variable = L"PRICECHECK_HW_SUS_CLIENT_ID";
    else if (_wcsicmp(name.c_str(), L"MachineId") == 0 &&
        path.find(L"\\microsoft\\sqmclient") != std::wstring::npos) variable = L"PRICECHECK_HW_SQM_MACHINE_ID";
    else if (_wcsicmp(name.c_str(), L"VideoIdentifier") == 0 &&
        path.find(L"\\control\\video\\") != std::wstring::npos) variable = L"PRICECHECK_HW_VIDEO_ID";
    else if (_wcsicmp(name.c_str(), L"ComputerName") == 0 &&
        path.find(L"\\control\\computername\\") != std::wstring::npos) variable = L"PRICECHECK_HW_COMPUTER_NAME";
    else if (_wcsicmp(name.c_str(), L"ProcessorNameString") == 0 &&
        path.find(L"\\hardware\\description\\system\\centralprocessor\\") != std::wstring::npos) variable = L"PRICECHECK_HW_PROCESSOR_MODEL";
    if (variable) {
        const auto text = env(variable);
        if (text.empty()) return false;
        type = REG_SZ;
        const auto* bytes = reinterpret_cast<const BYTE*>(text.c_str());
        data.assign(bytes, bytes + (text.size() + 1) * 2); return true;
    }
    if (_wcsicmp(name.c_str(), L"InstallDate") == 0 &&
        path.find(L"\\microsoft\\windows nt\\currentversion") != std::wstring::npos) {
        const auto text = env(L"PRICECHECK_HW_INSTALL_DATE");
        if (text.empty()) return false;
        const DWORD value = wcstoul(text.c_str(), nullptr, 10);
        type = REG_DWORD; data.resize(4); memcpy(data.data(), &value, 4); return true;
    }
    if (_wcsicmp(name.c_str(), L"Update Revision") == 0 &&
        path.find(L"\\hardware\\description\\system\\centralprocessor\\") != std::wstring::npos) {
        const auto text = env(L"PRICECHECK_HW_PROCESSOR_REVISION");
        if (text.size() != 16) return false;
        type = REG_BINARY; data.resize(8);
        for (unsigned i = 0; i < 8; ++i) data[i] = static_cast<BYTE>(wcstoul(text.substr(i * 2, 2).c_str(), nullptr, 16));
        return true;
    }
    return false;
}

LONG map_result(HKEY key, const std::wstring& name, int kind, void* buffer, ULONG capacity, ULONG* returned, LONG status) {
    if (status != 0 && status != small && status != overflow) return status;
    DWORD type = 0;
    std::vector<BYTE> data;
    if (replacement(key, name, type, data) && returned) {
        ULONG offset = 0;
        std::vector<BYTE> result;
        if (kind == 2 || kind == 3) {
            offset = kind == 2 ? 12 : 8; result.resize(offset + data.size());
            memcpy(result.data() + (kind == 2 ? 4 : 0), &type, 4);
            const ULONG count = static_cast<ULONG>(data.size());
            memcpy(result.data() + (kind == 2 ? 8 : 4), &count, 4);
        } else if (kind == 1 || kind == 4) {
            const ULONG name_size = static_cast<ULONG>(name.size() * 2);
            offset = (20 + name_size + (kind == 4 ? 7 : 3)) & (kind == 4 ? ~7u : ~3u);
            result.resize(offset + data.size());
            const ULONG count = static_cast<ULONG>(data.size());
            memcpy(result.data() + 4, &type, 4); memcpy(result.data() + 8, &offset, 4);
            memcpy(result.data() + 12, &count, 4); memcpy(result.data() + 16, &name_size, 4);
            memcpy(result.data() + 20, name.data(), name_size);
        } else return status;
        memcpy(result.data() + offset, data.data(), data.size());
        *returned = static_cast<ULONG>(result.size());
        if (buffer && capacity) memcpy(buffer, result.data(), min(capacity, *returned));
        return capacity >= *returned ? 0 : capacity < offset ? small : overflow;
    }
    if (status == 0 && _wcsicmp(name.c_str(), L"EDID") == 0 && buffer && returned && *returned <= capacity) {
        auto* bytes = static_cast<BYTE*>(buffer);
        ULONG offset = 0, length = 0;
        if (kind == 2 && capacity >= 12) { memcpy(&type, bytes + 4, 4); memcpy(&length, bytes + 8, 4); offset = 12; }
        else if (kind == 3 && capacity >= 8) { memcpy(&type, bytes, 4); memcpy(&length, bytes + 4, 4); offset = 8; }
        else if ((kind == 1 || kind == 4) && capacity >= 20) {
            memcpy(&type, bytes + 4, 4); memcpy(&offset, bytes + 8, 4); memcpy(&length, bytes + 12, 4);
        }
        if (offset <= capacity && length <= capacity - offset)
            PatchMonitorRegistryValue(key, L"EDID", type, bytes + offset, length);
    }
    return status;
}

LONG NTAPI hooked_query(HANDLE key, PUNICODE_STRING name, int kind, void* buffer, ULONG capacity, ULONG* returned) {
    const LONG status = real_query(key, name, kind, buffer, capacity, returned);
    const DWORD last_error = GetLastError();
    if (!name || !name->Buffer || name->Length > 128 || name->Length % 2) return status;
    const LONG result = map_result(static_cast<HKEY>(key), std::wstring(name->Buffer, name->Length / 2), kind, buffer, capacity, returned, status);
    SetLastError(last_error); return result;
}
LONG NTAPI hooked_enumerate(HANDLE key, ULONG index, int kind, void* buffer, ULONG capacity, ULONG* returned) {
    const LONG status = real_enumerate(key, index, kind, buffer, capacity, returned);
    if (status || (kind != 1 && kind != 4) || !buffer || capacity < 20) return status;
    ULONG length = 0; memcpy(&length, static_cast<BYTE*>(buffer) + 16, 4);
    if (length % 2 || length > 128 || length > capacity - 20) return status;
    const auto name = std::wstring(reinterpret_cast<wchar_t*>(static_cast<BYTE*>(buffer) + 20), length / 2);
    const DWORD last_error = GetLastError();
    const LONG result = map_result(static_cast<HKEY>(key), name, kind, buffer, capacity, returned, status);
    SetLastError(last_error); return result;
}
} // namespace

bool InstallRegistryApiIdentityHooks() {
    return MH_CreateHookApi(L"ntdll.dll", "NtQueryValueKey", hooked_query, reinterpret_cast<void**>(&real_query)) == MH_OK &&
        MH_CreateHookApi(L"ntdll.dll", "NtEnumerateValueKey", hooked_enumerate, reinterpret_cast<void**>(&real_enumerate)) == MH_OK;
}
