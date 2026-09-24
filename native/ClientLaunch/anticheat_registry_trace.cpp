#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <intrin.h>
#include <cstdio>
#include <cstring>
#include <cwctype>
#include "minhook/include/MinHook.h"
#include "anticheat_registry_trace.h"
#include "trace.h"

namespace {
using QueryW = LSTATUS(WINAPI*)(HKEY, LPCWSTR, LPDWORD, LPDWORD, LPBYTE, LPDWORD);
using QueryA = LSTATUS(WINAPI*)(HKEY, LPCSTR, LPDWORD, LPDWORD, LPBYTE, LPDWORD);
using GetW = LSTATUS(WINAPI*)(HKEY, LPCWSTR, LPCWSTR, DWORD, LPDWORD, PVOID, LPDWORD);
using GetA = LSTATUS(WINAPI*)(HKEY, LPCSTR, LPCSTR, DWORD, LPDWORD, PVOID, LPDWORD);
using SetW = LSTATUS(WINAPI*)(HKEY, LPCWSTR, DWORD, DWORD, const BYTE*, DWORD);
struct UnicodeString { USHORT length; USHORT maximum_length; PWSTR buffer; };
using NtQueryValue = LONG(NTAPI*)(HANDLE, const UnicodeString*, int, PVOID, ULONG, PULONG);
QueryW real_query = nullptr;
QueryA real_query_a = nullptr;
GetW real_get = nullptr;
GetA real_get_a = nullptr;
SetW real_set = nullptr;
NtQueryValue real_nt_query = nullptr;
bool virtual_ltpad = false;
DWORD replacement_ltpad = 0;
wchar_t replacement_machine_guid[40]{};
DWORD machine_guid_bytes = 0;
char replacement_machine_guid_a[40]{};

bool read_machine_guid() {
    const DWORD length = GetEnvironmentVariableW(L"PRICECHECK_HW_MACHINE_GUID",
        replacement_machine_guid, 40);
    if (length != 36) return false;
    for (DWORD index = 0; index < length; ++index) {
        const wchar_t value = replacement_machine_guid[index];
        if (index == 8 || index == 13 || index == 18 || index == 23) {
            if (value != L'-') return false;
        } else if (!iswxdigit(value)) return false;
    }
    machine_guid_bytes = (length + 1) * sizeof(wchar_t);
    for (DWORD index = 0; index < length; ++index)
        replacement_machine_guid_a[index] = static_cast<char>(replacement_machine_guid[index]);
    replacement_machine_guid_a[length] = 0;
    return true;
}

bool replace_machine_guid(LSTATUS status, LPDWORD type, void* data, LPDWORD size) {
    if (!machine_guid_bytes || status != ERROR_SUCCESS ||
        (type && *type != REG_SZ) || !data || !size ||
        *size < machine_guid_bytes) return false;
    memcpy(data, replacement_machine_guid, machine_guid_bytes);
    *size = machine_guid_bytes;
    return true;
}

bool replace_machine_guid_a(LSTATUS status, LPDWORD type, void* data, LPDWORD size) {
    constexpr DWORD needed = 37;
    if (!machine_guid_bytes || status != ERROR_SUCCESS ||
        (type && *type != REG_SZ) || !data || !size || *size < needed) return false;
    memcpy(data, replacement_machine_guid_a, needed);
    *size = needed;
    return true;
}

bool read_override() {
    wchar_t hex[9]{};
    if (GetEnvironmentVariableW(L"PRICECHECK_AA_LTPAD_HEX", hex, 9) != 8) return false;
    DWORD result = 0;
    for (wchar_t digit : hex) {
        if (!digit) break;
        result <<= 4;
        if (digit >= L'0' && digit <= L'9') result |= digit - L'0';
        else if (digit >= L'A' && digit <= L'F') result |= digit - L'A' + 10;
        else if (digit >= L'a' && digit <= L'f') result |= digit - L'a' + 10;
        else return false;
    }
    replacement_ltpad = result;
    return true;
}

void trace_call(const char* kind, const void* return_address, LSTATUS status) {
    HMODULE module = nullptr;
    if (!GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
        GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCSTR>(return_address), &module)) {
        TraceEvent(kind, static_cast<unsigned>(status));
        return;
    }
    char path[MAX_PATH]{};
    char category[72]{};
    const char* basename = "unknown";
    if (GetModuleFileNameA(module, path, MAX_PATH)) {
        const char* separator = strrchr(path, '\\');
        basename = separator ? separator + 1 : path;
    }
    sprintf_s(category, "aa_%s_%.*s", kind, 47, basename);
    TraceEvent(category, static_cast<unsigned>(status));
    TraceEvent("aa_registry_caller_rva", static_cast<unsigned>(
        reinterpret_cast<uintptr_t>(return_address) - reinterpret_cast<uintptr_t>(module)));
}

