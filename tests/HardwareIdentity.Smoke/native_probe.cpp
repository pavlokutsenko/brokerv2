#include <windows.h>
#include <string>
#include <vector>
#include <iostream>
#include "../../native/ClientLaunch/monitor_identity.h"
namespace KernelEdidProbe {
static SIZE_T Compare(const void* first, const void* second, SIZE_T count) {
    const auto* a = static_cast<const BYTE*>(first); const auto* b = static_cast<const BYTE*>(second);
    SIZE_T same = 0; while (same < count && a[same] == b[same]) ++same; return same;
}
#define RtlCompareMemory Compare
#include "../../native/LU4Memory/driver/identity_edid.inc"
#undef RtlCompareMemory
}
bool RegistryChecks(const std::wstring& uuid);
int wmain(int argc, wchar_t** argv) {
    if (argc == 3 && wcscmp(argv[1], L"--registry") == 0) return RegistryChecks(argv[2]) ? 0 : 3;
    if (argc != 4) return 2;
    std::wstring hex = argv[3];
    std::vector<BYTE> data;
    for (size_t i = 0; i + 1 < hex.size(); i += 2)
        data.push_back(static_cast<BYTE>(wcstoul(hex.substr(i, 2).c_str(), nullptr, 16)));
    auto kernel = data;
    KernelEdidProbe::IdentityPatchEdid(kernel.data(), static_cast<ULONG>(kernel.size()), argv[1], argv[2]);
    MapMonitorEdid(argv[1], argv[2], data.data(), static_cast<DWORD>(data.size()));
    char pair[3];
    for (BYTE value : data) { sprintf_s(pair, "%02X", value); std::cout << pair; }
    std::cout << "\n";
    for (BYTE value : kernel) { sprintf_s(pair, "%02X", value); std::cout << pair; }
    std::cout << "\n";
    BYTE mac[] = {2, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE}, mapped[6]{};
    MapAdapterMac(mac, "{01234567-89ab-cdef-0123-456789abcdef}", mapped);
    for (BYTE value : mapped) { sprintf_s(pair, "%02X", value); std::cout << pair; }
    std::cout << "\n";
    return 0;
}
