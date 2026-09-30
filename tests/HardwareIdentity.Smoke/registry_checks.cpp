#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winternl.h>
#include <string>
#include <iostream>
#include <wbemidl.h>
#include <oleauto.h>
#include "../../native/ClientLaunch/registry_api_identity.h"
#include "../../native/ClientLaunch/monitor_identity.h"
#include "../../native/ClientLaunch/minhook/include/MinHook.h"
#include "../../native/ClientLaunch/wmi_identity.h"
#include "../../native/ClientLaunch/device_identity.h"
#include "../../native/ClientLaunch/identity.h"

static bool MonitorWmiChecks(IWbemLocator* locator) {
    IWbemServices* service = nullptr; IEnumWbemClassObject* values = nullptr;
    BSTR path = SysAllocString(L"ROOT\\WMI");
    HRESULT status = locator->ConnectServer(path, nullptr, nullptr, nullptr, 0, nullptr, nullptr, &service);
    SysFreeString(path);
    if (FAILED(status)) { std::cout << "WMI_MONITOR_UNAVAILABLE\n"; return true; }
    CoSetProxyBlanket(service, RPC_C_AUTHN_WINNT, RPC_C_AUTHZ_NONE, nullptr, RPC_C_AUTHN_LEVEL_CALL,
        RPC_C_IMP_LEVEL_IMPERSONATE, nullptr, EOAC_NONE);
    BSTR language = SysAllocString(L"WQL"), query = SysAllocString(L"SELECT InstanceName,SerialNumberID FROM WmiMonitorID");
    status = service->ExecQuery(language, query, WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY, nullptr, &values);
    SysFreeString(language); SysFreeString(query);
    bool ok = true;
    if (SUCCEEDED(status)) {
        for (;;) {
            IWbemClassObject* item = nullptr; ULONG returned = 0;
            if (FAILED(values->Next(5000, 1, &item, &returned)) || !returned || !item) break;
            VARIANT id{}, serial{}; VariantInit(&id); VariantInit(&serial);
            item->Get(L"InstanceName", 0, &id, nullptr, nullptr); item->Get(L"SerialNumberID", 0, &serial, nullptr, nullptr);
            const VARTYPE element = serial.vt & VT_TYPEMASK;
            if (id.vt != VT_BSTR || !id.bstrVal || !(serial.vt & VT_ARRAY) || !serial.parray ||
                (element != VT_UI2 && element != VT_I4)) ok = false;
            else {
                LONG first = 0, last = -1;
                SafeArrayGetLBound(serial.parray, 1, &first); SafeArrayGetUBound(serial.parray, 1, &last);
                std::cout << "WMI_MONITOR|";
                for (const wchar_t* ch = id.bstrVal; *ch; ++ch) std::cout << static_cast<char>(*ch);
                std::cout << "|" << last - first + 1 << "|";
                for (LONG i = first; i <= last; ++i) {
                    LONG wide = 0; USHORT narrow = 0;
                    if (element == VT_UI2) { SafeArrayGetElement(serial.parray, &i, &narrow); wide = narrow; }
                    else SafeArrayGetElement(serial.parray, &i, &wide);
                    if (!wide) break;
                    std::cout << static_cast<char>(wide);
                }
                std::cout << "\n";
            }
            VariantClear(&id); VariantClear(&serial); item->Release();
        }
        values->Release();
    } else std::cout << "WMI_MONITOR_UNAVAILABLE\n";
    service->Release();
    return ok;
}

