#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <mswsock.h>
#include <windows.h>
#include <memory>
#include <climits>
#include <string>
#include <unordered_map>
#include "minhook/include/MinHook.h"
#include "proxy_pipe.h"
#include "trace.h"

namespace {
using ConnectFn = int (WSAAPI*)(SOCKET, const sockaddr*, int);
using WsaConnectFn = int (WSAAPI*)(SOCKET, const sockaddr*, int, LPWSABUF, LPWSABUF, LPQOS, LPQOS);
using SendFn = int (WSAAPI*)(SOCKET, const char*, int, int);
using RecvFn = int (WSAAPI*)(SOCKET, char*, int, int);
using SelectFn = int (WSAAPI*)(int, fd_set*, fd_set*, fd_set*, const timeval*);
using CloseFn = int (WSAAPI*)(SOCKET);
using PeerFn = int (WSAAPI*)(SOCKET, sockaddr*, int*);
using SendToFn = int (WSAAPI*)(SOCKET, const char*, int, int, const sockaddr*, int);
using WsaSendToFn = int (WSAAPI*)(SOCKET, LPWSABUF, DWORD, LPDWORD, DWORD, const sockaddr*, int, LPWSAOVERLAPPED, LPWSAOVERLAPPED_COMPLETION_ROUTINE);
using IoctlFn = int (WSAAPI*)(SOCKET, DWORD, LPVOID, DWORD, LPVOID, DWORD, LPDWORD, LPWSAOVERLAPPED, LPWSAOVERLAPPED_COMPLETION_ROUTINE);
ConnectFn real_connect = nullptr;
WsaConnectFn real_wsa_connect = nullptr;
SendFn real_send = nullptr;
RecvFn real_recv = nullptr;
SelectFn real_select = nullptr;
CloseFn real_close = nullptr;
PeerFn real_peer = nullptr;
SendToFn real_sendto = nullptr;
WsaSendToFn real_wsa_sendto = nullptr;
IoctlFn real_ioctl = nullptr;
LPFN_CONNECTEX real_connectex = nullptr;
const GUID connectex_guid = WSAID_CONNECTEX;
std::wstring pipe_path;
SRWLOCK map_gate = SRWLOCK_INIT;

struct Session {
    HANDLE pipe;
    sockaddr_in destination;
    ~Session() { if (pipe != INVALID_HANDLE_VALUE) CloseHandle(pipe); }
};
std::unordered_map<SOCKET, std::shared_ptr<Session>> sessions;

std::shared_ptr<Session> find_session(SOCKET socket) {
    AcquireSRWLockShared(&map_gate);
    auto it = sessions.find(socket);
    auto value = it == sessions.end() ? nullptr : it->second;
    ReleaseSRWLockShared(&map_gate);
    return value;
}
bool external_address(const sockaddr* address, int length) {
    if (!address || length < sizeof(sockaddr)) return false;
    if (address->sa_family == AF_INET6) {
        if (length < sizeof(sockaddr_in6)) return true;
        const IN6_ADDR loopback = IN6ADDR_LOOPBACK_INIT;
        return memcmp(&reinterpret_cast<const sockaddr_in6*>(address)->sin6_addr, &loopback, sizeof(loopback)) != 0;
    }
    if (address->sa_family != AF_INET || length < sizeof(sockaddr_in)) return false;
    return (ntohl(reinterpret_cast<const sockaddr_in*>(address)->sin_addr.s_addr) >> 24) != 127;
}
int pipe_connect(SOCKET socket, const sockaddr* address, int length) {
    if (!external_address(address, length)) return -2;
    if (address->sa_family != AF_INET || length < sizeof(sockaddr_in)) { WSASetLastError(WSAEAFNOSUPPORT); return SOCKET_ERROR; }
    int type = 0, size = sizeof(type);
    if (getsockopt(socket, SOL_SOCKET, SO_TYPE, reinterpret_cast<char*>(&type), &size) != 0 || type != SOCK_STREAM) {
        WSASetLastError(WSAEACCES); return SOCKET_ERROR;
    }
    const auto* dest = reinterpret_cast<const sockaddr_in*>(address);
    if (dest->sin_port == 0 || dest->sin_addr.s_addr == 0) { WSASetLastError(WSAEINVAL); return SOCKET_ERROR; }
    if (!WaitNamedPipeW(pipe_path.c_str(), 5000)) { TraceEvent("pipe_wait_error", GetLastError()); WSASetLastError(WSAENETDOWN); return SOCKET_ERROR; }
    HANDLE pipe = CreateFileW(pipe_path.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
    if (pipe == INVALID_HANDLE_VALUE) { TraceEvent("pipe_open_error", GetLastError()); WSASetLastError(WSAENETDOWN); return SOCKET_ERROR; }
    BYTE header[10]{'P', 'C', 'P', 'X'};
    memcpy(header + 4, &dest->sin_addr, 4);
    memcpy(header + 8, &dest->sin_port, 2);
    DWORD count = 0;
    BYTE status = 0;
    bool okay = WriteFile(pipe, header, sizeof(header), &count, nullptr) && count == sizeof(header) &&
        ReadFile(pipe, &status, 1, &count, nullptr) && count == 1 && status == 1;
    if (!okay) { TraceEvent("pipe_tunnel_failed", GetLastError()); CloseHandle(pipe); WSASetLastError(WSAECONNREFUSED); return SOCKET_ERROR; }
    auto session = std::make_shared<Session>();
    session->pipe = pipe;
    session->destination = *dest;
    AcquireSRWLockExclusive(&map_gate);
    sessions[socket] = session;
    ReleaseSRWLockExclusive(&map_gate);
    TraceEvent("pipe_tunnel_ready", ntohs(dest->sin_port));
    return 0;
}
int WSAAPI hooked_connect(SOCKET socket, const sockaddr* address, int length) {
    int result = pipe_connect(socket, address, length);
    return result == -2 ? real_connect(socket, address, length) : result;
}
int WSAAPI hooked_wsa_connect(SOCKET socket, const sockaddr* address, int length,
                             LPWSABUF caller, LPWSABUF callee, LPQOS send_qos, LPQOS receive_qos) {
    int result = pipe_connect(socket, address, length);
    return result == -2 ? real_wsa_connect(socket, address, length, caller, callee, send_qos, receive_qos) : result;
}
int WSAAPI hooked_send(SOCKET socket, const char* data, int size, int flags) {
    auto session = find_session(socket);
    if (!session) return real_send(socket, data, size, flags);
    if (size < 0) { WSASetLastError(WSAEINVAL); return SOCKET_ERROR; }
    if (size == 15 && ntohs(session->destination.sin_port) == 7782 && data) {
        unsigned word = 0;
        memcpy(&word, data, 4); TraceEvent("world_send_0", word);
        memcpy(&word, data + 4, 4); TraceEvent("world_send_1", word);
        memcpy(&word, data + 8, 4); TraceEvent("world_send_2", word);
        memcpy(&word, data + 11, 4); TraceEvent("world_send_3", word);
    }
    DWORD sent = 0;
    if (!WriteFile(session->pipe, data, static_cast<DWORD>(size), &sent, nullptr)) {
        TraceEvent("pipe_send_error", GetLastError()); WSASetLastError(WSAECONNRESET); return SOCKET_ERROR;
    }
    TraceEvent("pipe_send", sent);
    return static_cast<int>(sent);
}
int WSAAPI hooked_recv(SOCKET socket, char* data, int size, int flags) {
    auto session = find_session(socket);
    if (!session) return real_recv(socket, data, size, flags);
    if (size < 0) { WSASetLastError(WSAEINVAL); return SOCKET_ERROR; }
    DWORD received = 0, available = 0;
    BOOL okay = (flags & MSG_PEEK)
        ? PeekNamedPipe(session->pipe, data, static_cast<DWORD>(size), &received, &available, nullptr)
        : ReadFile(session->pipe, data, static_cast<DWORD>(size), &received, nullptr);
    if (!okay) {
        if (GetLastError() == ERROR_BROKEN_PIPE) { TraceEvent("pipe_recv_closed"); return 0; }
        TraceEvent("pipe_recv_error", GetLastError());
        WSASetLastError(WSAECONNRESET); return SOCKET_ERROR;
    }
    TraceEvent("pipe_recv", received);
    if (received == 13 && ntohs(session->destination.sin_port) == 7782 && data) {
        unsigned word = 0;
        memcpy(&word, data, 4); TraceEvent("world_reply_0", word);
        memcpy(&word, data + 4, 4); TraceEvent("world_reply_1", word);
        memcpy(&word, data + 8, 4); TraceEvent("world_reply_2", word);
        TraceEvent("world_reply_3", static_cast<unsigned char>(data[12]));
    }
    return static_cast<int>(received);
}
int WSAAPI hooked_peer(SOCKET socket, sockaddr* address, int* length) {
    auto session = find_session(socket);
    if (!session) return real_peer(socket, address, length);
    if (!address || !length || *length < sizeof(sockaddr_in)) { WSASetLastError(WSAEFAULT); return SOCKET_ERROR; }
    memcpy(address, &session->destination, sizeof(sockaddr_in));
    *length = sizeof(sockaddr_in);
    return 0;
}
int WSAAPI hooked_close(SOCKET socket) {
    AcquireSRWLockExclusive(&map_gate);
    sessions.erase(socket);
    ReleaseSRWLockExclusive(&map_gate);
    return real_close(socket);
}
bool pipe_ready(const std::shared_ptr<Session>& session) {
    DWORD available = 0;
    return !PeekNamedPipe(session->pipe, nullptr, 0, nullptr, &available, nullptr) || available > 0;
}
int WSAAPI hooked_select(int ignored, fd_set* read, fd_set* write, fd_set* except, const timeval* timeout) {
    fd_set old_read{}, old_write{}, old_except{};
    if (read) old_read = *read;
    if (write) old_write = *write;
    if (except) old_except = *except;
    bool has_virtual = false;
    for (fd_set* list : {read ? &old_read : nullptr, write ? &old_write : nullptr, except ? &old_except : nullptr})
        if (list) for (u_int i = 0; i < list->fd_count; ++i) if (find_session(list->fd_array[i])) has_virtual = true;
    if (!has_virtual) return real_select(ignored, read, write, except, timeout);
    ULONGLONG deadline = timeout ? GetTickCount64() + static_cast<ULONGLONG>(timeout->tv_sec) * 1000 +
        static_cast<ULONGLONG>(timeout->tv_usec / 1000) : ULLONG_MAX;
    for (;;) {
        fd_set real_read{}, real_write{}, real_except{};
        fd_set ready_read{}, ready_write{}, ready_except{};
        auto split = [&](const fd_set* source, fd_set& real, fd_set& ready, int kind) {
            if (!source) return;
            for (u_int i = 0; i < source->fd_count; ++i) {
                SOCKET socket = source->fd_array[i];
                auto session = find_session(socket);
                if (!session) FD_SET(socket, &real);
                else if (kind == 1 || (kind == 0 && pipe_ready(session))) FD_SET(socket, &ready);
            }
        };
        split(read ? &old_read : nullptr, real_read, ready_read, 0);
        split(write ? &old_write : nullptr, real_write, ready_write, 1);
        split(except ? &old_except : nullptr, real_except, ready_except, 2);
        timeval zero{};
        int result = (real_read.fd_count || real_write.fd_count || real_except.fd_count)
            ? real_select(0, &real_read, &real_write, &real_except, &zero) : 0;
        if (result == SOCKET_ERROR) return result;
        for (u_int i = 0; i < real_read.fd_count; ++i) FD_SET(real_read.fd_array[i], &ready_read);
        for (u_int i = 0; i < real_write.fd_count; ++i) FD_SET(real_write.fd_array[i], &ready_write);
        for (u_int i = 0; i < real_except.fd_count; ++i) FD_SET(real_except.fd_array[i], &ready_except);
        int count = ready_read.fd_count + ready_write.fd_count + ready_except.fd_count;
        if (count || GetTickCount64() >= deadline) {
            TraceEvent("pipe_select", (ready_read.fd_count << 16) | ready_write.fd_count);
            if (read) *read = ready_read;
            if (write) *write = ready_write;
            if (except) *except = ready_except;
            return count;
        }
        Sleep(10);
    }
}
int WSAAPI hooked_sendto(SOCKET socket, const char* data, int size, int flags, const sockaddr* target, int length) {
    if (external_address(target, length)) { WSASetLastError(WSAEACCES); return SOCKET_ERROR; }
    return real_sendto(socket, data, size, flags, target, length);
}
int WSAAPI hooked_wsa_sendto(SOCKET socket, LPWSABUF buffers, DWORD count, LPDWORD sent, DWORD flags,
                             const sockaddr* target, int length, LPWSAOVERLAPPED overlapped,
                             LPWSAOVERLAPPED_COMPLETION_ROUTINE completion) {
    if (external_address(target, length)) { WSASetLastError(WSAEACCES); return SOCKET_ERROR; }
    return real_wsa_sendto(socket, buffers, count, sent, flags, target, length, overlapped, completion);
}
BOOL PASCAL hooked_connectex(SOCKET socket, const sockaddr* address, int length, PVOID data,
                             DWORD data_length, LPDWORD sent, LPOVERLAPPED overlapped) {
    if (external_address(address, length)) { WSASetLastError(WSAEOPNOTSUPP); return FALSE; }
    return real_connectex(socket, address, length, data, data_length, sent, overlapped);
}
int WSAAPI hooked_ioctl(SOCKET socket, DWORD code, LPVOID input, DWORD input_size, LPVOID output,
                        DWORD output_size, LPDWORD bytes, LPWSAOVERLAPPED overlapped,
                        LPWSAOVERLAPPED_COMPLETION_ROUTINE completion) {
    int result = real_ioctl(socket, code, input, input_size, output, output_size, bytes, overlapped, completion);
    if (result == 0 && code == SIO_GET_EXTENSION_FUNCTION_POINTER && input && input_size >= sizeof(GUID) &&
        output && output_size >= sizeof(LPFN_CONNECTEX) && memcmp(input, &connectex_guid, sizeof(GUID)) == 0) {
        auto function = static_cast<LPFN_CONNECTEX*>(output);
        real_connectex = *function;
        *function = hooked_connectex;
    }
    return result;
}
}

bool InstallProxyPipeHooks() {
    DWORD size = GetEnvironmentVariableW(L"PRICECHECK_PROXY_PIPE", nullptr, 0);
    if (size < 16 || size > 128) return false;
    std::wstring name(size, L'\0');
    if (GetEnvironmentVariableW(L"PRICECHECK_PROXY_PIPE", name.data(), size) != size - 1) return false;
    name.resize(size - 1);
    for (wchar_t character : name)
        if (!(character >= L'0' && character <= L'9') && !(character >= L'A' && character <= L'Z') &&
            !(character >= L'a' && character <= L'z') && character != L'_') return false;
    pipe_path = L"\\\\.\\pipe\\" + name;
    return MH_CreateHookApi(L"ws2_32.dll", "connect", hooked_connect, reinterpret_cast<void**>(&real_connect)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "WSAConnect", hooked_wsa_connect, reinterpret_cast<void**>(&real_wsa_connect)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "WSAIoctl", hooked_ioctl, reinterpret_cast<void**>(&real_ioctl)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "send", hooked_send, reinterpret_cast<void**>(&real_send)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "recv", hooked_recv, reinterpret_cast<void**>(&real_recv)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "select", hooked_select, reinterpret_cast<void**>(&real_select)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "closesocket", hooked_close, reinterpret_cast<void**>(&real_close)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "getpeername", hooked_peer, reinterpret_cast<void**>(&real_peer)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "sendto", hooked_sendto, reinterpret_cast<void**>(&real_sendto)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "WSASendTo", hooked_wsa_sendto, reinterpret_cast<void**>(&real_wsa_sendto)) == MH_OK;
}
