#pragma once

#include <cstdint>

struct LoginRuntime {
    std::uintptr_t objects_slot = 0;
    std::uintptr_t chunks = 0;
    int count = 0;
    void* connect = nullptr;
    void* select_server = nullptr;
    void* select_character = nullptr;
    void* library_cdo = nullptr;
    void* hud_roster = nullptr;
    void* mode_roster = nullptr;
};

// Called from the game-thread hook. Resolves reflected objects from the live
// Unreal tables, then caches only validated pointers for the current process.
bool ResolveLoginRuntime(std::uintptr_t base, std::uintptr_t end, LoginRuntime& runtime);
int LoginRuntimeFailure();
void* LoginObjectAt(const LoginRuntime& runtime, int index);
