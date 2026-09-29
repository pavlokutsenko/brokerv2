#include "launch_timeouts.h"
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cwchar>
#include "character_roster.h"
#include "launch_guard.h"
#include "login_runtime.h"

namespace {
constexpr std::uint32_t magic = 0x50434C47;
constexpr int process_event_slot = 77;
volatile LONG stage = 0;
DWORD login_tick = 0;
DWORD server_tick = 0;

struct Shared {
    std::uint32_t magic;
    std::uint16_t user_length;
    std::uint16_t password_length;
    volatile LONG status;
    std::int32_t server_id;
    std::int32_t character_slot;
    wchar_t values[256];
};
struct FString {
    const wchar_t* data;
    std::int32_t count;
    std::int32_t capacity;
};
struct LoginParams { FString user; FString password; };
using ProcessEvent = void(__fastcall*)(void*, void*, void*);

bool image_bounds(std::uintptr_t base, std::uintptr_t& end) {
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < sizeof(IMAGE_DOS_HEADER) ||
        dos->e_lfanew > 0x1000) return false;
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE ||
        nt->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64 ||
        nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC ||
        nt->OptionalHeader.SizeOfImage <= 0x1000 ||
        nt->OptionalHeader.SizeOfImage > 0x40000000) return false;
    end = base + nt->OptionalHeader.SizeOfImage;
    return end > base;
}

LONG call_login(Shared* shared) {
    if (!LaunchGuardAllowsLogin(false)) return -30;
    if (shared->magic != magic || shared->user_length == 0 || shared->password_length == 0 ||
        shared->user_length > 120 || shared->password_length > 120 ||
        shared->user_length + shared->password_length + 2 > 256) return -1;
    HMODULE game = GetModuleHandleW(L"lu4.bin");
    if (!game) return -2;
    auto base = reinterpret_cast<std::uintptr_t>(game);
    std::uintptr_t end = 0;
    if (!image_bounds(base, end)) return -3;

    LoginRuntime runtime{};
    if (!ResolveLoginRuntime(base, end, runtime)) return LoginRuntimeFailure();
    auto function = runtime.connect;
    auto cdo = runtime.library_cdo;
    if (!function || !cdo ||
        *reinterpret_cast<std::uint16_t*>(reinterpret_cast<std::uintptr_t>(function) + 0xB6) != 32 ||
        (*reinterpret_cast<std::uint32_t*>(reinterpret_cast<std::uintptr_t>(function) + 0xB0) & 0x2000) == 0 ||
        *reinterpret_cast<void**>(reinterpret_cast<std::uintptr_t>(function) + 0x20) !=
            *reinterpret_cast<void**>(reinterpret_cast<std::uintptr_t>(cdo) + 0x10)) return -5;

    const wchar_t* user = shared->values;
    const wchar_t* password = shared->values + shared->user_length + 1;
    if (user[shared->user_length] != 0 || password[shared->password_length] != 0) return -6;
    LoginParams params{
        { user, static_cast<std::int32_t>(shared->user_length + 1), static_cast<std::int32_t>(shared->user_length + 1) },
        { password, static_cast<std::int32_t>(shared->password_length + 1), static_cast<std::int32_t>(shared->password_length + 1) }
    };
    auto vtable = *reinterpret_cast<std::uintptr_t*>(cdo);
    auto callback = *reinterpret_cast<ProcessEvent*>(vtable + process_event_slot * sizeof(void*));
    auto callback_address = reinterpret_cast<std::uintptr_t>(callback);
    if (callback_address < base + 0x1000 || callback_address >= end) return -7;
    HMODULE self = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
            reinterpret_cast<LPCWSTR>(&call_login), &self)) return -8;
    auto hud_roster=runtime.hud_roster;
    auto mode_roster=runtime.mode_roster;
    if(!hud_roster || !mode_roster ||
        *reinterpret_cast<std::uint16_t*>(reinterpret_cast<std::uintptr_t>(hud_roster)+0xB6)!=16 ||
        *reinterpret_cast<std::uint16_t*>(reinterpret_cast<std::uintptr_t>(mode_roster)+0xB6)!=16) return -27;
    if(!StartCharacterRoster(reinterpret_cast<void*>(callback),hud_roster,mode_roster)) return -28;
    callback(cdo, function, &params);
    return 2;
}

LONG select_server(Shared* shared) {
    if (!LaunchGuardAllowsLogin(false)) return -30;
    if (shared->server_id < 0 || shared->server_id > 1000) return -11;
    auto base = reinterpret_cast<std::uintptr_t>(GetModuleHandleW(L"lu4.bin"));
    if (!base) return -12;
    std::uintptr_t end = 0;
    if (!image_bounds(base, end)) return -12;
    LoginRuntime runtime{};
    if (!ResolveLoginRuntime(base, end, runtime)) return -12;
    auto function = runtime.select_server;
    if (!function ||
        *reinterpret_cast<std::uint16_t*>(reinterpret_cast<std::uintptr_t>(function) + 0xB6) != 4) return -13;
    auto expected_class = *reinterpret_cast<std::uintptr_t*>(reinterpret_cast<std::uintptr_t>(function) + 0x20);
    for (int index = 0; index < runtime.count; ++index) {
        auto instance = LoginObjectAt(runtime, index);
        if (!instance) continue;
        auto object = reinterpret_cast<std::uintptr_t>(instance);
        if ((*reinterpret_cast<std::uint32_t*>(object + 8) & 0x10) != 0) continue;
        auto type = *reinterpret_cast<std::uintptr_t*>(object + 0x10);
        bool derived = false;
        for (int depth = 0; depth < 8 && type; ++depth) {
            if (type == expected_class) { derived = true; break; }
            type = *reinterpret_cast<std::uintptr_t*>(type + 0x40);
        }
        if (!derived) continue;
        auto vtable = *reinterpret_cast<std::uintptr_t*>(object);
        auto callback = *reinterpret_cast<ProcessEvent*>(vtable + process_event_slot * sizeof(void*));
        auto address = reinterpret_cast<std::uintptr_t>(callback);
        if (address < base + 0x1000 || address >= end) return -14;
        std::int32_t server_id = shared->server_id;
        callback(instance, function, &server_id);
        return 3;
    }
    return -15;
}

