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
ULONG envelope_offset = 0;

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
    // Version metadata is irrelevant. The envelope is located and checked
    // cryptographically when the client opens its world connection.
    if (nt->Signature != IMAGE_NT_SIGNATURE ||
        nt->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64 ||
        nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC ||
        nt->OptionalHeader.SizeOfImage < 0x100000 ||
        nt->OptionalHeader.SizeOfImage > 0x40000000) return false;
    return true;
}

bool decode_envelope(const BYTE (&envelope)[78], BYTE (&middle)[16]) {
    BYTE key[20]{}, digest[20]{}, plain[38]{};
    memcpy(key, envelope + 20, sizeof(key));
    memcpy(plain, envelope + 40, sizeof(plain));
    rc4(key, plain, sizeof(plain));
    const bool prefix = plain[0] == 1;
    bool suffix = true;
    for (unsigned index = 33; index < 38; ++index) suffix = suffix && plain[index] == 0xAA;
    // The inexpensive plaintext marker filters the writable-image scan;
    // the keyed digest is computed only for a matching candidate.
    bool valid = prefix && suffix && sha1(envelope + 40, sizeof(plain), digest);
    if (valid) {
        rc4(key, digest, sizeof(digest));
        valid = memcmp(digest, envelope, sizeof(digest)) == 0;
    }
    if (valid) memcpy(middle, plain + 9, sizeof(middle));
    SecureZeroMemory(key, sizeof(key));
    SecureZeroMemory(digest, sizeof(digest));
    SecureZeroMemory(plain, sizeof(plain));
    return valid;
}

bool find_envelope(HANDLE device, BYTE (&middle)[16]) {
    BYTE header[4096]{};
    if (!read_driver(device, 0, header, sizeof(header))) return false;
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(header);
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(header + dos->e_lfanew);
    if (nt->FileHeader.NumberOfSections == 0 || nt->FileHeader.NumberOfSections > 64 ||
        reinterpret_cast<const BYTE*>(IMAGE_FIRST_SECTION(nt) + nt->FileHeader.NumberOfSections) >
            header + sizeof(header)) return false;
    const auto* sections = IMAGE_FIRST_SECTION(nt);
    ULONG matched = 0;
    BYTE candidate[78]{};
    for (unsigned index = 0; index < nt->FileHeader.NumberOfSections; ++index) {
        const auto& section = sections[index];
        if ((section.Characteristics & (IMAGE_SCN_MEM_READ | IMAGE_SCN_MEM_WRITE)) !=
            (IMAGE_SCN_MEM_READ | IMAGE_SCN_MEM_WRITE)) continue;
        const ULONG start = section.VirtualAddress;
        const ULONG size = section.Misc.VirtualSize;
        if (start >= nt->OptionalHeader.SizeOfImage ||
            size > nt->OptionalHeader.SizeOfImage - start) return false;
        for (ULONG position = 0; position + sizeof(candidate) <= size; position += 4096) {
            BYTE page[4096 + sizeof(candidate)]{};
            const ULONG length = (size - position) < 4096 ? size - position : 4096;
            if (!read_driver(device, start + position, page, length)) continue;
            ULONG available = length;
            if (size - position > length &&
                read_driver(device, start + position + length, page + length,
                    static_cast<ULONG>(sizeof(candidate) - 1)))
                available += static_cast<ULONG>(sizeof(candidate) - 1);
            for (ULONG at = 0; at + sizeof(candidate) <= available; at += 8) {
                bool first = false, key = false, cipher = false;
                for (unsigned byte = 0; byte < 20; ++byte) {
                    first |= page[at + byte] != 0;
                    key |= page[at + 20 + byte] != 0;
                }
                for (unsigned byte = 40; byte < sizeof(candidate); ++byte)
                    cipher |= page[at + byte] != 0;
                if (!first || !key || !cipher) continue;
                memcpy(candidate, page + at, sizeof(candidate));
                BYTE found[16]{};
                if (!decode_envelope(candidate, found)) continue;
                if (matched) {
                    const bool same = memcmp(middle, found, sizeof(middle)) == 0;
                    SecureZeroMemory(found, sizeof(found));
                    if (!same) return false;
                    continue;
                }
                matched = start + position + at;
                memcpy(middle, found, sizeof(middle));
                SecureZeroMemory(found, sizeof(found));
            }
        }
    }
    envelope_offset = matched;
    SecureZeroMemory(candidate, sizeof(candidate));
    return matched != 0;
}

