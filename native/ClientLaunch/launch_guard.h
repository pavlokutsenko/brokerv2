#pragma once
#include <windows.h>
#include <cstdint>

struct LaunchGuardState {
    std::uint32_t magic, schema, pid;
    volatile LONG error;
    volatile LONG64 heartbeat;
    volatile LONG flags, world_count;
    BYTE observed_world[16], expected_world[16];
    std::uint64_t process_created;
    volatile LONG64 agent_tick;
    volatile LONG required, controller_ready;
    volatile LONG64 applied_tick, world_opened_tick;
};
static_assert(sizeof(LaunchGuardState) == 104);
constexpr unsigned launch_guard_magic = 0x50434744;
bool StartLaunchGuard();
LaunchGuardState* GetLaunchGuardState();
bool LaunchGuardLeaseValid();
void LaunchGuardBeginWorld();
bool LaunchGuardAllowsLogin(bool character);
bool LaunchGuardAllowsNetwork();
void LaunchGuardWorldApplied();
void LaunchGuardFail(LONG error);
