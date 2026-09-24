#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <windows.h>
#include <iphlpapi.h>
#include <cstdlib>
#include <cstring>
#include "minhook/include/MinHook.h"
#include "router_identity.h"
#include "trace.h"

namespace {
using SendArpFn = DWORD(WINAPI*)(IPAddr, IPAddr, PULONG, PULONG);
SendArpFn real_send_arp = nullptr;
BYTE router_mac[6]{};
IPAddr gateway_ip = 0;

DWORD WINAPI hooked_send_arp(IPAddr destination, IPAddr source, PULONG mac, PULONG length) {
    DWORD result = real_send_arp(destination, source, mac, length);
    TraceEvent("send_arp_result", result);
    if (result == NO_ERROR && destination == gateway_ip && mac && length && *length >= sizeof(router_mac))
        memcpy(mac, router_mac, sizeof(router_mac));
    return result;
}
}

bool InstallRouterIdentityHooks() {
    wchar_t gateway_text[32]{};
    DWORD gateway_length = GetEnvironmentVariableW(L"PRICECHECK_HW_ROUTER_IP", gateway_text, 32);
    if (gateway_length == 0) return true;
    wchar_t* gateway_end = nullptr;
    unsigned long parsed_gateway = wcstoul(gateway_text, &gateway_end, 10);
    if (*gateway_end || parsed_gateway > 0xFFFFFFFFUL) return false;
    gateway_ip = static_cast<IPAddr>(parsed_gateway);
    wchar_t value[32]{};
    DWORD length = GetEnvironmentVariableW(L"PRICECHECK_HW_ROUTER_MAC", value, 32);
    if (length != 12) return false;
    for (size_t i = 0; i < 6; ++i) {
        wchar_t pair[3]{value[i * 2], value[i * 2 + 1], 0};
        wchar_t* end = nullptr;
        unsigned long parsed = wcstoul(pair, &end, 16);
        if (end != pair + 2 || parsed > 255) return false;
        router_mac[i] = static_cast<BYTE>(parsed);
    }
    return MH_CreateHookApi(L"iphlpapi.dll", "SendARP", hooked_send_arp,
                            reinterpret_cast<void**>(&real_send_arp)) == MH_OK;
}
