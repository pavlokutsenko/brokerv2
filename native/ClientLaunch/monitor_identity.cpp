#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <string>
#include <cwchar>
#include "monitor_identity.h"

namespace {
std::wstring identity_uuid;

uint32_t hash_text(const std::wstring& text) {
    uint32_t hash = 2166136261u;
    for (wchar_t ch : text) {
        if (ch >= L'A' && ch <= L'Z') ch += 32;
        hash = (hash ^ static_cast<uint32_t>(ch)) * 16777619u;
    }
    return hash;
}

std::wstring monitor_instance(HKEY key) {
    using QueryKeyFn = LONG(NTAPI*)(HANDLE, int, void*, ULONG, ULONG*);
    static const auto query = reinterpret_cast<QueryKeyFn>(GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "NtQueryKey"));
    alignas(wchar_t) BYTE buffer[4096]{};
    ULONG size = 0;
    if (!query || query(key, 3, buffer, sizeof(buffer), &size) < 0) return {};
    const ULONG bytes = *reinterpret_cast<const ULONG*>(buffer);
    if (bytes % 2 || bytes > sizeof(buffer) - sizeof(ULONG)) return {};
    std::wstring path(reinterpret_cast<const wchar_t*>(buffer + sizeof(ULONG)), bytes / 2);
    for (auto& ch : path) if (ch >= L'A' && ch <= L'Z') ch += 32;
    const std::wstring root = L"\\registry\\machine\\system\\";
    const std::wstring marker = L"\\enum\\display\\";
    const std::wstring suffix = L"\\device parameters";
    const auto first = path.find(marker);
    if (path.compare(0, root.size(), root) || first == std::wstring::npos ||
        path.size() < first + marker.size() + suffix.size() ||
        path.compare(path.size() - suffix.size(), suffix.size(), suffix)) return {};
    auto instance = path.substr(first + marker.size(), path.size() - suffix.size() - first - marker.size());
    const auto slash = instance.find(L'\\');
    if (slash == std::wstring::npos || slash == 0 || slash + 1 == instance.size() ||
        instance.find(L'\\', slash + 1) != std::wstring::npos) return {};
    return instance;
}

bool valid_edid(const BYTE* data, DWORD size) {
    const BYTE header[] = {0, 255, 255, 255, 255, 255, 255, 0};
    if (!data || size < 128 || size % 128 || memcmp(data, header, sizeof(header)) ||
        (static_cast<DWORD>(data[126]) + 1) * 128 > size) return false;
    for (DWORD block = 0; block < size; block += 128) {
        unsigned sum = 0;
        for (unsigned i = 0; i < 128; ++i) sum += data[block + i];
        if (sum & 255) return false;
    }
    return true;
}
} // namespace

bool InitializeMonitorIdentity(const std::wstring& uuid) {
    if (uuid.size() != 36) return false;
    identity_uuid = uuid;
    return true;
}

std::wstring MonitorSerialText(const std::wstring& uuid, const std::wstring& instance) {
    uint32_t serial = hash_text(L"edid|" + uuid + L"|" + instance);
    if (!serial) serial = 1;
    wchar_t text[17]{};
    swprintf_s(text, L"%08X%08X", serial, serial ^ 0x9E3779B9u);
    return std::wstring(text, 12);
}

void PatchMonitorRegistryValue(HKEY key, LPCWSTR name, DWORD type, BYTE* data, DWORD size) {
    if (!name || _wcsicmp(name, L"EDID") || type != REG_BINARY || !valid_edid(data, size)) return;
    const auto instance = monitor_instance(key);
    if (instance.empty()) return;
    MapMonitorEdid(identity_uuid, instance, data, size);
}

void MapMonitorEdid(const std::wstring& uuid, const std::wstring& instance, BYTE* data, DWORD size) {
    if (uuid.size() != 36 || instance.empty() || !valid_edid(data, size)) return;
    uint32_t serial = hash_text(L"edid|" + uuid + L"|" + instance);
    if (!serial) serial = 1;
    for (unsigned i = 0; i < 4; ++i) data[12 + i] = static_cast<BYTE>(serial >> (8 * i));
    char text[17]{};
    sprintf_s(text, "%08X%08X", serial, serial ^ 0x9E3779B9u);
    for (unsigned offset = 54; offset <= 108; offset += 18) {
        if (data[offset] || data[offset + 1] || data[offset + 2] || data[offset + 3] != 0xFF || data[offset + 4]) continue;
        memcpy(data + offset + 5, text, 12);
        data[offset + 17] = 10;
    }
    unsigned sum = 0;
    for (unsigned i = 0; i < 127; ++i) sum += data[i];
    data[127] = static_cast<BYTE>(0u - sum);
}

void MapAdapterMac(const BYTE* seed, const char* adapterName, BYTE* target) {
    memcpy(target, seed, 6);
    if (!adapterName || !*adapterName) return;
    wchar_t text[13]{};
    swprintf_s(text, L"%02X%02X%02X%02X%02X%02X", seed[0], seed[1], seed[2], seed[3], seed[4], seed[5]);
    std::wstring name;
    for (const char* current = adapterName; *current; ++current)
        if (*current != '{' && *current != '}') name += static_cast<unsigned char>(*current);
    const auto hash = hash_text(L"mac|" + std::wstring(text) + L"|" + name);
    target[0] = (target[0] | 2) & 254;
    for (unsigned i = 0; i < 3; ++i) target[3 + i] = static_cast<BYTE>(hash >> (8 * i));
}
