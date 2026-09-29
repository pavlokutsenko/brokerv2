#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cwchar>
#include "../../native/ClientLaunch/minhook/include/MinHook.h"

namespace {
constexpr std::uint32_t magic = 0x50435343;
constexpr std::uint32_t timestamp = 0x956E0D97;
constexpr std::uint32_t image_size = 0x0DCEB000;
constexpr std::uintptr_t gobjects_rva = 0x080768E0;
constexpr int select_index = 19227;
constexpr int library_index = 25311;
constexpr int process_event_slot = 77;
struct Shared { std::uint32_t magic; volatile LONG status; volatile LONG server_id; volatile LONG calls; std::uint64_t object; };
using ProcessEvent = void(__fastcall*)(void*, void*, void*);
ProcessEvent original = nullptr;
void* target = nullptr;
void* function = nullptr;
Shared* shared = nullptr;
HANDLE mapping = nullptr;
bool hooked = false;

void* ObjectAt(std::uintptr_t chunks, int index) {
    const auto chunk = *reinterpret_cast<std::uintptr_t*>(chunks + static_cast<std::uintptr_t>(index / 65536) * 8);
    return chunk ? *reinterpret_cast<void**>(chunk + static_cast<std::uintptr_t>(index % 65536) * 0x18) : nullptr;
}
void __fastcall Observe(void* object, void* called, void* params) {
    if (shared && shared->magic == magic && called == function && params) {
        __try {
            shared->object = reinterpret_cast<std::uint64_t>(object);
            InterlockedExchange(&shared->server_id, *static_cast<const std::int32_t*>(params));
            InterlockedIncrement(&shared->calls);
        } __except (EXCEPTION_EXECUTE_HANDLER) { InterlockedExchange(&shared->status, -7); }
    }
    original(object, called, params);
}
void Initialize() {
    wchar_t name[96]{};
    swprintf_s(name, L"Local\\PriceCheckServerChoice_%lu", GetCurrentProcessId());
    mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, name);
    if (!mapping) return;
    shared = static_cast<Shared*>(MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(Shared)));
    if (!shared || shared->magic != magic) return;
    __try {
        const auto base = reinterpret_cast<std::uintptr_t>(GetModuleHandleW(L"lu4.bin"));
        if (!base) { shared->status = -1; return; }
        const auto nt = base + *reinterpret_cast<const std::int32_t*>(base + 0x3C);
        if (*reinterpret_cast<const std::uint32_t*>(nt + 8) != timestamp ||
            *reinterpret_cast<const std::uint32_t*>(nt + 0x50) != image_size) { shared->status = -2; return; }
        const auto chunks = *reinterpret_cast<const std::uintptr_t*>(base + gobjects_rva);
        function = ObjectAt(chunks, select_index);
        auto cdo = ObjectAt(chunks, library_index);
        if (!function || !cdo ||
            *reinterpret_cast<const std::int32_t*>(reinterpret_cast<std::uintptr_t>(function) + 0x0C) != select_index ||
            *reinterpret_cast<const std::uint16_t*>(reinterpret_cast<std::uintptr_t>(function) + 0xB6) != 4) {
            shared->status = -3; return;
        }
        auto vtable = *reinterpret_cast<const std::uintptr_t*>(cdo);
        target = *reinterpret_cast<void**>(vtable + process_event_slot * sizeof(void*));
        if (reinterpret_cast<std::uintptr_t>(target) < base + 0x1000 ||
            reinterpret_cast<std::uintptr_t>(target) >= base + image_size) { shared->status = -4; return; }
        auto result = MH_Initialize();
        if (result != MH_OK && result != MH_ERROR_ALREADY_INITIALIZED) { shared->status = -5; return; }
        if (MH_CreateHook(target, &Observe, reinterpret_cast<void**>(&original)) != MH_OK ||
            MH_EnableHook(target) != MH_OK) { shared->status = -6; return; }
        hooked = true;
        HMODULE self = nullptr;
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
            reinterpret_cast<LPCWSTR>(&Initialize), &self);
        InterlockedExchange(&shared->status, 1);
    } __except (EXCEPTION_EXECUTE_HANDLER) { InterlockedExchange(&shared->status, -8); }
}
}

extern "C" __declspec(dllexport) LRESULT CALLBACK PriceCheckHookProc(int code, WPARAM wparam, LPARAM lparam) {
    if (code == HC_ACTION && wparam == PM_REMOVE && lparam &&
        reinterpret_cast<MSG*>(lparam)->message == WM_NULL && !hooked) Initialize();
    return CallNextHookEx(nullptr, code, wparam, lparam);
}