bool read_middle(BYTE (&middle)[16]) {
    HANDLE device = CreateFileW(L"\\\\.\\LU4Memory", GENERIC_READ, 0, nullptr,
                                OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (device == INVALID_HANDLE_VALUE) { status("world_identity_driver_error", GetLastError()); return false; }
    bool valid = supported_driver(device);
    if (valid && envelope_offset) {
        BYTE envelope[78]{};
        valid = read_driver(device, envelope_offset, envelope, sizeof(envelope)) &&
            decode_envelope(envelope, middle);
        SecureZeroMemory(envelope, sizeof(envelope));
        if (!valid) envelope_offset = 0;
    }
    if (valid && !envelope_offset) valid = find_envelope(device, middle);
    CloseHandle(device);
    if (!valid) status("world_identity_envelope_error", 0);
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

bool ValidateWorldIdentityLayout() {
    if (!enabled) return false;
    HANDLE device = CreateFileW(L"\\\\.\\LU4Memory", GENERIC_READ, 0, nullptr,
                                OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (device == INVALID_HANDLE_VALUE) return false;
    const bool valid = supported_driver(device);
    CloseHandle(device);
    return valid;
}

bool ValidateWorldIdentity() {
    BYTE original[16]{};
    const bool valid = enabled && read_middle(original) && memcmp(original, replacement, 16) != 0;
    SecureZeroMemory(original, sizeof(original));
    return valid;
}

bool CopyWorldIdentity(BYTE (&value)[16]) {
    if (!enabled) return false;
    memcpy(value, replacement, sizeof(value)); return true;
}

bool RewriteWorldIdentity(const char* source, size_t size, char (&output)[69]) {
    if (!enabled || !source || (size != 67 && size != sizeof(output))) return false;
    const HMODULE clmods = GetModuleHandleW(L"clmods64.dll");
    MEMORY_BASIC_INFORMATION region{};
    const bool source_in_clmods = clmods &&
        VirtualQuery(source, &region, sizeof(region)) == sizeof(region) &&
        region.Type == MEM_IMAGE && region.AllocationBase == clmods &&
        reinterpret_cast<std::uintptr_t>(source) + size <=
            reinterpret_cast<std::uintptr_t>(region.BaseAddress) + region.RegionSize;
    if (!source_in_clmods ||
        static_cast<BYTE>(source[0]) != size || source[1] != 0) {
        status("world_identity_packet_error", 1);
        return false;
    }
    BYTE middle[16]{};
    if (!read_middle(middle)) return false;
    if (memcmp(middle, replacement, sizeof(middle)) == 0) {
        SecureZeroMemory(middle, sizeof(middle));
        status("world_identity_not_new", 1); return false;
    }
    memcpy(output, source, size);
    // The driver's keyed XOR recurrence precedes RC4. Its ciphertext delta is
    // the prefix XOR of plaintext deltas; neither cipher state depends on data.
    // Both verified world layouts end in the same 16-byte identity and 8-byte tail.
    const auto identity_start = size - 24;
    BYTE delta = 0;
    for (size_t index = identity_start; index < size; ++index) {
        if (index < identity_start + 16)
            delta ^= middle[index - identity_start] ^ replacement[index - identity_start];
        output[index] = static_cast<char>(static_cast<BYTE>(output[index]) ^ delta);
    }
    SecureZeroMemory(middle, sizeof(middle));
    status("world_identity_applied", 16);
    return true;
}
