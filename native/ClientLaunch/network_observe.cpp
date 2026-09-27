#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include "minhook/include/MinHook.h"
#include "network_observe.h"
#include "trace.h"

namespace {
using ConnectFn = int (WSAAPI*)(SOCKET, const sockaddr*, int);
using SendFn = int (WSAAPI*)(SOCKET, const char*, int, int);
using RecvFn = int (WSAAPI*)(SOCKET, char*, int, int);
using WsaSendFn = int (WSAAPI*)(SOCKET, LPWSABUF, DWORD, LPDWORD, DWORD, LPWSAOVERLAPPED, LPWSAOVERLAPPED_COMPLETION_ROUTINE);
using WsaRecvFn = int (WSAAPI*)(SOCKET, LPWSABUF, DWORD, LPDWORD, LPDWORD, LPWSAOVERLAPPED, LPWSAOVERLAPPED_COMPLETION_ROUTINE);
using SendToFn = int (WSAAPI*)(SOCKET, const char*, int, int, const sockaddr*, int);
using RecvFromFn = int (WSAAPI*)(SOCKET, char*, int, int, sockaddr*, int*);
using SelectFn = int (WSAAPI*)(int, fd_set*, fd_set*, fd_set*, const timeval*);
using GetSockOptFn = int (WSAAPI*)(SOCKET, int, int, char*, int*);
using GetSockNameFn = int (WSAAPI*)(SOCKET, sockaddr*, int*);
using IoctlSocketFn = int (WSAAPI*)(SOCKET, long, u_long*);
ConnectFn real_connect = nullptr;
ConnectFn real_bind = nullptr;
SendFn real_send = nullptr;
RecvFn real_recv = nullptr;
SendFn lower_real_send = nullptr;
RecvFn lower_real_recv = nullptr;
WsaSendFn real_wsa_send = nullptr;
WsaRecvFn real_wsa_recv = nullptr;
SendToFn real_sendto = nullptr;
RecvFromFn real_recvfrom = nullptr;
SelectFn real_select = nullptr;
GetSockOptFn real_getsockopt = nullptr;
GetSockNameFn real_getsockname = nullptr;
IoctlSocketFn real_ioctlsocket = nullptr;

unsigned port_of(const sockaddr* address, int length) {
    if (!address || length < sizeof(sockaddr)) return 0;
    if (address->sa_family == AF_INET && length >= sizeof(sockaddr_in))
        return ntohs(reinterpret_cast<const sockaddr_in*>(address)->sin_port);
    if (address->sa_family == AF_INET6 && length >= sizeof(sockaddr_in6))
        return ntohs(reinterpret_cast<const sockaddr_in6*>(address)->sin6_port);
    return 0;
}
unsigned peer_port(SOCKET socket) {
    sockaddr_storage address{};
    int length = sizeof(address);
    return getpeername(socket, reinterpret_cast<sockaddr*>(&address), &length) == 0
        ? port_of(reinterpret_cast<const sockaddr*>(&address), length) : 0;
}
int WSAAPI hooked_connect(SOCKET socket, const sockaddr* address, int length) {
    TraceEvent("observe_connect_port", port_of(address, length));
    TraceEvent("observe_connect_family", address ? address->sa_family : 0);
    if (address && address->sa_family == AF_INET && length >= sizeof(sockaddr_in))
        TraceEvent("observe_connect_ipv4", ntohl(reinterpret_cast<const sockaddr_in*>(address)->sin_addr.s_addr));
    int result = real_connect(socket, address, length);
    const int error = WSAGetLastError();
    TraceEvent("observe_connect_error", result == 0 ? 0 : error);
    if (result == 0 && port_of(address, length) == 7782) {
        sockaddr_in local{};
        int local_size = sizeof(local);
        if (getsockname(socket, reinterpret_cast<sockaddr*>(&local), &local_size) == 0)
            TraceEvent("world_local_ipv4", ntohl(local.sin_addr.s_addr));
    }
    WSASetLastError(error); return result;
}
int WSAAPI hooked_bind(SOCKET socket, const sockaddr* address, int length) {
    const int result = real_bind(socket, address, length);
    const int error = WSAGetLastError();
    TraceEvent("observe_bind_family", address ? address->sa_family : 0);
    TraceEvent("observe_bind_port", port_of(address, length));
    TraceEvent("observe_bind_error", result == 0 ? 0 : error);
    WSASetLastError(error); return result;
}
int WSAAPI hooked_send(SOCKET socket, const char* data, int size, int flags) {
    TraceEvent("observe_send", (peer_port(socket) << 16) | (static_cast<unsigned>(size) & 0xffff));
    if (size == 15 && peer_port(socket) == 7782 && data) {
        unsigned word = 0;
        memcpy(&word, data, 4); TraceEvent("world_send_0", word);
        memcpy(&word, data + 4, 4); TraceEvent("world_send_1", word);
        memcpy(&word, data + 8, 4); TraceEvent("world_send_2", word);
        memcpy(&word, data + 11, 4); TraceEvent("world_send_3", word);
    }
    return real_send(socket, data, size, flags);
}
int WSAAPI hooked_recv(SOCKET socket, char* data, int size, int flags) {
    int result = real_recv(socket, data, size, flags);
    const int error = WSAGetLastError();
    TraceEvent("observe_recv", (peer_port(socket) << 16) | (static_cast<unsigned>(result) & 0xffff));
    if (result == 13 && peer_port(socket) == 7782 && data) {
        unsigned word = 0;
        memcpy(&word, data, 4); TraceEvent("world_reply_0", word);
        memcpy(&word, data + 4, 4); TraceEvent("world_reply_1", word);
        memcpy(&word, data + 8, 4); TraceEvent("world_reply_2", word);
        TraceEvent("world_reply_3", static_cast<unsigned char>(data[12]));
    }
    TraceEvent("observe_recv_error", result == SOCKET_ERROR ? error : 0);
    WSASetLastError(error); return result;
}
int WSAAPI lower_send(SOCKET socket, const char* data, int size, int flags) {
    if (peer_port(socket) == 7782 && size == 15 && data) {
        unsigned word = 0;
        memcpy(&word, data, sizeof(word));
        TraceEvent("lower_world_send", word);
    }
    return lower_real_send(socket, data, size, flags);
}
int WSAAPI lower_recv(SOCKET socket, char* data, int size, int flags) {
    int result = lower_real_recv(socket, data, size, flags);
    if (peer_port(socket) == 7782 && result == 13 && data) {
        unsigned word = 0;
        memcpy(&word, data, sizeof(word));
        TraceEvent("lower_world_recv", word);
    }
    return result;
}
int WSAAPI hooked_wsa_send(SOCKET socket, LPWSABUF buffers, DWORD count, LPDWORD sent,
                           DWORD flags, LPWSAOVERLAPPED overlapped, LPWSAOVERLAPPED_COMPLETION_ROUTINE completion) {
    TraceEvent("observe_wsasend", peer_port(socket));
    return real_wsa_send(socket, buffers, count, sent, flags, overlapped, completion);
}
int WSAAPI hooked_wsa_recv(SOCKET socket, LPWSABUF buffers, DWORD count, LPDWORD received,
                           LPDWORD flags, LPWSAOVERLAPPED overlapped, LPWSAOVERLAPPED_COMPLETION_ROUTINE completion) {
    int result = real_wsa_recv(socket, buffers, count, received, flags, overlapped, completion);
    TraceEvent("observe_wsarecv", peer_port(socket));
    return result;
}
int WSAAPI hooked_sendto(SOCKET socket, const char* data, int size, int flags, const sockaddr* target, int length) {
    TraceEvent("observe_sendto", port_of(target, length));
    return real_sendto(socket, data, size, flags, target, length);
}
int WSAAPI hooked_recvfrom(SOCKET socket, char* data, int size, int flags, sockaddr* source, int* length) {
    int result = real_recvfrom(socket, data, size, flags, source, length);
    TraceEvent("observe_recvfrom", result >= 0 && length ? port_of(source, *length) : 0);
    return result;
}
int WSAAPI hooked_select(int ignored, fd_set* read, fd_set* write, fd_set* except, const timeval* timeout) {
    unsigned detail = ((read ? read->fd_count : 0) << 16) | (write ? write->fd_count : 0);
    int result = real_select(ignored, read, write, except, timeout);
    TraceEvent("observe_select", detail);
    return result;
}
int WSAAPI hooked_getsockopt(SOCKET socket, int level, int option, char* value, int* length) {
    int result = real_getsockopt(socket, level, option, value, length);
    TraceEvent("observe_sockopt", (static_cast<unsigned>(option) & 0xffff) |
        ((result == 0 ? 0u : 1u) << 16));
    return result;
}
int WSAAPI hooked_getsockname(SOCKET socket, sockaddr* address, int* length) {
    int result = real_getsockname(socket, address, length);
    TraceEvent("observe_sockname", result == 0 ? 0 : WSAGetLastError());
    return result;
}
int WSAAPI hooked_ioctlsocket(SOCKET socket, long command, u_long* argument) {
    TraceEvent("observe_ioctlsocket", static_cast<unsigned>(command));
    return real_ioctlsocket(socket, command, argument);
}
}

