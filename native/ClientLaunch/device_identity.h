#pragma once

#include <string>

bool InstallDeviceIdentityHooks(const std::wstring& seed);
std::wstring MapDeviceInstanceId(const std::wstring& original);
