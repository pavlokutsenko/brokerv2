#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <windows.h>
#include <cstdio>
#include <string>
#include "minhook/include/MinHook.h"
#include "identity.h"
#include "proxy.h"
#include "trace.h"
#include "network_observe.h"
#include "world_send_probe.h"
#include "world_identity.h"
#include "launch_guard.h"
#include "proxy_pipe.h"
#include "early_child.h"
#include "anticheat_registry_trace.h"

namespace {
HANDLE ready_event = nullptr;
void trace_winsock_entry(const char* name, const char* first, const char* second) {
    HMODULE winsock = GetModuleHandleW(L"ws2_32.dll");
    if (!winsock) return;
    const auto* entry = reinterpret_cast<const unsigned char*>(GetProcAddress(winsock, name));
    if (!entry) return;
    unsigned char bytes[8]{};
    memcpy(bytes, entry, sizeof(bytes));
    unsigned word = 0;
    memcpy(&word, bytes, sizeof(word));
    TraceEvent(first, word);
    memcpy(&word, bytes + sizeof(word), sizeof(word));
    TraceEvent(second, word);
    if (bytes[0] != 0xE9) return;
    int displacement = 0;
    memcpy(&displacement, bytes + 1, sizeof(displacement));
    const auto* target = entry + 5 + displacement;
    TraceEvent(name[0] == 'r' ? "recv_jump_low" : "send_jump_low", static_cast<unsigned>(reinterpret_cast<uintptr_t>(target)));
    TraceEvent(name[0] == 'r' ? "recv_jump_high" : "send_jump_high", static_cast<unsigned>(reinterpret_cast<uintptr_t>(target) >> 32));
    MEMORY_BASIC_INFORMATION info{};
    if (!VirtualQuery(target, &info, sizeof(info))) {
        TraceEvent(name[0] == 'r' ? "recv_jump_query_error" : "send_jump_query_error", GetLastError());
        return;
    }
    std::string category(name);
    category += "_jump_";
    wchar_t path[MAX_PATH]{};
    DWORD length = info.Type == MEM_IMAGE
        ? GetModuleFileNameW(static_cast<HMODULE>(info.AllocationBase), path, MAX_PATH) : 0;
    if (length > 0 && length < MAX_PATH) {
        const wchar_t* base = wcsrchr(path, L'\\');
        for (const wchar_t* cursor = base ? base + 1 : path; *cursor && category.size() < 70; ++cursor)
            if ((*cursor >= L'a' && *cursor <= L'z') || (*cursor >= L'A' && *cursor <= L'Z') ||
                (*cursor >= L'0' && *cursor <= L'9') || *cursor == L'.' || *cursor == L'_')
                category += static_cast<char>(*cursor);
    } else category += "private";
    TraceEvent(category.c_str(), info.Type);
    const auto* region_end = static_cast<const unsigned char*>(info.BaseAddress) + info.RegionSize;
    if (target + 128 <= region_end) {
        for (unsigned index = 0; index < 32; ++index) {
            memcpy(&word, target + index * sizeof(word), sizeof(word));
            char label[32]{};
            sprintf_s(label, "%s_target_%02u", name, index);
            TraceEvent(label, word);
        }
    }
    if (target + 44 > region_end) return;
    const unsigned char* handler = nullptr;
    memcpy(&handler, target + 36, sizeof(handler));
    MEMORY_BASIC_INFORMATION handler_info{};
    if (!VirtualQuery(handler, &handler_info, sizeof(handler_info))) return;
    wchar_t handler_path[MAX_PATH]{};
    DWORD handler_length = handler_info.Type == MEM_IMAGE
        ? GetModuleFileNameW(static_cast<HMODULE>(handler_info.AllocationBase), handler_path, MAX_PATH) : 0;
    std::string handler_category(name);
    handler_category += "_handler_";
    if (handler_length > 0 && handler_length < MAX_PATH) {
        const wchar_t* base = wcsrchr(handler_path, L'\\');
        for (const wchar_t* cursor = base ? base + 1 : handler_path;
             *cursor && handler_category.size() < 70; ++cursor)
            if ((*cursor >= L'a' && *cursor <= L'z') || (*cursor >= L'A' && *cursor <= L'Z') ||
                (*cursor >= L'0' && *cursor <= L'9') || *cursor == L'.' || *cursor == L'_')
                handler_category += static_cast<char>(*cursor);
    } else handler_category += "private";
    TraceEvent(handler_category.c_str(), handler_info.Type);
    const auto* handler_end = static_cast<const unsigned char*>(handler_info.BaseAddress) + handler_info.RegionSize;
    if (handler + 128 <= handler_end) {
        for (unsigned index = 0; index < 32; ++index) {
            memcpy(&word, handler + index * sizeof(word), sizeof(word));
            char label[32]{};
            sprintf_s(label, "%s_handler_%02u", name, index);
            TraceEvent(label, word);
        }
    }
}
void log_status(const wchar_t* stage) {
    wchar_t directory[MAX_PATH]{};
    DWORD length = GetEnvironmentVariableW(L"LOCALAPPDATA", directory, MAX_PATH);
    if (length == 0 || length >= MAX_PATH) return;
    std::wstring path = std::wstring(directory) + L"\\PriceCheckCollector\\logs";
    CreateDirectoryW((std::wstring(directory) + L"\\PriceCheckCollector").c_str(), nullptr);
    CreateDirectoryW(path.c_str(), nullptr);
    path += L"\\agent-" + std::to_wstring(GetCurrentProcessId()) + L".txt";
    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    DWORD written = 0;
    WriteFile(file, stage, static_cast<DWORD>(wcslen(stage) * sizeof(wchar_t)), &written, nullptr);
    CloseHandle(file);
}
DWORD WINAPI start_agent(void*) {
    bool identity = GetEnvironmentVariableW(L"PRICECHECK_HW_UUID", nullptr, 0) > 1;
    bool pipe_proxy = GetEnvironmentVariableW(L"PRICECHECK_PROXY_PIPE", nullptr, 0) > 1;
    bool proxy = pipe_proxy || GetEnvironmentVariableW(L"PRICECHECK_PROXY_HOST", nullptr, 0) > 1;
    bool observe_network = GetEnvironmentVariableW(L"PRICECHECK_NETWORK_OBSERVE", nullptr, 0) > 1;
    bool early_root = IsEarlyLaunchRoot();
    bool world_send_probe =
        GetEnvironmentVariableW(L"PRICECHECK_TEST_WORLD_SEND_STACK", nullptr, 0) > 1;
    if (!identity && !proxy && !early_root) return 0;
    TraceStart();
    if (!early_root && !ConfigureWorldIdentity()) { log_status(L"invalid world identity"); return 10; }
    TraceEvent("agent_start", (identity ? 1u : 0u) | (proxy ? 2u : 0u) | (early_root ? 4u : 0u));
    WSADATA winsock{};
    if (WSAStartup(MAKEWORD(2, 2), &winsock) != 0) { log_status(L"WSAStartup failed"); return 1; }
    if (!early_root && GetEnvironmentVariableW(L"PRICECHECK_NETWORK_DETOUR_DUMP", nullptr, 0) > 1) {
        trace_winsock_entry("recv", "recv_entry_0", "recv_entry_1");
        trace_winsock_entry("send", "send_entry_0", "send_entry_1");
    }
    if (MH_Initialize() != MH_OK) { log_status(L"MH_Initialize failed"); return 2; }
    if (identity && !early_root && !InstallIdentityHooks()) { log_status(IdentityHookError()); return 3; }
    if (early_root && !InstallEarlyChildHook()) { log_status(L"early child hook installation failed"); return 8; }
    if (world_send_probe || WorldIdentityEnabled()) {
        const bool installed = InstallWorldSendProbeHooks();
        TraceEvent("world_probe_installed", installed ? 1u : 0u);
        if (!installed) { log_status(L"world send probe hook installation failed"); return 9; }
    }
    if (early_root && GetEnvironmentVariableW(L"PRICECHECK_TRACE_AA_REGISTRY", nullptr, 0) > 1)
        TraceEvent("aa_registry_hook", InstallAnticheatRegistryTraceHooks() ? 1u : 0u);
    if (proxy && !early_root && !(pipe_proxy ? InstallProxyPipeHooks() : InstallProxyHooks())) {
        log_status(L"proxy hook installation failed"); return 4;
    }
    if (observe_network && !proxy && !early_root &&
        !(WorldIdentityEnabled() ? InstallConnectionObserveHooks() : InstallNetworkObserveHooks())) {
        log_status(L"network observation hook installation failed"); return 7;
    }
    HMODULE self = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
                            reinterpret_cast<LPCWSTR>(&start_agent), &self)) {
        log_status(L"agent pin failed"); return 5;
    }
    if (MH_EnableHook(MH_ALL_HOOKS) != MH_OK) { log_status(L"MH_EnableHook failed"); return 6; }
    if (!early_root && !StartLaunchGuard()) { log_status(L"HWID protection verification failed"); return 11; }
    if (!early_root && !LaunchGuardAgentReady()) { log_status(L"HWID agent readiness not confirmed"); return 11; }
    std::wstring name = L"Local\\PriceCheckAgentReady_" + std::to_wstring(GetCurrentProcessId());
    ready_event = CreateEventW(nullptr, TRUE, FALSE, name.c_str());
    if (ready_event) SetEvent(ready_event);
    log_status(ready_event ? L"ready" : L"ready event failed");
    TraceEvent(ready_event ? "agent_ready" : "agent_ready_failed");
    return 0;
}
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(module);
        HANDLE thread = CreateThread(nullptr, 0, start_agent, nullptr, 0, nullptr);
        if (thread) CloseHandle(thread);
    }
    if (reason == DLL_PROCESS_DETACH && ready_event) CloseHandle(ready_event);
    return TRUE;
}

extern "C" __declspec(dllexport) LRESULT CALLBACK PriceCheckHookProc(int code, WPARAM wparam, LPARAM lparam) {
    return CallNextHookEx(nullptr, code, wparam, lparam);
}