bool InstallConnectionObserveHooks() {
    // The world protection owns send/WSASend and send+5. Never replace those.
    return MH_CreateHookApi(L"ws2_32.dll", "connect", hooked_connect, reinterpret_cast<void**>(&real_connect)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "bind", hooked_bind, reinterpret_cast<void**>(&real_bind)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "recv", hooked_recv, reinterpret_cast<void**>(&real_recv)) == MH_OK;
}

bool InstallNetworkObserveHooks() {
    bool installed = MH_CreateHookApi(L"ws2_32.dll", "connect", hooked_connect, reinterpret_cast<void**>(&real_connect)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "send", hooked_send, reinterpret_cast<void**>(&real_send)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "recv", hooked_recv, reinterpret_cast<void**>(&real_recv)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "WSASend", hooked_wsa_send, reinterpret_cast<void**>(&real_wsa_send)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "WSARecv", hooked_wsa_recv, reinterpret_cast<void**>(&real_wsa_recv)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "sendto", hooked_sendto, reinterpret_cast<void**>(&real_sendto)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "recvfrom", hooked_recvfrom, reinterpret_cast<void**>(&real_recvfrom)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "select", hooked_select, reinterpret_cast<void**>(&real_select)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "getsockopt", hooked_getsockopt, reinterpret_cast<void**>(&real_getsockopt)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "getsockname", hooked_getsockname, reinterpret_cast<void**>(&real_getsockname)) == MH_OK &&
        MH_CreateHookApi(L"ws2_32.dll", "ioctlsocket", hooked_ioctlsocket, reinterpret_cast<void**>(&real_ioctlsocket)) == MH_OK;
    if (!installed || GetEnvironmentVariableW(L"PRICECHECK_NETWORK_LOWER_OBSERVE", nullptr, 0) <= 1) return installed;
    HMODULE winsock = GetModuleHandleW(L"ws2_32.dll");
    if (!winsock) return false;
    auto* send_body = reinterpret_cast<unsigned char*>(GetProcAddress(winsock, "send"));
    auto* recv_body = reinterpret_cast<unsigned char*>(GetProcAddress(winsock, "recv"));
    if (!send_body || !recv_body || send_body[0] != 0xE9 || recv_body[0] != 0xE9) return false;
    return MH_CreateHook(send_body + 5, lower_send, reinterpret_cast<void**>(&lower_real_send)) == MH_OK &&
        MH_CreateHook(recv_body + 5, lower_recv, reinterpret_cast<void**>(&lower_real_recv)) == MH_OK;
}
