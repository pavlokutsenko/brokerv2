#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <wincrypt.h>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <cstdio>
#include "world_identity.h"
#include "trace.h"
#include "../LU4Memory/include/lu4_protocol.h"

namespace {
bool enabled = false;
BYTE replacement[16]{};
constexpr ULONG envelope_rva = 0x12ABD90;
constexpr DWORD driver_timestamp = 0x6A75E39D;
constexpr DWORD driver_image_size = 0x1317000;

void status(const char* event, unsigned detail) {
    TraceEvent(event, detail);
    wchar_t directory[MAX_PATH]{}, root[MAX_PATH]{}, logs[MAX_PATH]{}, path[MAX_PATH]{};
    const DWORD length = GetEnvironmentVariableW(L"LOCALAPPDATA", directory, MAX_PATH);
    if (!length || length >= MAX_PATH ||
        swprintf_s(root, L"%s\\PriceCheckCollector", directory) < 0 ||
        swprintf_s(logs, L"%s\\logs", root) < 0 ||
        swprintf_s(path, L"%s\\world-identity-%lu.txt", logs, GetCurrentProcessId()) < 0) return;
    CreateDirectoryW(root, nullptr);
    CreateDirectoryW(logs, nullptr);
    HANDLE file = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ, nullptr,
                              CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    char line[128]{};
    const int count = sprintf_s(line, "%s detail=%u\r\n", event, detail);
    DWORD written = 0;
    if (count > 0) WriteFile(file, line, count, &written, nullptr);
    CloseHandle(file);
}

int hex(wchar_t value) {
    if (value >= L'0' && value <= L'9') return value - L'0';
    if (value >= L'a' && value <= L'f') return value - L'a' + 10;
    if (value >= L'A' && value <= L'F') return value - L'A' + 10;
    return -1;
}

void rc4(const BYTE (&key)[20], BYTE* data, size_t length) {
    BYTE state[256]{};
    for (unsigned index = 0; index < 256; ++index) state[index] = static_cast<BYTE>(index);
    unsigned right = 0;
    for (unsigned index = 0; index < 256; ++index) {
        right = (right + state[index] + key[index % 20]) & 255;
        std::swap(state[index], state[right]);
    }
    unsigned left = 0;
    right = 0;
    for (size_t index = 0; index < length; ++index) {
        left = (left + 1) & 255;
        right = (right + state[left]) & 255;
        std::swap(state[left], state[right]);
        data[index] ^= state[(state[left] + state[right]) & 255];
    }
    SecureZeroMemory(state, sizeof(state));
}

bool sha1(const BYTE* data, DWORD length, BYTE (&digest)[20]) {
    HCRYPTPROV provider = 0;
    HCRYPTHASH hash = 0;
    DWORD digest_size = sizeof(digest);
    bool result = CryptAcquireContextW(&provider, nullptr, nullptr, PROV_RSA_FULL,
                                       CRYPT_VERIFYCONTEXT) &&
        CryptCreateHash(provider, CALG_SHA1, 0, 0, &hash) &&
        CryptHashData(hash, data, length, 0) &&
        CryptGetHashParam(hash, HP_HASHVAL, digest, &digest_size, 0) &&
        digest_size == sizeof(digest);
    if (hash) CryptDestroyHash(hash);
    if (provider) CryptReleaseContext(provider, 0);
    return result;
}

bool read_driver(HANDLE device, ULONG offset, BYTE* target, ULONG size) {
    BYTE buffer[24 + 4096]{};
    if (size > sizeof(buffer) - 24) return false;
    auto* request = reinterpret_cast<LU4_MODULE_READ_REQUEST*>(buffer);
    request->Version = LU4_PROTOCOL_VERSION;
    request->Offset = offset;
    request->Size = size;
    DWORD returned = 0;
    const bool ok = DeviceIoControl(device, IOCTL_LU4_READ_ACTIVE64, buffer, 24,
        buffer, 24 + size, &returned, nullptr) && returned == 24 + size &&
        request->BytesTransferred == size;
    if (ok) memcpy(target, buffer + 24, size);
    SecureZeroMemory(buffer, sizeof(buffer));
    return ok;
}

bool supported_driver(HANDLE device) {
    BYTE header[4096]{};
    if (!read_driver(device, 0, header, sizeof(header))) return false;
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(header);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < sizeof(IMAGE_DOS_HEADER) ||
        dos->e_lfanew > sizeof(header) - sizeof(IMAGE_NT_HEADERS64)) return false;
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(header + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.TimeDateStamp != driver_timestamp ||
        nt->OptionalHeader.SizeOfImage != driver_image_size) return false;
    const BYTE expected[] = {0x0F,0x11,0x4C,0x01,0x10,0x0F,0x11,0x04,0x01};
    BYTE actual[sizeof(expected)]{};
    return read_driver(device, 0x9C481, actual, sizeof(actual)) &&
        memcmp(actual, expected, sizeof(actual)) == 0;
}

