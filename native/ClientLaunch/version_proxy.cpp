#define WIN32_LEAN_AND_MEAN
#include <windows.h>

// Adapted from the local LU4 version proxy experiment. Paths and payload
// selection are collector-owned; this file has no runtime dependency on it.

#include <string>

namespace {

constexpr wchar_t kAgentVariable[] = L"PRICECHECK_AGENT_PATH";

HMODULE g_real_version = nullptr;
using VerFindFileAFn = DWORD(WINAPI*)(DWORD, LPCSTR, LPCSTR, LPCSTR, LPSTR, PUINT, LPSTR, PUINT);
using VerFindFileWFn = DWORD(WINAPI*)(DWORD, LPCWSTR, LPCWSTR, LPCWSTR, LPWSTR, PUINT, LPWSTR, PUINT);
using VerInstallFileAFn = DWORD(WINAPI*)(DWORD, LPCSTR, LPCSTR, LPCSTR, LPCSTR, LPCSTR, LPSTR, PUINT);
using VerInstallFileWFn = DWORD(WINAPI*)(DWORD, LPCWSTR, LPCWSTR, LPCWSTR, LPCWSTR, LPCWSTR, LPWSTR, PUINT);
using GetFileVersionInfoSizeAFn = DWORD(WINAPI*)(LPCSTR, LPDWORD);
using GetFileVersionInfoSizeWFn = DWORD(WINAPI*)(LPCWSTR, LPDWORD);
using GetFileVersionInfoAFn = BOOL(WINAPI*)(LPCSTR, DWORD, DWORD, LPVOID);
using GetFileVersionInfoWFn = BOOL(WINAPI*)(LPCWSTR, DWORD, DWORD, LPVOID);
using GetFileVersionInfoSizeExAFn = DWORD(WINAPI*)(DWORD, LPCSTR, LPDWORD);
using GetFileVersionInfoSizeExWFn = DWORD(WINAPI*)(DWORD, LPCWSTR, LPDWORD);
using GetFileVersionInfoExAFn = BOOL(WINAPI*)(DWORD, LPCSTR, DWORD, DWORD, LPVOID);
using GetFileVersionInfoExWFn = BOOL(WINAPI*)(DWORD, LPCWSTR, DWORD, DWORD, LPVOID);
using VerLanguageNameAFn = DWORD(WINAPI*)(DWORD, LPSTR, DWORD);
using VerLanguageNameWFn = DWORD(WINAPI*)(DWORD, LPWSTR, DWORD);
using VerQueryValueAFn = BOOL(WINAPI*)(LPCVOID, LPCSTR, LPVOID*, PUINT);
using VerQueryValueWFn = BOOL(WINAPI*)(LPCVOID, LPCWSTR, LPVOID*, PUINT);
using GetFileVersionInfoByHandleFn = BOOL(WINAPI*)(DWORD, HANDLE, LPVOID*, PDWORD);

VerFindFileAFn g_ver_find_file_a = nullptr;
VerFindFileWFn g_ver_find_file_w = nullptr;
VerInstallFileAFn g_ver_install_file_a = nullptr;
VerInstallFileWFn g_ver_install_file_w = nullptr;
GetFileVersionInfoSizeAFn g_get_file_version_info_size_a = nullptr;
GetFileVersionInfoSizeWFn g_get_file_version_info_size_w = nullptr;
GetFileVersionInfoAFn g_get_file_version_info_a = nullptr;
GetFileVersionInfoWFn g_get_file_version_info_w = nullptr;
GetFileVersionInfoSizeExAFn g_get_file_version_info_size_ex_a = nullptr;
GetFileVersionInfoSizeExWFn g_get_file_version_info_size_ex_w = nullptr;
GetFileVersionInfoExAFn g_get_file_version_info_ex_a = nullptr;
GetFileVersionInfoExWFn g_get_file_version_info_ex_w = nullptr;
VerLanguageNameAFn g_ver_language_name_a = nullptr;
VerLanguageNameWFn g_ver_language_name_w = nullptr;
VerQueryValueAFn g_ver_query_value_a = nullptr;
VerQueryValueWFn g_ver_query_value_w = nullptr;
GetFileVersionInfoByHandleFn g_get_file_version_info_by_handle = nullptr;

void append_proxy_log(const wchar_t* message) {
    OutputDebugStringW(message ? message : L"");
}

std::wstring read_payload_path() {
    const DWORD needed = GetEnvironmentVariableW(kAgentVariable, nullptr, 0);
    if (needed <= 1 || needed > 32768) return {};
    std::wstring path(needed, L'\0');
    const DWORD copied = GetEnvironmentVariableW(kAgentVariable, path.data(), needed);
    if (copied == 0 || copied >= needed) return {};
    path.resize(copied);
    return path;
}

FARPROC get_real_proc(const char* name) {
    if (!g_real_version) {
        return nullptr;
    }
    return GetProcAddress(g_real_version, name);
}

bool load_real_version() {
    if (g_real_version) {
        return true;
    }
    wchar_t system_dir[MAX_PATH] = {};
    UINT count = GetSystemDirectoryW(system_dir, MAX_PATH);
    if (count == 0 || count >= MAX_PATH) {
        append_proxy_log(L"GetSystemDirectoryW failed");
        return false;
    }
    std::wstring path(system_dir);
    path += L"\\version.dll";
    g_real_version = LoadLibraryW(path.c_str());
    if (!g_real_version) {
        append_proxy_log(L"LoadLibraryW(system32\\\\version.dll) failed");
        return false;
    }
    g_ver_find_file_a = reinterpret_cast<VerFindFileAFn>(get_real_proc("VerFindFileA"));
    g_ver_find_file_w = reinterpret_cast<VerFindFileWFn>(get_real_proc("VerFindFileW"));
    g_ver_install_file_a = reinterpret_cast<VerInstallFileAFn>(get_real_proc("VerInstallFileA"));
    g_ver_install_file_w = reinterpret_cast<VerInstallFileWFn>(get_real_proc("VerInstallFileW"));
    g_get_file_version_info_size_a = reinterpret_cast<GetFileVersionInfoSizeAFn>(get_real_proc("GetFileVersionInfoSizeA"));
    g_get_file_version_info_size_w = reinterpret_cast<GetFileVersionInfoSizeWFn>(get_real_proc("GetFileVersionInfoSizeW"));
    g_get_file_version_info_a = reinterpret_cast<GetFileVersionInfoAFn>(get_real_proc("GetFileVersionInfoA"));
    g_get_file_version_info_w = reinterpret_cast<GetFileVersionInfoWFn>(get_real_proc("GetFileVersionInfoW"));
    g_get_file_version_info_size_ex_a = reinterpret_cast<GetFileVersionInfoSizeExAFn>(get_real_proc("GetFileVersionInfoSizeExA"));
    g_get_file_version_info_size_ex_w = reinterpret_cast<GetFileVersionInfoSizeExWFn>(get_real_proc("GetFileVersionInfoSizeExW"));
    g_get_file_version_info_ex_a = reinterpret_cast<GetFileVersionInfoExAFn>(get_real_proc("GetFileVersionInfoExA"));
    g_get_file_version_info_ex_w = reinterpret_cast<GetFileVersionInfoExWFn>(get_real_proc("GetFileVersionInfoExW"));
    g_ver_language_name_a = reinterpret_cast<VerLanguageNameAFn>(get_real_proc("VerLanguageNameA"));
    g_ver_language_name_w = reinterpret_cast<VerLanguageNameWFn>(get_real_proc("VerLanguageNameW"));
    g_ver_query_value_a = reinterpret_cast<VerQueryValueAFn>(get_real_proc("VerQueryValueA"));
    g_ver_query_value_w = reinterpret_cast<VerQueryValueWFn>(get_real_proc("VerQueryValueW"));
    g_get_file_version_info_by_handle = reinterpret_cast<GetFileVersionInfoByHandleFn>(get_real_proc("GetFileVersionInfoByHandle"));
    append_proxy_log(L"Loaded real system version.dll");
    return true;
}

DWORD WINAPI payload_loader_thread(LPVOID) {
    const std::wstring payload_path = read_payload_path();
    if (payload_path.empty()) {
        append_proxy_log(L"Payload path config missing or empty");
        return 0;
    }
    HMODULE payload = LoadLibraryW(payload_path.c_str());
    if (payload == nullptr) {
        std::wstring message = L"Payload LoadLibraryW failed | error=";
        message += std::to_wstring(static_cast<unsigned long>(GetLastError()));
        message += L" | path=";
        message += payload_path;
        append_proxy_log(message.c_str());
        return 0;
    }
    std::wstring message = L"Payload DLL loaded successfully | path=";
    message += payload_path;
    append_proxy_log(message.c_str());
    return 0;
}

} // namespace

