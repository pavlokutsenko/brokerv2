#pragma once
#include <cstddef>

bool ConfigureWorldIdentity();
bool WorldIdentityEnabled();
// Rewrites a copy of the verified 37 -> 69 world request, never kernel state.
bool RewriteWorldIdentity(const char* source, size_t size, char (&output)[69]);
