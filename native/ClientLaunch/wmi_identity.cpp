#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <wbemidl.h>
#include <oleauto.h>
#include <string>
#include "minhook/include/MinHook.h"
#include "device_identity.h"
#include "trace.h"
#include "wmi_identity.h"
#include "monitor_identity.h"

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
        if (_wcsicmp(property, L"SerialNumber") == 0)
            return repeat_to_length(env(L"PRICECHECK_HW_PROCESSOR_SERIAL"), original.size());
        if (_wcsicmp(property, L"ProcessorId") == 0) return env(L"PRICECHECK_HW_PROCESSOR_ID");
        if (_wcsicmp(property, L"Name") == 0) return env(L"PRICECHECK_HW_PROCESSOR_MODEL");
    }
    if (_wcsicmp(category.c_str(), L"Win32_SystemEnclosure") == 0 && _wcsicmp(property, L"SerialNumber") == 0)
        return repeat_to_length(env(L"PRICECHECK_HW_CHASSIS_SERIAL"), original.size());
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
    if (FAILED(result) || !name || !value ||
        _wcsicmp(name, L"__CLASS") == 0) return result;
    VARIANT class_value{};
    VariantInit(&class_value);
    const HRESULT class_result = real_get_property(object, L"__CLASS", 0, &class_value, nullptr, nullptr);
    if (SUCCEEDED(class_result) && class_value.vt == VT_BSTR && class_value.bstrVal) {
        const std::wstring category = class_value.bstrVal;
        if (_wcsicmp(name, L"MACAddress") == 0 && value->vt == VT_BSTR && value->bstrVal &&
            (_wcsicmp(category.c_str(), L"Win32_NetworkAdapterConfiguration") == 0 ||
             _wcsicmp(category.c_str(), L"Win32_NetworkAdapter") == 0)) {
            VARIANT id{}; VariantInit(&id);
            const auto field = _wcsicmp(category.c_str(), L"Win32_NetworkAdapterConfiguration") == 0 ? L"SettingID" : L"GUID";
            if (SUCCEEDED(real_get_property(object, field, 0, &id, nullptr, nullptr)) && id.vt == VT_BSTR && id.bstrVal) {
                const auto seed = env(L"PRICECHECK_HW_MAC");
                if (seed.size() == 12) {
                    BYTE bytes[6]{}, mapped[6]{};
                    for (unsigned i = 0; i < 6; ++i) bytes[i] = static_cast<BYTE>(wcstoul(seed.substr(i * 2, 2).c_str(), nullptr, 16));
                    std::string adapter;
                    for (const wchar_t* ch = id.bstrVal; *ch && *ch <= 127; ++ch) adapter += static_cast<char>(*ch);
                    if (!adapter.empty()) {
                        MapAdapterMac(bytes, adapter.c_str(), mapped);
                        wchar_t text[18]{};
                        swprintf_s(text, L"%02X:%02X:%02X:%02X:%02X:%02X", mapped[0], mapped[1], mapped[2], mapped[3], mapped[4], mapped[5]);
                        BSTR substitute = SysAllocString(text);
                        if (substitute) { VariantClear(value); value->vt = VT_BSTR; value->bstrVal = substitute; }
                    }
                }
            }
            VariantClear(&id);
        }
        if (_wcsicmp(category.c_str(), L"WmiMonitorID") == 0 && _wcsicmp(name, L"SerialNumberID") == 0 &&
            (value->vt == (VT_ARRAY | VT_UI2) || value->vt == (VT_ARRAY | VT_I4)) &&
            value->parray && SafeArrayGetDim(value->parray) == 1) {
            VARIANT id{}; VariantInit(&id);
            if (SUCCEEDED(real_get_property(object, L"InstanceName", 0, &id, nullptr, nullptr)) && id.vt == VT_BSTR && id.bstrVal) {
                std::wstring instance = id.bstrVal;
                if (_wcsnicmp(instance.c_str(), L"DISPLAY\\", 8) == 0) instance.erase(0, 8);
                const auto marker = instance.rfind(L'_');
                if (marker != std::wstring::npos && marker + 1 < instance.size() &&
                    instance.find_first_not_of(L"0123456789", marker + 1) == std::wstring::npos)
                    instance.resize(marker);
                const auto serial = MonitorSerialText(env(L"PRICECHECK_HW_UUID"), instance);
                LONG first = 0, last = -1;
                if (SUCCEEDED(SafeArrayGetLBound(value->parray, 1, &first)) && SUCCEEDED(SafeArrayGetUBound(value->parray, 1, &last)) &&
                    last >= first && last - first < 256) {
                    const VARTYPE originalType = value->vt;
                    const VARTYPE elementType = originalType & VT_TYPEMASK;
                    SAFEARRAY* array = SafeArrayCreateVector(elementType, first, last - first + 1);
                    void* data = nullptr;
                    if (array && SUCCEEDED(SafeArrayAccessData(array, &data))) {
                        for (LONG i = 0; i <= last - first; ++i) {
                            const auto ch = i < last - first && static_cast<size_t>(i) < serial.size() ? serial[i] : 0;
                            if (elementType == VT_UI2) static_cast<USHORT*>(data)[i] = static_cast<USHORT>(ch);
                            else static_cast<LONG*>(data)[i] = ch;
                        }
                        SafeArrayUnaccessData(array);
                        VariantClear(value); value->vt = originalType; value->parray = array;
                    } else if (array) SafeArrayDestroy(array);
                }
            }
            VariantClear(&id);
        }
        if (value->vt != VT_BSTR || !value->bstrVal) { VariantClear(&class_value); return result; }
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