extern "C" DWORD WINAPI ProxyVerFindFileA(
    DWORD flags,
    LPCSTR file_name,
    LPCSTR win_dir,
    LPCSTR app_dir,
    LPSTR cur_dir,
    PUINT cur_dir_len,
    LPSTR dest_dir,
    PUINT dest_dir_len) {
    return g_ver_find_file_a
        ? g_ver_find_file_a(flags, file_name, win_dir, app_dir, cur_dir, cur_dir_len, dest_dir, dest_dir_len)
        : 0;
}

extern "C" DWORD WINAPI ProxyVerFindFileW(
    DWORD flags,
    LPCWSTR file_name,
    LPCWSTR win_dir,
    LPCWSTR app_dir,
    LPWSTR cur_dir,
    PUINT cur_dir_len,
    LPWSTR dest_dir,
    PUINT dest_dir_len) {
    return g_ver_find_file_w
        ? g_ver_find_file_w(flags, file_name, win_dir, app_dir, cur_dir, cur_dir_len, dest_dir, dest_dir_len)
        : 0;
}

extern "C" DWORD WINAPI ProxyVerInstallFileA(
    DWORD flags,
    LPCSTR src_file_name,
    LPCSTR dest_file_name,
    LPCSTR src_dir,
    LPCSTR dest_dir,
    LPCSTR cur_dir,
    LPSTR tmp_file,
    PUINT tmp_file_len) {
    return g_ver_install_file_a
        ? g_ver_install_file_a(flags, src_file_name, dest_file_name, src_dir, dest_dir, cur_dir, tmp_file, tmp_file_len)
        : 0;
}

