#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <wbemidl.h>
#include <oleauto.h>
#include <string>
#include "minhook/include/MinHook.h"
#include "device_identity.h"
#include "trace.h"
#include "wmi_identity.h"

namespace {
using GetPropertyFn = HRESULT(STDMETHODCALLTYPE*)(IWbemClassObject*, LPCWSTR, long,
    VARIANT*, CIMTYPE*, long*);
GetPropertyFn real_get_property = nullptr;

std::wstring env(const wchar_t* name) {
    const DWORD length = GetEnvironmentVariableW(name, nullptr, 0);
    if (!length || length > 4096) return {};
    std::wstring result(length, L'\0');
    const DWORD copied = GetEnvironmentVariableW(name, result.data(), length);
    if (!copied || copied >= length) return {};
    result.resize(copied);
    return result;
}

std::wstring repeat_to_length(const std::wstring& seed, size_t length) {
    if (seed.empty() || !length) return {};
    std::wstring result;
    result.reserve(length);
    for (size_t i = 0; i < length; ++i) result += seed[i % seed.size()];
    return result;
}

std::wstring replacement(const std::wstring& category, LPCWSTR property, const std::wstring& original) {
    if (_wcsicmp(property, L"PNPDeviceID") == 0 || _wcsicmp(property, L"DeviceID") == 0)
        return MapDeviceInstanceId(original);
    if (_wcsicmp(category.c_str(), L"Win32_ComputerSystemProduct") == 0) {
        if (_wcsicmp(property, L"UUID") == 0) return env(L"PRICECHECK_HW_UUID");
        if (_wcsicmp(property, L"IdentifyingNumber") == 0)
            return repeat_to_length(env(L"PRICECHECK_HW_SYSTEM_SERIAL"), original.size());
    }
    if (_wcsicmp(category.c_str(), L"Win32_BaseBoard") == 0 &&
        _wcsicmp(property, L"SerialNumber") == 0)
        return repeat_to_length(env(L"PRICECHECK_HW_BOARD_SERIAL"), original.size());
    if (_wcsicmp(category.c_str(), L"Win32_Processor") == 0) {
        if (_wcsicmp(property, L"ProcessorId") == 0) return env(L"PRICECHECK_HW_PROCESSOR_ID");
        if (_wcsicmp(property, L"Name") == 0) return env(L"PRICECHECK_HW_PROCESSOR_MODEL");
    }
    if (_wcsicmp(category.c_str(), L"Win32_PhysicalMemory") == 0 &&
        _wcsicmp(property, L"SerialNumber") == 0)
        return repeat_to_length(env(L"PRICECHECK_HW_MEMORY_SERIAL"), original.size());
    if (_wcsicmp(category.c_str(), L"Win32_DiskDrive") == 0 &&
        _wcsicmp(property, L"SerialNumber") == 0)
        return repeat_to_length(env(L"PRICECHECK_HW_DISK_SERIAL"), original.size());
    if (_wcsicmp(category.c_str(), L"Win32_LogicalDisk") == 0 &&
        _wcsicmp(property, L"VolumeSerialNumber") == 0)
        return env(L"PRICECHECK_HW_VOLUME_SERIAL");
    if (_wcsicmp(category.c_str(), L"Win32_ComputerSystem") == 0 &&
        _wcsicmp(property, L"Name") == 0)
        return env(L"PRICECHECK_HW_COMPUTER_NAME");
    return original;
}

HRESULT STDMETHODCALLTYPE hooked_get_property(IWbemClassObject* object, LPCWSTR name, long flags,
                                               VARIANT* value, CIMTYPE* type, long* flavor) {
    const HRESULT result = real_get_property(object, name, flags, value, type, flavor);
    if (FAILED(result) || !name || !value || value->vt != VT_BSTR || !value->bstrVal ||
        _wcsicmp(name, L"__CLASS") == 0) return result;
    VARIANT class_value{};
    VariantInit(&class_value);
    const HRESULT class_result = real_get_property(object, L"__CLASS", 0, &class_value, nullptr, nullptr);
    if (SUCCEEDED(class_result) && class_value.vt == VT_BSTR && class_value.bstrVal) {
        const std::wstring original(value->bstrVal, SysStringLen(value->bstrVal));
        const auto mapped = replacement(class_value.bstrVal, name, original);
        if (!mapped.empty() && mapped != original) {
            BSTR substitute = SysAllocStringLen(mapped.data(), static_cast<UINT>(mapped.size()));
            if (substitute) {
                VariantClear(value);
                value->vt = VT_BSTR;
                value->bstrVal = substitute;
                TraceEvent("wmi_property_mapped", 1);
            }
        }
    }
    VariantClear(&class_value);
    return result;
}
} // namespace

bool InstallWmiIdentityHooks() {
    const HRESULT apartment = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(apartment) && apartment != RPC_E_CHANGED_MODE) return false;
    IWbemLocator* locator = nullptr;
    IWbemServices* services = nullptr;
    IEnumWbemClassObject* enumerator = nullptr;
    IWbemClassObject* sample = nullptr;
    bool installed = false;
    do {
        if (FAILED(CoCreateInstance(CLSID_WbemLocator, nullptr, CLSCTX_INPROC_SERVER,
                                    IID_IWbemLocator, reinterpret_cast<void**>(&locator)))) break;
        BSTR path = SysAllocString(L"ROOT\\CIMV2");
        const HRESULT connected = locator->ConnectServer(path, nullptr, nullptr, nullptr,
                                                          0, nullptr, nullptr, &services);
        SysFreeString(path);
        if (FAILED(connected)) break;
        CoSetProxyBlanket(services, RPC_C_AUTHN_WINNT, RPC_C_AUTHZ_NONE, nullptr,
                          RPC_C_AUTHN_LEVEL_CALL, RPC_C_IMP_LEVEL_IMPERSONATE, nullptr, EOAC_NONE);
        BSTR language = SysAllocString(L"WQL");
        BSTR query = SysAllocString(L"SELECT UUID FROM Win32_ComputerSystemProduct");
        const HRESULT queried = services->ExecQuery(language, query,
                                                    WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
                                                    nullptr, &enumerator);
        SysFreeString(language);
        SysFreeString(query);
        if (FAILED(queried) || !enumerator) break;
        ULONG returned = 0;
        if (FAILED(enumerator->Next(3000, 1, &sample, &returned)) || returned != 1 || !sample) break;
        auto** vtable = *reinterpret_cast<void***>(sample);
        HMODULE pinned = nullptr;
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                                reinterpret_cast<LPCWSTR>(vtable[4]), &pinned)) break;
        installed = MH_CreateHook(vtable[4], hooked_get_property,
                                  reinterpret_cast<void**>(&real_get_property)) == MH_OK;
    } while (false);
    if (sample) sample->Release();
    if (enumerator) enumerator->Release();
    if (services) services->Release();
    if (locator) locator->Release();
    if (SUCCEEDED(apartment)) CoUninitialize();
    TraceEvent("wmi_hook_installed", installed ? 1 : 0);
    return installed;
}
