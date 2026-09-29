#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <cstdio>
#include <cstdint>
#include "minhook/include/MinHook.h"
#include "trace.h"
#include "world_send_probe.h"
#include "world_identity.h"
#include "launch_guard.h"

namespace {
using SendFn = int (WSAAPI*)(SOCKET, const char*, int, int);
using WsaSendFn = int (WSAAPI*)(SOCKET, LPWSABUF, DWORD, LPDWORD, DWORD,
                                LPWSAOVERLAPPED, LPWSAOVERLAPPED_COMPLETION_ROUTINE);
SendFn real_send = nullptr;
SendFn lower_real_send = nullptr;
WsaSendFn real_wsa_send = nullptr;
volatile LONG captured = 0;
volatile LONG lower_captured = 0;
thread_local const char* pending_world_source = nullptr;
thread_local int pending_world_length = 0;
bool world_port(unsigned port) { return port == 7782 || port == 9971 || port == 9972 || port == 9973; }
bool source_length(int length) { return length == 35 || length == 37; }
bool wire_length(int length) { return length == 67 || length == 69; }

void inspect(SOCKET socket, unsigned length) {
    if (length <= 100) TraceEvent("world_probe_send_length", length);
    if ((!source_length(length) && !wire_length(length)) ||
        InterlockedCompareExchange(&captured, 0, 0)) return;
    sockaddr_storage peer{};
    int peer_length = sizeof(peer);
    if (getpeername(socket, reinterpret_cast<sockaddr*>(&peer), &peer_length) != 0 ||
        peer.ss_family != AF_INET) {TraceEvent("world_probe_peer_error", WSAGetLastError()); return;}
    const unsigned port = ntohs(reinterpret_cast<sockaddr_in*>(&peer)->sin_port);
    TraceEvent("world_probe_peer_port", port);
    if (!world_port(port)) return;
    if (InterlockedCompareExchange(&captured, 1, 0) == 0) {
        TraceEvent("world_probe_original_size", length);
        TraceWorldSendStack();
    }
}

int WSAAPI hooked_send(SOCKET socket, const char* data, int length, int flags) {
    if (!LaunchGuardAllowsNetwork()) { LaunchGuardFail(6); WSASetLastError(WSAEACCES); return SOCKET_ERROR; }
    if (data && source_length(length)) {
        unsigned hash = 2166136261u;
        for (int index = 0; index < length; ++index)
            hash = (hash ^ static_cast<unsigned char>(data[index])) * 16777619u;
        TraceEvent("world_probe_send37_fnv", hash);
        if (WorldIdentityEnabled() || GetEnvironmentVariableW(L"PRICECHECK_TEST_WORLD_BUFFER_LAYOUT", nullptr, 0) > 1) {
            sockaddr_in peer{};
            int peer_length = sizeof(peer);
            if (getpeername(socket, reinterpret_cast<sockaddr*>(&peer), &peer_length) == 0 &&
                peer.sin_family == AF_INET && world_port(ntohs(peer.sin_port)))
            {
                pending_world_source = data;
                pending_world_length = length;
            }
        }
    }
    if (data && length > 0) inspect(socket, static_cast<unsigned>(length));
    const int result = real_send(socket, data, length, flags);
    pending_world_source = nullptr;
    pending_world_length = 0;
    return result;
}

int WSAAPI hooked_wsa_send(SOCKET socket, LPWSABUF buffers, DWORD count,
                           LPDWORD sent, DWORD flags, LPWSAOVERLAPPED overlapped,
                           LPWSAOVERLAPPED_COMPLETION_ROUTINE completion) {
    if (!LaunchGuardAllowsNetwork()) { LaunchGuardFail(6); WSASetLastError(WSAEACCES); return SOCKET_ERROR; }
    unsigned total = 0;
    if (buffers) for (DWORD index = 0; index < count && total <= 69; ++index)
        total += buffers[index].len;
    inspect(socket, total);
    return real_wsa_send(socket, buffers, count, sent, flags, overlapped, completion);
}

int WSAAPI hooked_lower_send(SOCKET socket, const char* data, int length, int flags) {
    char isolated[69]{};
    const int previous_error = WSAGetLastError();
    const bool isolate = pending_world_source && wire_length(length) &&
        length == pending_world_length + 32 && WorldIdentityEnabled();
    if (isolate) LaunchGuardBeginWorld();
    const bool rewritten = isolate && RewriteWorldIdentity(data, length, isolated);
    WSASetLastError(previous_error);
    if (isolate && !rewritten) { LaunchGuardFail(7); WSASetLastError(WSAEOPNOTSUPP); return SOCKET_ERROR; }
    if (data && length > 0 && length <= 100) TraceEvent("world_lower_send_length", length);
    if (data && (length == 32 || source_length(length) || wire_length(length)) &&
        InterlockedCompareExchange(&lower_captured, 0, 0) == 0) {
        sockaddr_in peer{};
        int peer_length = sizeof(peer);
        if (getpeername(socket, reinterpret_cast<sockaddr*>(&peer), &peer_length) == 0 &&
            peer.sin_family == AF_INET && world_port(ntohs(peer.sin_port)) &&
            InterlockedCompareExchange(&lower_captured, 1, 0) == 0) {
            TraceEvent("world_lower_original_size", length);
            TraceWorldSendStack();
            if (wire_length(length) &&
                GetEnvironmentVariableW(L"PRICECHECK_TEST_WORLD_SLOT_HASH", nullptr, 0) > 1) {
                unsigned hash = 2166136261u;
                for (int index = 0; index < length; ++index)
                    hash = (hash ^ static_cast<unsigned char>(data[index])) * 16777619u;
                TraceEvent("world_slot_at_send", hash);
            }
            if (wire_length(length) && pending_world_source &&
                GetEnvironmentVariableW(L"PRICECHECK_TEST_WORLD_BUFFER_LAYOUT", nullptr, 0) > 1) {
                const auto source = reinterpret_cast<uintptr_t>(pending_world_source);
                const auto wire = reinterpret_cast<uintptr_t>(data);
                TraceEvent("world_buffer_same", source == wire ? 1u : 0u);
                const auto delta = static_cast<int64_t>(wire) - static_cast<int64_t>(source);
                if (delta >= -0x100000 && delta <= 0x100000)
                    TraceEvent("world_buffer_delta", static_cast<unsigned>(delta));
                const NT_TIB* tib = reinterpret_cast<const NT_TIB*>(NtCurrentTeb());
                const auto low = reinterpret_cast<uintptr_t>(tib->StackLimit);
                const auto high = reinterpret_cast<uintptr_t>(tib->StackBase);
                TraceEvent("world_source_on_stack", source >= low && source < high ? 1u : 0u);
                TraceEvent("world_wire_on_stack", wire >= low && wire < high ? 1u : 0u);
                MEMORY_BASIC_INFORMATION source_region{};
                MEMORY_BASIC_INFORMATION wire_region{};
                if (VirtualQuery(pending_world_source, &source_region, sizeof(source_region)) &&
                    VirtualQuery(data, &wire_region, sizeof(wire_region))) {
                    TraceEvent("world_source_region_type", source_region.Type);
                    TraceEvent("world_wire_region_type", wire_region.Type);
                    TraceEvent("world_same_allocation",
                               source_region.AllocationBase == wire_region.AllocationBase ? 1u : 0u);
                    if (wire_region.Type == MEM_IMAGE) {
                        const auto image_base = reinterpret_cast<uintptr_t>(wire_region.AllocationBase);
                        const auto image_rva = wire - image_base;
                        if (image_rva <= 0x20000000)
                            TraceEvent("world_wire_image_rva", static_cast<unsigned>(image_rva));
                        const HMODULE clmods = GetModuleHandleW(L"clmods64.dll");
                        TraceEvent("world_wire_clmods_image",
                                   clmods && reinterpret_cast<uintptr_t>(clmods) == image_base ? 1u : 0u);
                    }
                }
                pending_world_source = nullptr;
            }
            if (wire_length(length) &&
                GetEnvironmentVariableW(L"PRICECHECK_TEST_AA_WINDOW_HASH", nullptr, 0) > 1) {
                for (unsigned start = 0; start + 59 <= 69; ++start) {
                    unsigned hash = 2166136261u;
                    for (unsigned offset = 0; offset < 59; ++offset)
                        hash = (hash ^ static_cast<unsigned char>(data[start + offset])) * 16777619u;
                    char category[40]{};
                    sprintf_s(category, "world_window59_%u", start);
                    TraceEvent(category, hash);
                }
                for (unsigned start = 0; start + 40 <= 69; ++start) {
                    unsigned hash = 2166136261u;
                    for (unsigned offset = 0; offset < 40; ++offset)
                        hash = (hash ^ static_cast<unsigned char>(data[start + offset])) * 16777619u;
                    char category[40]{};
                    sprintf_s(category, "world_window40_%u", start);
                    TraceEvent(category, hash);
                }
            }
        }
    }
    const int result = lower_real_send(socket, rewritten ? isolated : data, length, flags);
    if (rewritten && result == length) LaunchGuardWorldApplied();
    return result;
}
}

