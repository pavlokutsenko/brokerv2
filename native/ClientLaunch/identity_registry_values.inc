// NT registry hooks own the path-scoped replacement and buffer-size handling.
// Keep these Win32 wrappers for the existing trace categories.
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
    const DWORD previous = GetLastError();
    if (unsigned kind = registry_kind_w(name)) TraceEvent("registry_value_w", kind);
    SetLastError(previous);
    return real_registry_w(key, name, reserved, type, data, size);
}

LSTATUS WINAPI hooked_registry_a(HKEY key, LPCSTR name, LPDWORD reserved, LPDWORD type, LPBYTE data, LPDWORD size) {
    const DWORD previous = GetLastError();
    if (unsigned kind = registry_kind_a(name)) TraceEvent("registry_value_a", kind);
    SetLastError(previous);
    return real_registry_a(key, name, reserved, type, data, size);
}
