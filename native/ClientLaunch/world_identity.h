#pragma once
#include <cstddef>

bool ConfigureWorldIdentity();
bool WorldIdentityEnabled();
bool ValidateWorldIdentity();
bool ValidateWorldIdentityLayout();
bool CopyWorldIdentity(BYTE (&value)[16]);
// Rewrites a copy of the verified 35 -> 67 or 37 -> 69 world request, never kernel state.
bool RewriteWorldIdentity(const char* source, size_t size, char (&output)[69]);
