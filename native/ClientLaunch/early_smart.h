#pragma once

#include <windows.h>
#include <cstdint>

void* InstallEarlySmartHook(HANDLE process, uintptr_t module,
                            const BYTE (&ata_serial)[20]);
void TraceEarlySmartCounters(HANDLE process, void* counters);
