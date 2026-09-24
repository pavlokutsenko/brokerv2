#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include "disk_serial.h"

bool BuildAtaSerialFromEnvironment(BYTE (&out)[20]) {
    DWORD needed = GetEnvironmentVariableW(L"PRICECHECK_HW_DISK_SERIAL", nullptr, 0);
    if (needed < 2 || needed > 129) return false;
    std::wstring text(needed, L'\0');
    DWORD copied = GetEnvironmentVariableW(L"PRICECHECK_HW_DISK_SERIAL",
                                           text.data(), needed);
    if (!copied || copied >= needed) return false;
    text.resize(copied);
    for (wchar_t ch : text)
        if (ch < 32 || ch > 126) return false;
    for (size_t index = 0; index < 20; ++index)
        out[index ^ 1] = static_cast<BYTE>(text[index % text.size()]);
    return true;
}