bool read_middle(BYTE (&middle)[16]) {
    HANDLE device = CreateFileW(L"\\\\.\\LU4Memory", GENERIC_READ, 0, nullptr,
                                OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (device == INVALID_HANDLE_VALUE) { status("world_identity_driver_error", GetLastError()); return false; }
    BYTE envelope[78]{};
    const bool read = supported_driver(device) && read_driver(device, envelope_rva, envelope, sizeof(envelope));
    CloseHandle(device);
    if (!read) { status("world_identity_layout_error", 1); return false; }
    BYTE key[20]{}, digest[20]{}, plain[38]{};
    memcpy(key, envelope + 20, sizeof(key));
    memcpy(plain, envelope + 40, sizeof(plain));
    bool valid = sha1(plain, sizeof(plain), digest);
    rc4(key, digest, sizeof(digest));
    valid = valid && memcmp(digest, envelope, sizeof(digest)) == 0;
    rc4(key, plain, sizeof(plain));
    valid = valid && plain[0] == 1;
    for (unsigned index = 33; index < 38; ++index) valid = valid && plain[index] == 0xAA;
    if (valid) memcpy(middle, plain + 9, sizeof(middle));
    SecureZeroMemory(key, sizeof(key));
    SecureZeroMemory(digest, sizeof(digest));
    SecureZeroMemory(plain, sizeof(plain));
    SecureZeroMemory(envelope, sizeof(envelope));
    if (!valid) status("world_identity_envelope_error", 1);
    return valid;
}
}

bool ConfigureWorldIdentity() {
    wchar_t value[33]{};
    const DWORD length = GetEnvironmentVariableW(L"PRICECHECK_WORLD_IDENTITY", value, 33);
    if (!length) return true;
    if (length != 32) return false;
    for (unsigned index = 0; index < 16; ++index) {
        const int high = hex(value[index * 2]), low = hex(value[index * 2 + 1]);
        if (high < 0 || low < 0) return false;
        replacement[index] = static_cast<BYTE>((high << 4) | low);
    }
    enabled = true;
    return true;
}

bool WorldIdentityEnabled() { return enabled; }

bool RewriteWorldIdentity(const char* source, size_t size, char (&output)[69]) {
    if (!enabled || !source || size != sizeof(output)) return false;
    const HMODULE clmods = GetModuleHandleW(L"clmods64.dll");
    if (!clmods || source != reinterpret_cast<const char*>(clmods) + 0x7F01D ||
        static_cast<BYTE>(source[0]) != 69 || source[1] != 0) {
        status("world_identity_packet_error", 1);
        return false;
    }
    BYTE middle[16]{};
    if (!read_middle(middle)) return false;
    memcpy(output, source, sizeof(output));
    // The driver's keyed XOR recurrence precedes RC4. Its ciphertext delta is
    // the prefix XOR of plaintext deltas; neither cipher state depends on data.
    BYTE delta = 0;
    for (unsigned index = 45; index < sizeof(output); ++index) {
        if (index < 61) delta ^= middle[index - 45] ^ replacement[index - 45];
        output[index] = static_cast<char>(static_cast<BYTE>(output[index]) ^ delta);
    }
    SecureZeroMemory(middle, sizeof(middle));
    status("world_identity_applied", 16);
    return true;
}