LSTATUS WINAPI hooked_query(HKEY key, LPCWSTR name, LPDWORD reserved,
                            LPDWORD type, LPBYTE data, LPDWORD size) {
    const void* return_address = _ReturnAddress();
    LSTATUS status = real_query(key, name, reserved, type, data, size);
    DWORD last_error = GetLastError();
    if (name && _wcsicmp(name, L"ltpad") == 0) {
        if (virtual_ltpad && status == ERROR_SUCCESS && (!type || *type == REG_DWORD) &&
            data && size && *size >= sizeof(DWORD)) {
            memcpy(data, &replacement_ltpad, sizeof(DWORD));
            TraceEvent("aa_query_replaced", 1);
        }
        trace_call("query", return_address, status);
    } else if (name && _wcsicmp(name, L"MachineGuid") == 0) {
        if (replace_machine_guid(status, type, data, size))
            TraceEvent("aa_machine_guid_query_replaced", 1);
        trace_call("machine_guid_query", return_address, status);
    }
    SetLastError(last_error);
    return status;
}

LSTATUS WINAPI hooked_set(HKEY key, LPCWSTR name, DWORD reserved,
                          DWORD type, const BYTE* data, DWORD size) {
    const void* return_address = _ReturnAddress();
    const bool named_ltpad = name && _wcsicmp(name, L"ltpad") == 0;
    const bool intercept = virtual_ltpad && named_ltpad && type == REG_DWORD &&
        data && size == sizeof(DWORD);
    LSTATUS status = intercept ? ERROR_SUCCESS : real_set(key, name, reserved, type, data, size);
    DWORD last_error = intercept ? ERROR_SUCCESS : GetLastError();
    if (named_ltpad) {
        if (intercept) TraceEvent("aa_set_virtual", 1);
        trace_call("set", return_address, status);
    }
    SetLastError(last_error);
    return status;
}

LSTATUS WINAPI hooked_query_a(HKEY key, LPCSTR name, LPDWORD reserved,
                              LPDWORD type, LPBYTE data, LPDWORD size) {
    const void* return_address = _ReturnAddress();
    LSTATUS status = real_query_a(key, name, reserved, type, data, size);
    DWORD last_error = GetLastError();
    if (name && _stricmp(name, "MachineGuid") == 0) {
        if (replace_machine_guid_a(status, type, data, size))
            TraceEvent("aa_machine_guid_query_a_replaced", 1);
        trace_call("machine_guid_query_a", return_address, status);
    }
    SetLastError(last_error);
    return status;
}

LSTATUS WINAPI hooked_get(HKEY key, LPCWSTR subkey, LPCWSTR name, DWORD flags,
                         LPDWORD type, PVOID data, LPDWORD size) {
    const void* return_address = _ReturnAddress();
    LSTATUS status = real_get(key, subkey, name, flags, type, data, size);
    DWORD last_error = GetLastError();
    if (name && _wcsicmp(name, L"ltpad") == 0) {
        if (virtual_ltpad && status == ERROR_SUCCESS && (!type || *type == REG_DWORD) &&
            data && size && *size >= sizeof(DWORD)) {
            memcpy(data, &replacement_ltpad, sizeof(DWORD));
            TraceEvent("aa_get_replaced", 1);
        }
        trace_call("get", return_address, status);
        if (type) TraceEvent("aa_get_type", *type);
        if (size) TraceEvent("aa_get_bytes", *size);
    } else if (name && _wcsicmp(name, L"MachineGuid") == 0) {
        if (replace_machine_guid(status, type, data, size))
            TraceEvent("aa_machine_guid_get_replaced", 1);
        trace_call("machine_guid_get", return_address, status);
    }
    SetLastError(last_error);
    return status;
}

