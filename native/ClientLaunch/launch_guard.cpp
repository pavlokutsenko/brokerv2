#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cwchar>
#include <cstdio>
#include "launch_guard.h"

namespace {
LaunchGuardState* state = nullptr;
HANDLE mapping = nullptr;
std::uint64_t creation = 0;
SRWLOCK state_lock = SRWLOCK_INIT;
bool denied(unsigned reason, DWORD system_error = 0) {
    // Failure-only diagnostics: no credentials or hardware values are written.
    wchar_t directory[MAX_PATH]{}, root[MAX_PATH]{}, logs[MAX_PATH]{}, path[MAX_PATH]{};
    const DWORD length = GetEnvironmentVariableW(L"LOCALAPPDATA", directory, MAX_PATH);
    if (length && length < MAX_PATH &&
        swprintf_s(root, L"%s\\PriceCheckCollector", directory) > 0 &&
        swprintf_s(logs, L"%s\\logs", root) > 0 &&
        swprintf_s(path, L"%s\\launch-guard-%lu.txt", logs, GetCurrentProcessId()) > 0) {
        CreateDirectoryW(root, nullptr); CreateDirectoryW(logs, nullptr);
        HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file != INVALID_HANDLE_VALUE) {
            char line[160]{};
            const int count = sprintf_s(line, "tick=%llu reason=%u system_error=%lu flags=%ld controller=%ld error=%ld heartbeat_age=%llu\r\n",
                GetTickCount64(), reason, system_error, state ? state->flags : 0,
                state ? state->controller_ready : 0, state ? state->error : 0,
                state ? GetTickCount64() - state->heartbeat : 0);
            DWORD written = 0;
            if (count > 0) WriteFile(file, line, count, &written, nullptr);
            CloseHandle(file);
        }
    }
    return false;
}
bool required() { return GetEnvironmentVariableW(L"PRICECHECK_LAUNCH_GUARD", nullptr, 0) > 1; }
bool open_state_unlocked() {
    if (state) return state->magic == launch_guard_magic && state->schema == 1 &&
        state->pid == GetCurrentProcessId() && state->process_created == creation;
    if (!required()) return true;
    wchar_t name[128]{};
    const auto length = GetEnvironmentVariableW(L"PRICECHECK_LAUNCH_GUARD", name, 128);
    if (!length || length >= 128) return denied(1);
    mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, name);
    if (!mapping) return denied(2, GetLastError());
    state = static_cast<LaunchGuardState*>(MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(*state)));
    FILETIME created{}, exited{}, kernel{}, user{};
    const bool timed = GetProcessTimes(GetCurrentProcess(), &created, &exited, &kernel, &user) != FALSE;
    const DWORD time_error = timed ? 0 : GetLastError();
    creation = static_cast<std::uint64_t>(created.dwHighDateTime) << 32 | created.dwLowDateTime;
    if (!state || state->magic != launch_guard_magic || state->schema != 1 ||
        state->pid != GetCurrentProcessId() || !timed || state->process_created != creation) {
        denied(!state ? 3 : !timed ? 5 : state->process_created != creation ? 6 : 4, time_error);
        if (state) UnmapViewOfFile(state);
        CloseHandle(mapping); state = nullptr; mapping = nullptr;
        return false;
    }
    return true;
}
bool open_state() {
    AcquireSRWLockExclusive(&state_lock);
    const bool valid = open_state_unlocked();
    ReleaseSRWLockExclusive(&state_lock);
    return valid;
}
}
LaunchGuardState* GetLaunchGuardState() { return open_state() ? state : nullptr; }
bool LaunchGuardLeaseValid() {
    return state && (state->required == 1 || state->required == 3) && !state->error && state->controller_ready == 1 &&
        GetTickCount64() - static_cast<ULONGLONG>(InterlockedCompareExchange64(&state->heartbeat, 0, 0)) < 5000;
}
void LaunchGuardFail(LONG error) {
    if (state && InterlockedCompareExchange(&state->error, error, 0) == 0) denied(20 + error);
}
bool LaunchGuardAllowsLogin(bool character) {
    if (!open_state()) return false;
    if (!state) return true;
    if (!LaunchGuardLeaseValid()) return denied(7);
    if (!(state->flags & 1)) return denied(8);
    return !character || ((state->flags & 2) && state->world_opened_tick > 0 &&
        state->applied_tick >= state->world_opened_tick);
}
bool LaunchGuardAllowsNetwork() {
    if (!open_state()) return false;
    return !state || LaunchGuardLeaseValid() && (state->flags & 1);
}
