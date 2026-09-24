#pragma once
#include <windows.h>
#include <cstdint>

// Test-only, in-process IAT callback in the protected child. No DLL is loaded there.
void* InstallEarlyRegistryHook(HANDLE process, uintptr_t kernelbase);
void TraceEarlyRegistryCounters(HANDLE process, void* counters);