LSTATUS WINAPI hooked_get_a(HKEY key, LPCSTR subkey, LPCSTR name, DWORD flags,
                           LPDWORD type, PVOID data, LPDWORD size) {
    const void* return_address = _ReturnAddress();
    LSTATUS status = real_get_a(key, subkey, name, flags, type, data, size);
    DWORD last_error = GetLastError();
    if (name && _stricmp(name, "MachineGuid") == 0) {
        if (replace_machine_guid_a(status, type, data, size))
            TraceEvent("aa_machine_guid_get_a_replaced", 1);
        trace_call("machine_guid_get_a", return_address, status);
    }
    SetLastError(last_error);
    return status;
}

LONG NTAPI hooked_nt_query(HANDLE key, const UnicodeString* name, int value_class,
                           PVOID data, ULONG capacity, PULONG returned) {
    const void* return_address = _ReturnAddress();
    LONG status = real_nt_query(key, name, value_class, data, capacity, returned);
    DWORD last_error = GetLastError();
    if (name && name->buffer && name->length == 11 * sizeof(wchar_t) &&
        _wcsnicmp(name->buffer, L"MachineGuid", 11) == 0) {
        if (machine_guid_bytes && status == 0 && value_class == 2 && data &&
            capacity >= 12 + machine_guid_bytes && returned &&
            *returned >= 12 + machine_guid_bytes) {
            auto* bytes = static_cast<BYTE*>(data);
            DWORD type = 0, length = 0;
            memcpy(&type, bytes + 4, sizeof(type));
            memcpy(&length, bytes + 8, sizeof(length));
            if (type == REG_SZ && length == machine_guid_bytes) {
                memcpy(bytes + 12, replacement_machine_guid, machine_guid_bytes);
                TraceEvent("aa_machine_guid_nt_replaced", 1);
            }
        }
        trace_call("machine_guid_ntquery", return_address, status);
        TraceEvent("aa_machine_guid_ntclass", static_cast<unsigned>(value_class));
        TraceEvent("aa_machine_guid_ntbytes", returned ? *returned : 0);
    }
    SetLastError(last_error);
    return status;
}
}

bool InstallAnticheatRegistryTraceHooks() {
    virtual_ltpad = read_override();
    TraceEvent("aa_ltpad_virtual", virtual_ltpad ? 1u : 0u);
    TraceEvent("aa_machine_guid_virtual", read_machine_guid() ? 1u : 0u);
    return MH_CreateHookApi(L"advapi32.dll", "RegQueryValueExW", hooked_query,
        reinterpret_cast<void**>(&real_query)) == MH_OK &&
        MH_CreateHookApi(L"advapi32.dll", "RegQueryValueExA", hooked_query_a,
        reinterpret_cast<void**>(&real_query_a)) == MH_OK &&
        MH_CreateHookApi(L"advapi32.dll", "RegGetValueW", hooked_get,
        reinterpret_cast<void**>(&real_get)) == MH_OK &&
        MH_CreateHookApi(L"advapi32.dll", "RegGetValueA", hooked_get_a,
        reinterpret_cast<void**>(&real_get_a)) == MH_OK &&
        MH_CreateHookApi(L"advapi32.dll", "RegSetValueExW", hooked_set,
        reinterpret_cast<void**>(&real_set)) == MH_OK &&
        MH_CreateHookApi(L"ntdll.dll", "NtQueryValueKey", hooked_nt_query,
        reinterpret_cast<void**>(&real_nt_query)) == MH_OK;
}
