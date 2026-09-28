#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <objbase.h>
#include <intrin.h>
#include <cstdio>
#include <string>
#include "minhook/include/MinHook.h"
#include "disk_identity.h"
#include "disk_serial.h"
#include "trace.h"

namespace {
using DeviceIoControlFn = BOOL(WINAPI*)(HANDLE, DWORD, LPVOID, DWORD, LPVOID, DWORD, LPDWORD, LPOVERLAPPED);
DeviceIoControlFn real_device_io_control = nullptr;
using NtDeviceIoControlFileFn = LONG(NTAPI*)(HANDLE, HANDLE, void*, void*, void*, ULONG,
                                            void*, ULONG, void*, ULONG);
NtDeviceIoControlFileFn real_nt_device_io_control_file = nullptr;
volatile LONG nt_world_160_count = 0;
std::string disk_serial;
BYTE ata_serial[20]{};
GUID disk_guid{};
DWORD disk_signature = 0;
constexpr DWORD storage_query = 0x002D1400; // IOCTL_STORAGE_QUERY_PROPERTY
constexpr DWORD drive_layout_query = 0x00070050; // IOCTL_DISK_GET_DRIVE_LAYOUT_EX
constexpr DWORD smart_receive = 0x0007C088; // SMART_RCV_DRIVE_DATA
constexpr size_t aa_input_limit = 164;

  unsigned fingerprint(const BYTE* data, size_t length) {
    unsigned hash = 2166136261u;
    for (size_t index = 0; index < length; ++index)
        hash = (hash ^ data[index]) * 16777619u;
    return hash;
  }

  LONG NTAPI hooked_nt_device_io_control_file(HANDLE handle, HANDLE event, void* apc_routine,
      void* apc_context, void* io_status, ULONG code, void* input, ULONG input_size,
      void* output, ULONG output_size) {
      const DWORD previous_error = GetLastError();
      const LONG sequence = code == 0x222160 ? InterlockedIncrement(&nt_world_160_count) : 0;
      wchar_t pause_text[16]{};
      const DWORD pause_length = sequence == 2 ?
          GetEnvironmentVariableW(L"PRICECHECK_TEST_WORLD_NT_HOLD_MS", pause_text, 16) : 0;
      const DWORD pause_ms = pause_length > 0 && pause_length < 16 ?
          min(1500ul, wcstoul(pause_text, nullptr, 10)) : 0;
      if (pause_ms) {
          TraceEvent("world_nt_before_hold", pause_ms);
          Sleep(pause_ms);
      }
      SetLastError(previous_error);
      const LONG status = real_nt_device_io_control_file(handle, event, apc_routine, apc_context,
          io_status, code, input, input_size, output, output_size);
      const DWORD last_error = GetLastError();
      if (pause_ms) {
          TraceEvent("world_nt_after_hold", pause_ms);
          Sleep(pause_ms);
      }
      SetLastError(last_error);
      return status;
  }

std::wstring env(const wchar_t* name) {
    DWORD size = GetEnvironmentVariableW(name, nullptr, 0);
    if (size == 0 || size > 4096) return {};
    std::wstring result(size, L'\0');
    DWORD copied = GetEnvironmentVariableW(name, result.data(), size);
    if (copied == 0 || copied >= size) return {};
    result.resize(copied);
    return result;
}