extern "C" DWORD WINAPI ProxyVerInstallFileW(
    DWORD flags,
    LPCWSTR src_file_name,
    LPCWSTR dest_file_name,
    LPCWSTR src_dir,
    LPCWSTR dest_dir,
    LPCWSTR cur_dir,
    LPWSTR tmp_file,
    PUINT tmp_file_len) {
    return g_ver_install_file_w
        ? g_ver_install_file_w(flags, src_file_name, dest_file_name, src_dir, dest_dir, cur_dir, tmp_file, tmp_file_len)
        : 0;
}

extern "C" DWORD WINAPI ProxyGetFileVersionInfoSizeA(LPCSTR filename, LPDWORD handle) {
    return g_get_file_version_info_size_a ? g_get_file_version_info_size_a(filename, handle) : 0;
}

extern "C" DWORD WINAPI ProxyGetFileVersionInfoSizeW(LPCWSTR filename, LPDWORD handle) {
    return g_get_file_version_info_size_w ? g_get_file_version_info_size_w(filename, handle) : 0;
}

extern "C" BOOL WINAPI ProxyGetFileVersionInfoA(LPCSTR filename, DWORD handle, DWORD len, LPVOID data) {
    return g_get_file_version_info_a ? g_get_file_version_info_a(filename, handle, len, data) : FALSE;
}

extern "C" BOOL WINAPI ProxyGetFileVersionInfoW(LPCWSTR filename, DWORD handle, DWORD len, LPVOID data) {
    return g_get_file_version_info_w ? g_get_file_version_info_w(filename, handle, len, data) : FALSE;
}

extern "C" DWORD WINAPI ProxyGetFileVersionInfoSizeExA(DWORD flags, LPCSTR filename, LPDWORD handle) {
    return g_get_file_version_info_size_ex_a ? g_get_file_version_info_size_ex_a(flags, filename, handle) : 0;
}

extern "C" DWORD WINAPI ProxyGetFileVersionInfoSizeExW(DWORD flags, LPCWSTR filename, LPDWORD handle) {
    return g_get_file_version_info_size_ex_w ? g_get_file_version_info_size_ex_w(flags, filename, handle) : 0;
}

extern "C" BOOL WINAPI ProxyGetFileVersionInfoExA(DWORD flags, LPCSTR filename, DWORD handle, DWORD len, LPVOID data) {
    return g_get_file_version_info_ex_a ? g_get_file_version_info_ex_a(flags, filename, handle, len, data) : FALSE;
}

extern "C" BOOL WINAPI ProxyGetFileVersionInfoExW(DWORD flags, LPCWSTR filename, DWORD handle, DWORD len, LPVOID data) {
    return g_get_file_version_info_ex_w ? g_get_file_version_info_ex_w(flags, filename, handle, len, data) : FALSE;
}

extern "C" BOOL WINAPI ProxyGetFileVersionInfoByHandle(DWORD flags, HANDLE file, LPVOID* data, PDWORD len) {
    return g_get_file_version_info_by_handle ? g_get_file_version_info_by_handle(flags, file, data, len) : FALSE;
}

extern "C" DWORD WINAPI ProxyVerLanguageNameA(DWORD lang, LPSTR buffer, DWORD size) {
    return g_ver_language_name_a ? g_ver_language_name_a(lang, buffer, size) : 0;
}

extern "C" DWORD WINAPI ProxyVerLanguageNameW(DWORD lang, LPWSTR buffer, DWORD size) {
    return g_ver_language_name_w ? g_ver_language_name_w(lang, buffer, size) : 0;
}

extern "C" BOOL WINAPI ProxyVerQueryValueA(LPCVOID block, LPCSTR sub_block, LPVOID* buffer, PUINT len) {
    return g_ver_query_value_a ? g_ver_query_value_a(block, sub_block, buffer, len) : FALSE;
}

extern "C" BOOL WINAPI ProxyVerQueryValueW(LPCVOID block, LPCWSTR sub_block, LPVOID* buffer, PUINT len) {
    return g_ver_query_value_w ? g_ver_query_value_w(block, sub_block, buffer, len) : FALSE;
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(module);
        load_real_version();
        if (GetEnvironmentVariableW(kAgentVariable, nullptr, 0) > 1) {
            HANDLE thread = CreateThread(nullptr, 0, payload_loader_thread, nullptr, 0, nullptr);
            if (thread != nullptr) CloseHandle(thread);
        }
    }
    return TRUE;
}