static bool WmiChecks(const std::wstring& uuid) {
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    IWbemLocator* locator = nullptr; IWbemServices* service = nullptr;
    IEnumWbemClassObject* values = nullptr;
    bool ok = false;
    if (FAILED(CoCreateInstance(CLSID_WbemLocator, nullptr, CLSCTX_INPROC_SERVER, IID_IWbemLocator,
        reinterpret_cast<void**>(&locator)))) return false;
    BSTR path = SysAllocString(L"ROOT\\CIMV2");
    HRESULT status = locator->ConnectServer(path, nullptr, nullptr, nullptr, 0, nullptr, nullptr, &service);
    SysFreeString(path);
    if (SUCCEEDED(status)) {
        CoSetProxyBlanket(service, RPC_C_AUTHN_WINNT, RPC_C_AUTHZ_NONE, nullptr, RPC_C_AUTHN_LEVEL_CALL,
            RPC_C_IMP_LEVEL_IMPERSONATE, nullptr, EOAC_NONE);
        BSTR language = SysAllocString(L"WQL"); BSTR query = SysAllocString(L"SELECT UUID FROM Win32_ComputerSystemProduct");
        status = service->ExecQuery(language, query, WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY, nullptr, &values);
        SysFreeString(language); SysFreeString(query);
        if (SUCCEEDED(status)) {
            IWbemClassObject* item = nullptr; ULONG returned = 0;
            if (SUCCEEDED(values->Next(5000, 1, &item, &returned)) && returned && item) {
                VARIANT value{}; VariantInit(&value);
                ok = SUCCEEDED(item->Get(L"UUID", 0, &value, nullptr, nullptr)) && value.vt == VT_BSTR && value.bstrVal && uuid == value.bstrVal;
                VariantClear(&value); item->Release();
            }
            values->Release(); values = nullptr;
        }
        language = SysAllocString(L"WQL"); query = SysAllocString(L"SELECT MACAddress,SettingID FROM Win32_NetworkAdapterConfiguration");
        status = service->ExecQuery(language, query, WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY, nullptr, &values);
        SysFreeString(language); SysFreeString(query);
        if (SUCCEEDED(status)) {
            for (;;) {
                IWbemClassObject* item = nullptr; ULONG returned = 0;
                if (FAILED(values->Next(5000, 1, &item, &returned)) || !returned || !item) break;
                VARIANT id{}, mac{}; VariantInit(&id); VariantInit(&mac);
                item->Get(L"SettingID", 0, &id, nullptr, nullptr); item->Get(L"MACAddress", 0, &mac, nullptr, nullptr);
                if (id.vt == VT_BSTR && mac.vt == VT_BSTR && id.bstrVal && mac.bstrVal) {
                    std::wstring record = std::wstring(id.bstrVal) + L"|" + mac.bstrVal;
                    std::cout << "WMI_MAC|";
                    for (wchar_t ch : record) std::cout << static_cast<char>(ch);
                    std::cout << "\n";
                }
                VariantClear(&id); VariantClear(&mac); item->Release();
            }
            values->Release();
        }
        service->Release();
    }
    ok = MonitorWmiChecks(locator) && ok;
    locator->Release(); CoUninitialize();
    return ok;
}

