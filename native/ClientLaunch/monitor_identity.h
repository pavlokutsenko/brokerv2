#pragma once
#include <windows.h>
#include <string>

bool InitializeMonitorIdentity(const std::wstring& uuid);
void MapMonitorEdid(const std::wstring& uuid, const std::wstring& instance, BYTE* data, DWORD size);
void PatchMonitorRegistryValue(HKEY key, LPCWSTR name, DWORD type, BYTE* data, DWORD size);
void MapAdapterMac(const BYTE* seed, const char* adapterName, BYTE* target);
std::wstring MonitorSerialText(const std::wstring& uuid, const std::wstring& instance);
