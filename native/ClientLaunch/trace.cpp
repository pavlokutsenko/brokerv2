#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdio>
#include <cstring>
#include "trace.h"

namespace {
HANDLE file = INVALID_HANDLE_VALUE;
HANDLE ioctl_file = INVALID_HANDLE_VALUE;
SRWLOCK gate = SRWLOCK_INIT;
volatile LONG event_count = 0;
volatile LONG ioctl_event_count = 0;
constexpr LONG max_events = 4096;
}

void TraceStart() {
    wchar_t enabled[2]{};
    if (GetEnvironmentVariableW(L"PRICECHECK_TRACE_HARDWARE", enabled, 2) != 1 || enabled[0] != L'1') return;
    wchar_t directory[MAX_PATH]{};
    DWORD length = GetEnvironmentVariableW(L"LOCALAPPDATA", directory, MAX_PATH);
    if (length == 0 || length >= MAX_PATH) return;
    wchar_t root[MAX_PATH]{};
    wchar_t logs[MAX_PATH]{};
    wchar_t path[MAX_PATH]{};
    if (swprintf_s(root, L"%s\\PriceCheckCollector", directory) < 0 ||
        swprintf_s(logs, L"%s\\logs", root) < 0 ||
        swprintf_s(path, L"%s\\hardware-trace-%lu.csv", logs, GetCurrentProcessId()) < 0) return;
    CreateDirectoryW(root, nullptr);
    CreateDirectoryW(logs, nullptr);
    file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    const char header[] = "utc,category,detail\r\n";
    DWORD written = 0;
    WriteFile(file, header, sizeof(header) - 1, &written, nullptr);
    if (swprintf_s(path, L"%s\\ioctl-trace-%lu.csv", logs, GetCurrentProcessId()) < 0) return;
    ioctl_file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (ioctl_file == INVALID_HANDLE_VALUE) return;
    const char ioctl_header[] = "utc,code,input_bytes,output_capacity,returned_bytes,success,last_error,caller_module,caller_rva,call_stack\r\n";
    WriteFile(ioctl_file, ioctl_header, sizeof(ioctl_header) - 1, &written, nullptr);
}

void TraceEvent(const char* category, unsigned detail) {
    if (file == INVALID_HANDLE_VALUE || InterlockedIncrement(&event_count) > max_events) return;
    SYSTEMTIME now{};
    GetSystemTime(&now);
    char line[160]{};
    int count = sprintf_s(line, "%04u-%02u-%02uT%02u:%02u:%02u.%03uZ,%s,%08X\r\n",
        now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond,
        now.wMilliseconds, category, detail);
    if (count <= 0) return;
    AcquireSRWLockExclusive(&gate);
    DWORD written = 0;
    WriteFile(file, line, static_cast<DWORD>(count), &written, nullptr);
    ReleaseSRWLockExclusive(&gate);
}

void TraceWorldSendStack() {
    TraceEvent("world_send_stack", 1);
    void* frames[12]{};
    const USHORT count = CaptureStackBackTrace(2, 12, frames, nullptr);
    for (USHORT index = 0; index < count; ++index) {
        HMODULE module = nullptr;
        if (!GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
            GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCSTR>(frames[index]), &module)) continue;
        char path[MAX_PATH]{};
        if (!GetModuleFileNameA(module, path, MAX_PATH)) continue;
        const char* slash = strrchr(path, '\\');
        const char* name = slash ? slash + 1 : path;
        unsigned module_id = 0;
        if (_stricmp(name, "clmods64.dll") == 0) module_id = 1;
        else if (_stricmp(name, "lu4.bin") == 0) module_id = 2;
        else if (_stricmp(name, "ws2_32.dll") == 0) module_id = 3;
        else if (_stricmp(name, "PriceCheck.ClientAgent.dll") == 0) module_id = 4;
        else if (_stricmp(name, "kernel32.dll") == 0) module_id = 5;
        else if (_stricmp(name, "ntdll.dll") == 0) module_id = 6;
        const auto rva = reinterpret_cast<uintptr_t>(frames[index]) -
                         reinterpret_cast<uintptr_t>(module);
        char category[40]{};
        sprintf_s(category, "world_send_frame_%u_module", index);
        TraceEvent(category, module_id);
        sprintf_s(category, "world_send_frame_%u_rva", index);
        TraceEvent(category, static_cast<unsigned>(rva));
    }
}

void TraceIoctl(unsigned code, unsigned input_size, unsigned output_size,
                unsigned returned, unsigned last_error, bool success, const void* caller) {
    const bool world_probe =
        GetEnvironmentVariableW(L"PRICECHECK_TEST_TRACE_WORLD_IOCTL", nullptr, 0) > 1;
    const bool selected = code == 0x222158 || code == 0x22215C || code == 0x222160 ||
        (world_probe && code >= 0x222000 && code < 0x222200);
    if (ioctl_file == INVALID_HANDLE_VALUE || !selected ||
        InterlockedIncrement(&ioctl_event_count) > max_events) return;
    SYSTEMTIME now{};
    GetSystemTime(&now);
    char module_name[MAX_PATH]{};
    const char* module_base_name = "unknown";
    unsigned long long caller_rva = 0;
    HMODULE module = nullptr;
    if (caller && GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
        GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCSTR>(caller), &module)) {
        caller_rva = reinterpret_cast<uintptr_t>(caller) - reinterpret_cast<uintptr_t>(module);
        if (GetModuleFileNameA(module, module_name, MAX_PATH)) {
            const char* separator = strrchr(module_name, '\\');
            module_base_name = separator ? separator + 1 : module_name;
        }
    }
    void* frames[8]{};
    const USHORT frame_count = CaptureStackBackTrace(0, 8, frames, nullptr);
    char stack[512]{};
    size_t stack_length = 0;
    for (USHORT index = 0; index < frame_count; ++index) {
        HMODULE frame_module = nullptr;
        if (!GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
            GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCSTR>(frames[index]), &frame_module)) continue;
        char frame_name[MAX_PATH]{};
        if (!GetModuleFileNameA(frame_module, frame_name, MAX_PATH)) continue;
        const char* separator = strrchr(frame_name, '\\');
        const char* base_name = separator ? separator + 1 : frame_name;
        const auto rva = reinterpret_cast<uintptr_t>(frames[index]) - reinterpret_cast<uintptr_t>(frame_module);
        int added = sprintf_s(stack + stack_length, sizeof(stack) - stack_length,
                              "%s%s:%llX", stack_length ? "|" : "", base_name,
                              static_cast<unsigned long long>(rva));
        if (added <= 0) break;
        stack_length += static_cast<size_t>(added);
    }
    char line[1024]{};
    int count = sprintf_s(line, "%04u-%02u-%02uT%02u:%02u:%02u.%03uZ,%08X,%u,%u,%u,%u,%u,%s,%llX,%s\r\n",
        now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond,
        now.wMilliseconds, code, input_size, output_size, returned, success ? 1u : 0u,
        last_error, module_base_name, caller_rva, stack);
    if (count <= 0) return;
    AcquireSRWLockExclusive(&gate);
    DWORD written = 0;
    WriteFile(ioctl_file, line, static_cast<DWORD>(count), &written, nullptr);
    ReleaseSRWLockExclusive(&gate);
}
