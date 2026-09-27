#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <initializer_list>
#include "../../native/ClientLaunch/launch_guard.h"

static std::atomic_bool hardware{true}, world{true}, layout{true};
bool VerifyIdentityHooks() { return hardware.load(); }
bool ValidateWorldIdentity() { return world.load(); }
bool ValidateWorldIdentityLayout() { return layout.load(); }
bool CopyWorldIdentity(BYTE (&value)[16]) { memset(value, 0x11, sizeof(value)); return true; }
static void require(bool condition, const char* message) {
    if (!condition) { fprintf(stderr, "%s\n", message); ExitProcess(1); }
}
int main(int argc, char** argv) {
    wchar_t name[128]{};
    swprintf_s(name, L"Local\\PriceCheckGuardProbe_%lu", GetCurrentProcessId());
    SetEnvironmentVariableW(L"PRICECHECK_LAUNCH_GUARD", name);
    require(!LaunchGuardAllowsLogin(false) && !LaunchGuardAllowsNetwork(), "Missing mapping was accepted");
    HANDLE mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(LaunchGuardState), name);
    require(mapping != nullptr, "Create mapping failed");
    auto state = static_cast<LaunchGuardState*>(MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(LaunchGuardState)));
    require(state != nullptr, "Map view failed");
    state->magic = launch_guard_magic; state->schema = 1; state->pid = GetCurrentProcessId();
    state->required = 3; state->controller_ready = 1; state->heartbeat = GetTickCount64();
    FILETIME created{}, exit{}, kernel{}, user{};
    require(GetProcessTimes(GetCurrentProcess(), &created, &exit, &kernel, &user), "Process time unavailable");
    const std::uint64_t birth = static_cast<std::uint64_t>(created.dwHighDateTime) << 32 | created.dwLowDateTime;
    state->process_created = birth + 1;
    require(!LaunchGuardAllowsLogin(false) && !LaunchGuardAllowsLogin(false), "Wrong process birth accepted on retry");
    state->process_created = birth;
    memset(state->expected_world, 0x11, sizeof(state->expected_world));
    if (argc == 2) {
        if (strcmp(argv[1], "--prelogin-pending-envelope") == 0) {
            world.store(false);
            require(StartLaunchGuard(), "Pending envelope prevented pre-login checks");
            Sleep(1200);
            require(!state->error && (state->flags & 1), "Pending envelope killed a valid pre-login client");
            puts("NATIVE_PRELOGIN_OK layout checked while world envelope is pending"); return 0;
        }
        require(StartLaunchGuard(), "Valid watchdog could not start");
        if (strcmp(argv[1], "--watchdog-hwid") == 0) hardware.store(false);
        else if (strcmp(argv[1], "--watchdog-world") == 0) layout.store(false);
        else if (strcmp(argv[1], "--watchdog-envelope") == 0) { state->world_count = 1; world.store(false); }
        else if (strcmp(argv[1], "--watchdog-controller") == 0) state->controller_ready = 0;
        else return 1;
        Sleep(4000);
        require(false, "Watchdog allowed the unprotected process to survive");
    }
    require(!LaunchGuardAllowsLogin(false) && !LaunchGuardAllowsNetwork(), "Unconfirmed HWID accepted");
    state->flags = 1;
    state->required = 1;
    require(LaunchGuardAllowsLogin(false) && LaunchGuardAllowsNetwork(), "HWID-only lease rejected");
    for (auto invalid : {0, 2, 4, 7}) {
        state->required = invalid;
        require(!LaunchGuardAllowsLogin(false) && !LaunchGuardAllowsNetwork(), "Invalid required flags accepted");
    }
    state->required = 3;
    require(LaunchGuardAllowsLogin(false) && LaunchGuardAllowsNetwork(), "Healthy pre-login gate rejected");
    require(!LaunchGuardAllowsLogin(true), "Character gate accepted an unconfirmed world HWID");
    state->flags = 3; state->world_opened_tick = GetTickCount64(); state->applied_tick = state->world_opened_tick;
    require(LaunchGuardAllowsLogin(true), "Applied world HWID rejected");
    state->world_opened_tick = state->applied_tick + 1;
    require(!LaunchGuardAllowsLogin(true), "Stale world application accepted");
    state->heartbeat = GetTickCount64() - 6000;
    require(!LaunchGuardAllowsLogin(false) && !LaunchGuardAllowsNetwork(), "Stale controller accepted");
    state->heartbeat = GetTickCount64(); state->controller_ready = 0;
    require(!LaunchGuardAllowsLogin(false) && !LaunchGuardAllowsNetwork(), "Revoked controller accepted");
    state->controller_ready = 1; state->error = 8;
    require(!LaunchGuardAllowsLogin(false) && !LaunchGuardAllowsNetwork(), "Failed native check accepted");
    state->error = 0; state->process_created = birth + 1;
    require(!LaunchGuardAllowsLogin(false) && !LaunchGuardAllowsNetwork(), "Changed process identity accepted");
    puts("NATIVE_GATES_OK mapping PID/birth, HWID readiness, world freshness, lease, failure and send gate");
    UnmapViewOfFile(state); CloseHandle(mapping);
}
