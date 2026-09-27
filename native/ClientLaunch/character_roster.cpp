#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cwchar>
#include "minhook/include/MinHook.h"
#include "character_roster.h"

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
    if(!shared || shared->magic!=roster_magic || shared->version!=1) { StopCharacterRoster();return false; }
    hud_function=hud;mode_function=mode;hooked_address=process_event;
    roster_name=*reinterpret_cast<const std::int32_t*>(reinterpret_cast<std::uintptr_t>(hud)+0x18);
    auto init=MH_Initialize();
    if(init!=MH_OK && init!=MH_ERROR_ALREADY_INITIALIZED) { StopCharacterRoster();return false; }
    if(MH_CreateHook(process_event,&ObserveRoster,reinterpret_cast<void**>(&original))!=MH_OK) { StopCharacterRoster();return false; }
    created=true;
    if(MH_EnableHook(process_event)!=MH_OK) { StopCharacterRoster();return false; }
    return true;
}

int CharacterRosterCount() { return shared ? shared->count : -3; }
void CharacterRosterSelected(int slot) { if(shared) InterlockedExchange(&shared->selected,slot); }
void StopCharacterRoster() {
    if(created && hooked_address) { MH_DisableHook(hooked_address);MH_RemoveHook(hooked_address); }
    created=false;hooked_address=nullptr;
    if(shared) { UnmapViewOfFile(shared);shared=nullptr; }
    if(mapping) { CloseHandle(mapping);mapping=nullptr; }
}