bool RegistryChecks(const std::wstring& uuid) {
    SetEnvironmentVariableW(L"PRICECHECK_HW_MACHINE_GUID", uuid.c_str());
    SetEnvironmentVariableW(L"PRICECHECK_HW_UUID", uuid.c_str());
    SetEnvironmentVariableW(L"PRICECHECK_HW_MAC", L"02AABBCCDDEE");
    SetEnvironmentVariableW(L"PRICECHECK_HW_PROFILE_GUID", (L"{" + uuid + L"}").c_str());
    for (const auto* field : {L"PRICECHECK_HW_SUS_CLIENT_ID", L"PRICECHECK_HW_SQM_MACHINE_ID", L"PRICECHECK_HW_VIDEO_ID", L"PRICECHECK_HW_DISK_GUID"})
        SetEnvironmentVariableW(field, uuid.c_str());
    for (const auto* field : {L"PRICECHECK_HW_BOARD_SERIAL", L"PRICECHECK_HW_SYSTEM_SERIAL", L"PRICECHECK_HW_CHASSIS_SERIAL",
         L"PRICECHECK_HW_PROCESSOR_SERIAL", L"PRICECHECK_HW_MEMORY_SERIAL", L"PRICECHECK_HW_DISK_SERIAL"})
        SetEnvironmentVariableW(field, L"SYNTHETIC12345678");
    SetEnvironmentVariableW(L"PRICECHECK_HW_PROCESSOR_MODEL", L"Synthetic Processor");
    SetEnvironmentVariableW(L"PRICECHECK_HW_PROCESSOR_REVISION", L"1122334455667788");
    SetEnvironmentVariableW(L"PRICECHECK_HW_PROCESSOR_ID", L"1122334455667788");
    SetEnvironmentVariableW(L"PRICECHECK_HW_PRODUCT_ID", L"11111-22222-33333-44444");
    SetEnvironmentVariableW(L"PRICECHECK_HW_COMPUTER_NAME", L"SYNTHETIC-PC");
    SetEnvironmentVariableW(L"PRICECHECK_HW_INSTALL_DATE", L"1710000000");
    SetEnvironmentVariableW(L"PRICECHECK_HW_VOLUME_SERIAL", L"1234ABCD");
    SetEnvironmentVariableW(L"PRICECHECK_HW_DISK_SIGNATURE", L"1234ABCD");
    SetEnvironmentVariableW(L"PRICECHECK_HW_ROUTER_IP", nullptr);
    if (MH_Initialize() != MH_OK || !InstallIdentityHooks() || MH_EnableHook(MH_ALL_HOOKS) != MH_OK || !VerifyIdentityHooks()) {
        std::wcerr << L"identity integration failure: " << IdentityHookError() << L"\n";
        return false;
    }
    HKEY key = nullptr;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Cryptography", 0, KEY_READ | KEY_WOW64_64KEY, &key)) return false;
    wchar_t value[128]{};
    DWORD type = 0, length = sizeof(value);
    bool ok = RegQueryValueExW(key, L"MachineGuid", nullptr, &type, reinterpret_cast<BYTE*>(value), &length) == ERROR_SUCCESS && uuid == value;
    length = sizeof(value);
    ok = ok && RegQueryValueExW(key, L"ProductId", nullptr, &type, reinterpret_cast<BYTE*>(value), &length) == ERROR_FILE_NOT_FOUND;
    length = sizeof(value);
    ok = ok && RegGetValueW(key, nullptr, L"MachineGuid", RRF_RT_REG_SZ, &type, value, &length) == ERROR_SUCCESS && uuid == value;
    length = sizeof(value);
    ok = ok && RegGetValueW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Cryptography", L"MachineGuid", RRF_RT_REG_SZ | RRF_SUBKEY_WOW6464KEY,
        &type, value, &length) == ERROR_SUCCESS && uuid == value;
    char ansi[128]{}; length = sizeof(ansi);
    std::string ascii; for (wchar_t ch : uuid) ascii += static_cast<char>(ch);
    ok = ok && RegGetValueA(key, nullptr, "MachineGuid", RRF_RT_REG_SZ, &type, ansi, &length) == ERROR_SUCCESS && ascii == ansi;
    length = 0;
    ok = ok && RegQueryValueExW(key, L"MachineGuid", nullptr, &type, nullptr, &length) == ERROR_SUCCESS && length == (uuid.size() + 1) * 2;
    using QueryFn = LONG(NTAPI*)(HANDLE, PUNICODE_STRING, int, void*, ULONG, ULONG*);
    auto query = reinterpret_cast<QueryFn>(GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "NtQueryValueKey"));
    wchar_t field[] = L"MachineGuid";
    UNICODE_STRING name{static_cast<USHORT>(wcslen(field) * 2), sizeof(field), field};
    for (int kind : {1, 2, 3, 4}) {
        alignas(8) BYTE data[512]{};
        ULONG returned = 0, offset = 0;
        const LONG status = query(key, &name, kind, data, sizeof(data), &returned);
        if (kind == 1 || kind == 4) memcpy(&offset, data + 8, 4);
        else offset = kind == 2 ? 12 : 8;
        ok = ok && status == 0 && offset < sizeof(data) && uuid == reinterpret_cast<wchar_t*>(data + offset);
    }
    BYTE tiny[4]{}; ULONG needed = 0;
    ok = ok && query(key, &name, 2, tiny, sizeof(tiny), &needed) == static_cast<LONG>(0xC0000023) && needed == 12 + (uuid.size() + 1) * 2;
    bool enumerated = false;
    for (DWORD index = 0; ; ++index) {
        wchar_t fieldName[256]{}; DWORD chars = 256; length = sizeof(value);
        const LONG status = RegEnumValueW(key, index, fieldName, &chars, nullptr, &type, reinterpret_cast<BYTE*>(value), &length);
        if (status == ERROR_NO_MORE_ITEMS) break;
        if (status != ERROR_SUCCESS) { ok = false; break; }
        if (_wcsicmp(fieldName, L"MachineGuid") == 0) { enumerated = true; ok = ok && uuid == value; }
    }
    RegCloseKey(key);
    ok = ok && WmiChecks(uuid);
    MH_DisableHook(MH_ALL_HOOKS); MH_Uninitialize();
    std::cout << (ok && enumerated ? "registry API checks: PASS\n" : "registry API checks: FAIL\n");
    return ok && enumerated;
}
