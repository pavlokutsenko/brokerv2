#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cwchar>
#include "launch_guard.h"

namespace {
LaunchGuardState* state = nullptr;
HANDLE mapping = nullptr;
std::uint64_t creation = 0;
SRWLOCK state_lock = SRWLOCK_INIT;
bool required() { return GetEnvironmentVariableW(L"PRICECHECK_LAUNCH_GUARD", nullptr, 0) > 1; }
bool open_state_unlocked() {
    if (state) return state->magic == launch_guard_magic && state->schema == 1 &&
        state->pid == GetCurrentProcessId() && state->process_created == creation;
    if (!required()) return true;
    wchar_t name[128]{};
    const auto length = GetEnvironmentVariableW(L"PRICECHECK_LAUNCH_GUARD", name, 128);
    if (!length || length >= 128) return false;
    mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, name);
    if (!mapping) return false;
    state = static_cast<LaunchGuardState*>(MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(*state)));
    FILETIME created{}, exited{}, kernel{}, user{};
    const bool timed = GetProcessTimes(GetCurrentProcess(), &created, &exited, &kernel, &user) != FALSE;
    creation = static_cast<std::uint64_t>(created.dwHighDateTime) << 32 | created.dwLowDateTime;
    if (!state || state->magic != launch_guard_magic || state->schema != 1 ||
        state->pid != GetCurrentProcessId() || !timed || state->process_created != creation) {
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
    if (state) InterlockedCompareExchange(&state->error, error, 0);
}
bool LaunchGuardAllowsLogin(bool character) {
    if (!open_state()) return false;
    return !state || LaunchGuardLeaseValid() && (state->flags & 1) &&
        (!character || ((state->flags & 2) && state->world_opened_tick > 0 &&
            state->applied_tick >= state->world_opened_tick));
}
bool LaunchGuardAllowsNetwork() {
    if (!open_state()) return false;
    return !state || LaunchGuardLeaseValid() && (state->flags & 1);
}