  BOOL WINAPI hooked_device_io_control(HANDLE handle, DWORD code, LPVOID input, DWORD input_size,
                                       LPVOID output, DWORD output_size, LPDWORD returned, LPOVERLAPPED overlapped) {
      const DWORD previous_error = GetLastError();
    const bool sample_input =
        GetEnvironmentVariableW(L"PRICECHECK_TEST_AA_IOCTL_INPUT_HASH", nullptr, 0) > 1 &&
        (code == 0x222158 || code == 0x22215C || code == 0x222160) &&
        input && input_size > 0 && input_size <= aa_input_limit;
    BYTE before[aa_input_limit]{};
    SIZE_T copied = 0;
      const bool have_before = sample_input &&
          ReadProcessMemory(GetCurrentProcess(), input, before, input_size, &copied) &&
          copied == input_size;
      SetLastError(previous_error);
      BOOL result = real_device_io_control(handle, code, input, input_size, output, output_size, returned, overlapped);
      const DWORD last_error = GetLastError();
    struct RestoreLastError {
        DWORD value;
        ~RestoreLastError() { SetLastError(value); }
      } restore_last_error{last_error};
    TraceEvent("device_ioctl", code);
    TraceIoctl(code, input_size, output_size, result && returned ? *returned : 0,
               last_error, result != FALSE, _ReturnAddress());
    if (have_before) {
        BYTE after[aa_input_limit]{};
        copied = 0;
        if (ReadProcessMemory(GetCurrentProcess(), input, after, input_size, &copied) &&
            copied == input_size) {
            unsigned changed = 0;
            for (DWORD index = 0; index < input_size; ++index)
                if (before[index] != after[index]) ++changed;
            const char* label = code == 0x222158 ? "aa_158" :
                                code == 0x22215C ? "aa_15c" : "aa_160";
            char category[40]{};
            sprintf_s(category, "%s_before", label);
            TraceEvent(category, fingerprint(before, input_size));
            sprintf_s(category, "%s_after", label);
            TraceEvent(category, fingerprint(after, input_size));
            sprintf_s(category, "%s_changed", label);
            TraceEvent(category, changed);
            if (code == 0x222160 && input_size == 59 &&
                GetEnvironmentVariableW(L"PRICECHECK_TEST_AA_WINDOW_HASH", nullptr, 0) > 1) {
                for (unsigned start = 0; start + 37 <= input_size; ++start) {
                    sprintf_s(category, "aa_160_window37_%u", start);
                    TraceEvent(category, fingerprint(before + start, 37));
                }
            }
        }
    }
    if (code == storage_query || code == drive_layout_query || (code >> 16) == 0x2D)
        TraceEvent("storage_ioctl", code);
    if (!result || overlapped || !output || !returned || *returned > output_size) return result;
    auto* descriptor = static_cast<BYTE*>(output);
    if (code == smart_receive && *returned >= 0x38 && output_size >= 0x38) {
        memcpy(descriptor + 0x24, ata_serial, sizeof(ata_serial));
        TraceEvent("smart_serial_modified", 1);
        return result;
    }
    if (code == drive_layout_query && *returned >= 24) {
        DWORD style = *reinterpret_cast<DWORD*>(descriptor);
        if (style == 0) memcpy(descriptor + 8, &disk_signature, sizeof(disk_signature)); // MBR
        if (style == 1) memcpy(descriptor + 8, &disk_guid, sizeof(disk_guid)); // GPT
        return result;
    }
    if (code != storage_query || !input || input_size < 8 || *returned < 28 || disk_serial.empty()) return result;
    auto* query = static_cast<BYTE*>(input);
    if (*reinterpret_cast<DWORD*>(query) != 0 || *reinterpret_cast<DWORD*>(query + 4) != 0) return result;
    DWORD offset = *reinterpret_cast<DWORD*>(descriptor + 24); // STORAGE_DEVICE_DESCRIPTOR.SerialNumberOffset
    if (offset < 28 || offset >= *returned) return result;
    BYTE* value = descriptor + offset;
    BYTE* end = descriptor + *returned;
    BYTE* last = value;
    while (last < end && *last) ++last;
    if (last == end) return result;
    for (size_t i = 0; value + i < last; ++i) value[i] = static_cast<BYTE>(disk_serial[i % disk_serial.size()]);
    return result;
}
}

bool InstallDiskIdentityHooks() {
    auto text = env(L"PRICECHECK_HW_DISK_SERIAL");
    for (wchar_t ch : text) {
        if (ch < 32 || ch > 126) return false;
        disk_serial += static_cast<char>(ch);
    }
    if (disk_serial.empty()) return false;
    if (!BuildAtaSerialFromEnvironment(ata_serial)) return false;
    auto guid_text = env(L"PRICECHECK_HW_DISK_GUID");
    if (CLSIDFromString((L"{" + guid_text + L"}").c_str(), &disk_guid) != NOERROR) return false;
    auto signature_text = env(L"PRICECHECK_HW_DISK_SIGNATURE");
    wchar_t* end = nullptr;
    disk_signature = wcstoul(signature_text.c_str(), &end, 16);
    if (signature_text.size() != 8 || !end || *end) return false;
    const bool installed = MH_CreateHookApi(L"kernel32.dll", "DeviceIoControl", hooked_device_io_control,
                                           reinterpret_cast<void**>(&real_device_io_control)) == MH_OK;
    if (installed && GetEnvironmentVariableW(L"PRICECHECK_TEST_WORLD_NT_IO", nullptr, 0) > 1 &&
        GetModuleHandleW(L"clmods64.dll")) {
        const bool nt_installed = MH_CreateHookApi(L"ntdll.dll", "NtDeviceIoControlFile",
            hooked_nt_device_io_control_file,
            reinterpret_cast<void**>(&real_nt_device_io_control_file)) == MH_OK;
        TraceEvent("world_nt_hook_installed", nt_installed ? 1u : 0u);
    }
    return installed;
}
