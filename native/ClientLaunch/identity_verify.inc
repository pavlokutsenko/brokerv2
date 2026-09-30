/* Read back the existing hooks through the public APIs, not configuration flags. */
bool VerifyIdentityHooks() {
    HKEY key = nullptr;
    wchar_t guid[128]{};
    DWORD type = 0, length = sizeof(guid);
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Cryptography", 0,
                     KEY_READ | KEY_WOW64_64KEY, &key) != ERROR_SUCCESS) return false;
    const auto result = RegQueryValueExW(key, L"MachineGuid", nullptr, &type,
                                      reinterpret_cast<BYTE*>(guid), &length);
    RegCloseKey(key);
    if (result != ERROR_SUCCESS || type != REG_SZ || machine_guid != guid) return false;
    DWORD serial = 0;
    if (!GetVolumeInformationW(L"C:\\", nullptr, 0, &serial, nullptr, nullptr, nullptr, 0) ||
        serial != volume_serial) return false;

    const DWORD provider = 0x52534D42;
    const UINT size = GetSystemFirmwareTable(provider, 0, nullptr, 0);
    if (size < 8 || size > 1024 * 1024) return false;
    auto data = static_cast<BYTE*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, size));
    if (!data) return false;
    bool uuid = false;
    if (GetSystemFirmwareTable(provider, 0, data, size) == size) {
        BYTE* cursor = data + 8;
        BYTE* limit = data + size;
        while (cursor + 4 <= limit && cursor[1] >= 4 && cursor + cursor[1] <= limit) {
            if (cursor[0] == 1 && cursor[1] >= 24) {
                uuid = memcmp(cursor + 8, &system_uuid, sizeof(system_uuid)) == 0; break;
            }
            cursor += cursor[1];
            while (cursor + 1 < limit && (cursor[0] || cursor[1])) ++cursor;
            cursor += 2;
        }
    }
    HeapFree(GetProcessHeap(), 0, data);
    if (!uuid) return false;
    ULONG bytes = 16384;
    auto adapters = static_cast<PIP_ADAPTER_ADDRESSES>(HeapAlloc(GetProcessHeap(), 0, bytes));
    if (!adapters) return false;
    ULONG status = GetAdaptersAddresses(AF_UNSPEC, 0, nullptr, adapters, &bytes);
    if (status == ERROR_BUFFER_OVERFLOW && bytes <= 1024 * 1024) {
        HeapFree(GetProcessHeap(), 0, adapters);
        adapters = static_cast<PIP_ADAPTER_ADDRESSES>(HeapAlloc(GetProcessHeap(), 0, bytes));
        if (!adapters) return false;
        status = GetAdaptersAddresses(AF_UNSPEC, 0, nullptr, adapters, &bytes);
    }
    bool network = false;
    if (status == NO_ERROR) for (auto item = adapters; item; item = item->Next)
        if (item->PhysicalAddressLength == 6) {
            BYTE expected[6]{}; MapAdapterMac(mac, item->AdapterName, expected);
            network = memcmp(item->PhysicalAddress, expected, 6) == 0; break;
        }
    HeapFree(GetProcessHeap(), 0, adapters);
    return network;
}
