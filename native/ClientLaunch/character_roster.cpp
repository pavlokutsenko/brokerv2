#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cwchar>
#include <cstdio>
#include "minhook/include/MinHook.h"
#include "character_roster.h"
#include "../LU4Memory/include/lu4_protocol.h"

namespace {
constexpr std::uint32_t roster_magic=0x50435253;
struct Shared { std::uint32_t magic; volatile LONG count; volatile LONG selected; std::uint32_t version; };
struct Array { void* data; std::int32_t count; std::int32_t capacity; };
using ProcessEvent=void(__fastcall*)(void*,void*,void*);
ProcessEvent original=nullptr;
void* hooked_address=nullptr;
void* hud_function=nullptr;
void* mode_function=nullptr;
HANDLE mapping=nullptr;
Shared* shared=nullptr;
bool created=false;
std::int32_t roster_name=-1;
void* protected_page=nullptr;
DWORD previous_protection=0;

bool protect_roster_page(void* address, DWORD protection, DWORD* previous) {
    HANDLE device=CreateFileW(L"\\\\.\\LU4Memory", GENERIC_READ|GENERIC_WRITE,
        FILE_SHARE_READ|FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
    if(device==INVALID_HANDLE_VALUE) return false;
    LU4_VIRTUAL_MEMORY_REQUEST request{};
    request.Version=LU4_PROTOCOL_VERSION;
    request.ProcessId=GetCurrentProcessId();
    request.Address=reinterpret_cast<ULONGLONG>(address);
    request.Size=5;
    request.Protection=protection;
    DWORD returned=0;
    const bool okay=DeviceIoControl(device, IOCTL_LU4_PROTECT_PROCESS_MEMORY,
        &request, sizeof(request), &request, sizeof(request), &returned, nullptr) &&
        returned==sizeof(request) && request.Version==LU4_PROTOCOL_VERSION &&
        request.ProcessId==GetCurrentProcessId() && request.Reserved==0 &&
        request.Address<=reinterpret_cast<ULONGLONG>(address) &&
        reinterpret_cast<ULONGLONG>(address)-request.Address<request.Size;
    if(okay && previous) *previous=request.OldProtection;
    CloseHandle(device);
    return okay;
}

void roster_error(const char* stage, int code) {
    wchar_t local[MAX_PATH]{}, directory[MAX_PATH]{}, logs[MAX_PATH]{}, path[MAX_PATH]{};
    if (!GetEnvironmentVariableW(L"LOCALAPPDATA", local, MAX_PATH) ||
        swprintf_s(directory, L"%s\\PriceCheckCollector", local) < 0 ||
        swprintf_s(logs, L"%s\\logs", directory) < 0 ||
        swprintf_s(path, L"%s\\character-roster-%lu.txt", logs, GetCurrentProcessId()) < 0) return;
    CreateDirectoryW(directory, nullptr); CreateDirectoryW(logs, nullptr);
    FILE* file = nullptr;
    if (_wfopen_s(&file, path, L"w") == 0 && file) {
        fprintf(file, "%s status=%d\n", stage, code);
        fclose(file);
    }
}

bool IsRosterFunction(void* function) {
    if(function==hud_function || function==mode_function) return true;
    const auto address=reinterpret_cast<std::uintptr_t>(function);
    if(!address || *reinterpret_cast<const std::int32_t*>(address+0x18)!=roster_name ||
        *reinterpret_cast<const std::uint16_t*>(address+0xB6)!=16) return false;
    auto outer=*reinterpret_cast<std::uintptr_t*>(address+0x20);
    auto hud_class=*reinterpret_cast<std::uintptr_t*>(reinterpret_cast<std::uintptr_t>(hud_function)+0x20);
    auto mode_class=*reinterpret_cast<std::uintptr_t*>(reinterpret_cast<std::uintptr_t>(mode_function)+0x20);
    for(int depth=0;depth<8 && outer;depth++) {
        if(outer==hud_class || outer==mode_class) return true;
        outer=*reinterpret_cast<std::uintptr_t*>(outer+0x40);
    }
    return false;
}

void __fastcall ObserveRoster(void* object,void* function,void* params) {
    if(shared && shared->magic==roster_magic && params) {
        __try {
            if(IsRosterFunction(function)) {
            const auto* characters=static_cast<const Array*>(params);
            if(characters->count>=0 && characters->count<=7 && characters->capacity>=characters->count &&
                characters->capacity<=64 && (characters->count==0 || characters->data))
                InterlockedExchange(&shared->count,characters->count);
            }
        } __except(EXCEPTION_EXECUTE_HANDLER) { InterlockedExchange(&shared->count,-2); }
    }
    original(object,function,params); // Observation only; preserve the game's notification.
}
}

bool StartCharacterRoster(void* process_event,void* hud,void* mode) {
    wchar_t name[96]{};swprintf_s(name,L"Local\\PriceCheckCharacterRoster_%lu",GetCurrentProcessId());
    mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,name);
    if(!mapping) return true; // Existing manual-slot login retains its original path.
    shared=static_cast<Shared*>(MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(Shared)));
    if(!shared || shared->magic!=roster_magic || shared->version!=1) {
        roster_error("mapping", GetLastError()); StopCharacterRoster();return false;
    }
    hud_function=hud;mode_function=mode;hooked_address=process_event;
    roster_name=*reinterpret_cast<const std::int32_t*>(reinterpret_cast<std::uintptr_t>(hud)+0x18);
    auto init=MH_Initialize();
    if(init!=MH_OK && init!=MH_ERROR_ALREADY_INITIALIZED) {
        roster_error("initialize", init); StopCharacterRoster();return false;
    }
    const auto created_status = MH_CreateHook(process_event,&ObserveRoster,reinterpret_cast<void**>(&original));
    if(created_status!=MH_OK) { roster_error("create", created_status); StopCharacterRoster();return false; }
    created=true;
    // The updated client denies user-mode VirtualProtect on its image code.
    // Limit the driver-backed writable window to this roster hook's lifetime;
    // MinHook still owns the trampoline and performs its usual thread-safe patch.
    if(!protect_roster_page(process_event,PAGE_EXECUTE_READWRITE,&previous_protection)) {
        roster_error("protect", GetLastError()); StopCharacterRoster();return false;
    }
    protected_page=process_event;
    const auto enabled_status = MH_EnableHookOnWritablePage(process_event);
    if(enabled_status!=MH_OK) { roster_error("enable", enabled_status); StopCharacterRoster();return false; }
    return true;
}

int CharacterRosterCount() { return shared ? shared->count : -3; }
void CharacterRosterSelected(int slot) { if(shared) InterlockedExchange(&shared->selected,slot); }
void StopCharacterRoster() {
    if(created && hooked_address) {
        const auto disabled=MH_DisableHookOnWritablePage(hooked_address);
        if(disabled!=MH_OK && disabled!=MH_ERROR_DISABLED)
            roster_error("disable",disabled);
        else {
            const auto removed=MH_RemoveHook(hooked_address);
            if(removed!=MH_OK) roster_error("remove",removed);
        }
    }
    created=false;hooked_address=nullptr;
    if(protected_page) {
        if(!protect_roster_page(protected_page,previous_protection,nullptr))
            roster_error("restore",GetLastError());
        protected_page=nullptr;previous_protection=0;
    }
    if(shared) { UnmapViewOfFile(shared);shared=nullptr; }
    if(mapping) { CloseHandle(mapping);mapping=nullptr; }
}