// Stable, read-only discovery for the collector's existing send trampoline.
// Consumers validate the actual relay and retained instruction before use.
struct PriceCheckSendLayout {
    uint32_t magic, size, version, reserved;
    SendFn callback;
    SendFn* original_slot;
};
extern "C" __declspec(dllexport) const PriceCheckSendLayout PriceCheckSendHookLayout = {
    0x5043534C, sizeof(PriceCheckSendLayout), 1, 0, hooked_send, &real_send
};

bool InstallWorldSendProbeHooks() {
    const bool installed = MH_CreateHookApi(L"ws2_32.dll", "send", hooked_send,
        reinterpret_cast<void**>(&real_send)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "WSASend", hooked_wsa_send,
        reinterpret_cast<void**>(&real_wsa_send)) == MH_OK;
    if (!installed) return false;
    HMODULE winsock = GetModuleHandleW(L"ws2_32.dll");
    auto* body = winsock ? reinterpret_cast<unsigned char*>(GetProcAddress(winsock, "send")) : nullptr;
    if (!body || body[0] != 0xE9) {
        TraceEvent("world_lower_hook_available", 0);
        return !WorldIdentityEnabled();
    }
    const bool lower = MH_CreateHook(body + 5, hooked_lower_send,
        reinterpret_cast<void**>(&lower_real_send)) == MH_OK;
    TraceEvent("world_lower_hook_available", lower ? 1u : 0u);
    return lower;
}