LONG select_character(Shared* shared) {
    if (!LaunchGuardAllowsLogin(false)) return -30;
    if (!LaunchGuardAllowsLogin(true)) return -31;
    int slot = shared->character_slot;
    if (slot < 0 || slot > 10) return -20;
    const int count=CharacterRosterCount();
    if(count==-1 || count==0) return -24; // Wait for the real character-list notification.
    if(count==-2) return -26;
    if(count>0 && slot>=count) slot=0;
    auto base = reinterpret_cast<std::uintptr_t>(GetModuleHandleW(L"lu4.bin"));
    if (!base) return -21;
    std::uintptr_t end = 0;
    if (!image_bounds(base, end)) return -21;
    LoginRuntime runtime{};
    if (!ResolveLoginRuntime(base, end, runtime)) return -21;
    auto function = runtime.select_character;
    if (!function ||
        *reinterpret_cast<std::uint16_t*>(reinterpret_cast<std::uintptr_t>(function) + 0xB6) != 4) return -22;
    auto expected_class = *reinterpret_cast<std::uintptr_t*>(reinterpret_cast<std::uintptr_t>(function) + 0x20);
    for (int index = 0; index < runtime.count; ++index) {
        auto instance = LoginObjectAt(runtime, index);
        if (!instance) continue;
        auto object = reinterpret_cast<std::uintptr_t>(instance);
        if ((*reinterpret_cast<std::uint32_t*>(object + 8) & 0x10) != 0) continue;
        auto type = *reinterpret_cast<std::uintptr_t*>(object + 0x10);
        bool derived = false;
        for (int depth = 0; depth < 8 && type; ++depth) {
            if (type == expected_class) { derived = true; break; }
            type = *reinterpret_cast<std::uintptr_t*>(type + 0x40);
        }
        if (!derived) continue;
        auto vtable = *reinterpret_cast<std::uintptr_t*>(object);
        auto callback = *reinterpret_cast<ProcessEvent*>(vtable + process_event_slot * sizeof(void*));
        auto address = reinterpret_cast<std::uintptr_t>(callback);
        if (address < base + 0x1000 || address >= end) return -23;
        callback(instance, function, &slot);
        CharacterRosterSelected(slot);
        StopCharacterRoster();
        return 4;
    }
    return -24;
}
}

extern "C" __declspec(dllexport) LRESULT CALLBACK PriceCheckHookProc(int code, WPARAM wparam, LPARAM lparam) {
    if (code == HC_ACTION && wparam == PM_REMOVE && lparam &&
        reinterpret_cast<MSG*>(lparam)->message == WM_NULL) {
        LONG current = stage;
        if (current == 0 && InterlockedCompareExchange(&stage, 1, 0) != 0)
            return CallNextHookEx(nullptr, code, wparam, lparam);
        if (current == 1 && (GetTickCount() - login_tick < 5000 ||
            InterlockedCompareExchange(&stage, 2, 1) != 1))
            return CallNextHookEx(nullptr, code, wparam, lparam);
        if (current == 2 && (GetTickCount() - server_tick < 2500 ||
            InterlockedCompareExchange(&stage, 3, 2) != 2))
            return CallNextHookEx(nullptr, code, wparam, lparam);
        if (current >= 3) return CallNextHookEx(nullptr, code, wparam, lparam);
        wchar_t name[128]{};
        swprintf_s(name, L"Local\\PriceCheckAutoLogin_%lu", GetCurrentProcessId());
        HANDLE mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, name);
        if (mapping) {
            auto shared = static_cast<Shared*>(MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(Shared)));
            if (shared) {
                LONG result = -9;
                if (current == 0) {
                    InterlockedExchange(&shared->status, 1);
                    __try { result = call_login(shared); }
                    __except (EXCEPTION_EXECUTE_HANDLER) { result = -10; }
                    if (result == 2) login_tick = GetTickCount();
                    else InterlockedExchange(&stage, 4);
                } else if (current == 1) {
                    __try { result = select_server(shared); }
                    __except (EXCEPTION_EXECUTE_HANDLER) { result = -16; }
                    if (result == 3) server_tick = GetTickCount();
                    else if (result == -15 && GetTickCount() - login_tick < launch_timeouts::server_selection_ms) {
                        InterlockedExchange(&stage, 1);
                        result = 2;
                    }
                    else InterlockedExchange(&stage, 4);
                } else {
                    __try { result = select_character(shared); }
                    __except (EXCEPTION_EXECUTE_HANDLER) { result = -25; }
                    if ((result == -24 || result == -31) &&
                        GetTickCount() - server_tick < launch_timeouts::character_selection_ms) {
                        InterlockedExchange(&stage, 2);
                        result = 3;
                    } else InterlockedExchange(&stage, 4);
                }
                InterlockedExchange(&shared->status, result);
                if(result<0) StopCharacterRoster();
                UnmapViewOfFile(shared);
            } else InterlockedExchange(&stage, current);
            CloseHandle(mapping);
        } else InterlockedExchange(&stage, current);
    }
    return CallNextHookEx(nullptr, code, wparam, lparam);
}
