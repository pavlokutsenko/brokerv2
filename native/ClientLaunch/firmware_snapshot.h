#pragma once

#include <windows.h>
#include <vector>

// Builds the same length-preserving SMBIOS view used by the normal identity
// agent, using the current launch template's environment values.
bool BuildEarlyFirmwareSnapshot(std::vector<BYTE>& bytes, unsigned& patched_fields);
