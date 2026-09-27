#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include "launch_guard.h"
#include "identity.h"
#include "world_identity.h"

namespace {
#ifdef PRICECHECK_GUARD_PROBE_STAGE
// Test-only build, used without a guard mapping to add readbacks individually.
// A real protected launch always takes the mandatory guarded path below.
DWORD WINAPI probe_monitor(void*) {
    for (;;) {
        Sleep(500);
        if (!VerifyIdentityHooks() ||
            (PRICECHECK_GUARD_PROBE_STAGE >= 3 && !ValidateWorldIdentityLayout())) {
            TerminateProcess(GetCurrentProcess(), 0x50430008); return 8;
        }
    }
}
#endif
DWORD WINAPI monitor(void*) {
    auto state = GetLaunchGuardState();
    for (;;) {
        Sleep(500);
        const bool worldValid = state->world_count > 0 ? ValidateWorldIdentity() : ValidateWorldIdentityLayout();
        const LONG error = !LaunchGuardLeaseValid() ? 4 : !VerifyIdentityHooks() ? 8 : !worldValid ? 9 : 0;
        if (error) {
            LaunchGuardFail(error);
            TerminateProcess(GetCurrentProcess(), 0x50430004); return 4;
        }
        InterlockedExchange64(&state->agent_tick, GetTickCount64());
    }
}
}
bool StartLaunchGuard() {
    const bool required = GetEnvironmentVariableW(L"PRICECHECK_LAUNCH_GUARD", nullptr, 0) > 1;
    auto state = GetLaunchGuardState();
    if (!required) {
#ifdef PRICECHECK_GUARD_PROBE_STAGE
        if (!VerifyIdentityHooks()) return false;
        if (PRICECHECK_GUARD_PROBE_STAGE >= 2) {
            HANDLE probe = CreateThread(nullptr, 0, probe_monitor, nullptr, 0, nullptr);
            if (!probe) return false;
            CloseHandle(probe);
        }
#endif
        return true;
    }
    if (!state || !LaunchGuardLeaseValid()) { LaunchGuardFail(1); return false; }
    if (!VerifyIdentityHooks()) { LaunchGuardFail(8); return false; }
    // The active64 envelope is generated for the actual world handshake. Before
    // login verify the supported layout; the send gate validates the envelope,
    // rejects unchanged identity and confirms a complete rewritten send.
    if (!ValidateWorldIdentityLayout()) { LaunchGuardFail(9); return false; }
    BYTE expected[16]{};
    if (!CopyWorldIdentity(expected) || memcmp(expected, state->expected_world, 16)) {
        LaunchGuardFail(2); return false;
    }
    InterlockedOr(&state->flags, 1);
    InterlockedExchange64(&state->agent_tick, GetTickCount64());
    HANDLE thread = CreateThread(nullptr, 0, monitor, nullptr, 0, nullptr);
    if (!thread) { LaunchGuardFail(3); return false; }
    CloseHandle(thread); return true;
}
void LaunchGuardBeginWorld() {
    if (auto state = GetLaunchGuardState()) {
        InterlockedAnd(&state->flags, ~2);
        InterlockedExchange64(&state->world_opened_tick, GetTickCount64());
    }
}
void LaunchGuardWorldApplied() {
    auto state = GetLaunchGuardState();
    if (!state) return;
    if (!CopyWorldIdentity(state->observed_world)) { LaunchGuardFail(5); return; }
    InterlockedExchange64(&state->applied_tick, GetTickCount64());
    InterlockedIncrement(&state->world_count);
    InterlockedOr(&state->flags, 2);
}
