#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <mswsock.h>
#include <windows.h>
#include <string>
#include <vector>
#include <memory>
#include "minhook/include/MinHook.h"
#include "proxy.h"
#include "trace.h"

namespace {
using ConnectFn = int (WSAAPI*)(SOCKET, const sockaddr*, int);
using WsaConnectFn = int (WSAAPI*)(SOCKET, const sockaddr*, int, LPWSABUF, LPWSABUF, LPQOS, LPQOS);
using WsaIoctlFn = int (WSAAPI*)(SOCKET, DWORD, LPVOID, DWORD, LPVOID, DWORD, LPDWORD, LPWSAOVERLAPPED, LPWSAOVERLAPPED_COMPLETION_ROUTINE);
using SendToFn = int (WSAAPI*)(SOCKET, const char*, int, int, const sockaddr*, int);
using WsaSendToFn = int (WSAAPI*)(SOCKET, LPWSABUF, DWORD, LPDWORD, DWORD, const sockaddr*, int, LPWSAOVERLAPPED, LPWSAOVERLAPPED_COMPLETION_ROUTINE);
ConnectFn real_connect = nullptr;
WsaConnectFn real_wsa_connect = nullptr;
WsaIoctlFn real_wsa_ioctl = nullptr;
SendToFn real_sendto = nullptr;
WsaSendToFn real_wsa_sendto = nullptr;
LPFN_CONNECTEX real_connect_ex = nullptr;
const GUID connect_ex_id = WSAID_CONNECTEX;
sockaddr_in proxy_address{};
std::string authorization;
thread_local bool bypass = false;
volatile LONG connectivity_probe_ran = 0;

std::wstring env(const wchar_t* name) {
    DWORD size = GetEnvironmentVariableW(name, nullptr, 0);
    if (size == 0 || size > 4096) return {};
    std::wstring result(size, L'\0');
    DWORD copied = GetEnvironmentVariableW(name, result.data(), size);
    if (copied == 0 || copied >= size) return {};
    result.resize(copied);
    return result;
}

std::string utf8(const std::wstring& value) {
    int count = WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (count <= 0) return {};
    std::string result(count, '\0');
    WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), result.data(), count, nullptr, nullptr);
    return result;
}

std::string base64(const std::string& input) {
    constexpr char alphabet[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    std::string output;
    for (size_t i = 0; i < input.size(); i += 3) {
        unsigned value = static_cast<unsigned char>(input[i]) << 16;
        if (i + 1 < input.size()) value |= static_cast<unsigned char>(input[i + 1]) << 8;
        if (i + 2 < input.size()) value |= static_cast<unsigned char>(input[i + 2]);
        output += alphabet[(value >> 18) & 63];
        output += alphabet[(value >> 12) & 63];
        output += i + 1 < input.size() ? alphabet[(value >> 6) & 63] : '=';
        output += i + 2 < input.size() ? alphabet[value & 63] : '=';
    }
    return output;
}

bool write_all(SOCKET socket, const char* data, int size) {
    while (size > 0) {
        WSABUF buffer{static_cast<ULONG>(size), const_cast<char*>(data)};
        DWORD sent = 0;
        if (WSASend(socket, &buffer, 1, &sent, 0, nullptr, nullptr) != 0 || sent == 0) return false;
        data += sent;
        size -= static_cast<int>(sent);
    }
    return true;
}

int read_some(SOCKET socket, char* data, int size) {
    WSABUF buffer{static_cast<ULONG>(size), data};
    DWORD flags = 0, read = 0;
    return WSARecv(socket, &buffer, 1, &read, &flags, nullptr, nullptr) == 0 ? static_cast<int>(read) : -1;
}

struct Relay {
    SOCKET listener;
    sockaddr_in destination;
};

DWORD WINAPI relay_thread(void* raw) {
    std::unique_ptr<Relay> relay(static_cast<Relay*>(raw));
    fd_set set;
    FD_ZERO(&set);
    FD_SET(relay->listener, &set);
    timeval accept_timeout{15, 0};
    if (select(0, &set, nullptr, nullptr, &accept_timeout) != 1) {
        TraceEvent("relay_accept_timeout");
        closesocket(relay->listener);
        return 0;
    }
    SOCKET client = accept(relay->listener, nullptr, nullptr);
    closesocket(relay->listener);
    if (client == INVALID_SOCKET) { TraceEvent("relay_accept_failed", WSAGetLastError()); return 0; }

    SOCKET upstream = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (upstream == INVALID_SOCKET) { closesocket(client); return 0; }
    DWORD timeout = 15000;
    setsockopt(upstream, SOL_SOCKET, SO_RCVTIMEO, reinterpret_cast<const char*>(&timeout), sizeof(timeout));
    setsockopt(upstream, SOL_SOCKET, SO_SNDTIMEO, reinterpret_cast<const char*>(&timeout), sizeof(timeout));
    bypass = true;
    int connected = real_connect(upstream, reinterpret_cast<const sockaddr*>(&proxy_address), sizeof(proxy_address));
    bypass = false;
    TraceEvent("proxy_tcp_result", connected == 0 ? 1 : 0);
    if (connected != 0) { closesocket(upstream); closesocket(client); return 0; }

    char ip[INET_ADDRSTRLEN]{};
    InetNtopA(AF_INET, &relay->destination.sin_addr, ip, sizeof(ip));
    std::string endpoint = std::string(ip) + ":" + std::to_string(ntohs(relay->destination.sin_port));
    std::string request = "CONNECT " + endpoint + " HTTP/1.1\r\nHost: " + endpoint + "\r\n";
    if (!authorization.empty()) request += "Proxy-Authorization: Basic " + authorization + "\r\n";
    request += "Proxy-Connection: Keep-Alive\r\n\r\n";
    if (!write_all(upstream, request.data(), static_cast<int>(request.size()))) {
        closesocket(upstream); closesocket(client); return 0;
    }
    std::string response;
    char buffer[8192];
    while (response.find("\r\n\r\n") == std::string::npos && response.size() < 16384) {
        int count = read_some(upstream, buffer, sizeof(buffer));
        if (count <= 0) { closesocket(upstream); closesocket(client); return 0; }
        response.append(buffer, count);
    }
    auto line_end = response.find("\r\n");
    auto header_end = response.find("\r\n\r\n");
    auto status_begin = line_end == std::string::npos ? std::string::npos : response.find(' ');
    bool accepted = status_begin != std::string::npos && status_begin < line_end &&
        response.compare(status_begin + 1, 3, "200") == 0 &&
        (status_begin + 4 == line_end || response[status_begin + 4] == ' ');
    TraceEvent("proxy_http_result", accepted ? 200 : 0);
    if (header_end == std::string::npos || !accepted) {
        closesocket(upstream); closesocket(client); return 0;
    }
    if (response.size() > header_end + 4) {
        auto offset = header_end + 4;
        if (!write_all(client, response.data() + offset, static_cast<int>(response.size() - offset))) {
            closesocket(upstream); closesocket(client); return 0;
        }
    }
    timeout = 0;
    setsockopt(upstream, SOL_SOCKET, SO_RCVTIMEO, reinterpret_cast<const char*>(&timeout), sizeof(timeout));
    setsockopt(upstream, SOL_SOCKET, SO_SNDTIMEO, reinterpret_cast<const char*>(&timeout), sizeof(timeout));
    for (;;) {
        FD_ZERO(&set);
        FD_SET(client, &set);
        FD_SET(upstream, &set);
        if (select(0, &set, nullptr, nullptr, nullptr) <= 0) break;
        SOCKET source = FD_ISSET(client, &set) ? client : upstream;
        SOCKET target = source == client ? upstream : client;
        int count = read_some(source, buffer, sizeof(buffer));
        if (count <= 0 || !write_all(target, buffer, count)) break;
    }
    closesocket(upstream);
    closesocket(client);
    return 0;
}

bool should_redirect(SOCKET socket, const sockaddr* address, int length) {
    if (bypass || !address || length < sizeof(sockaddr_in)) return false;
    if (address->sa_family != AF_INET) return false;
    int type = 0, size = sizeof(type);
    if (getsockopt(socket, SOL_SOCKET, SO_TYPE, reinterpret_cast<char*>(&type), &size) != 0 || type != SOCK_STREAM) return false;
    auto destination = reinterpret_cast<const sockaddr_in*>(address);
    unsigned long ip = ntohl(destination->sin_addr.s_addr);
    return ip != 0 && (ip >> 24) != 127 && destination->sin_port != 0;
}

bool forbidden_direct(SOCKET socket, const sockaddr* address, int length) {
    if (!address || length < sizeof(sockaddr)) return false;
    if (address->sa_family == AF_INET6 && length >= sizeof(sockaddr_in6)) {
        auto* ipv6 = reinterpret_cast<const sockaddr_in6*>(address);
        const IN6_ADDR loopback = IN6ADDR_LOOPBACK_INIT;
        return memcmp(&ipv6->sin6_addr, &loopback, sizeof(loopback)) != 0;
    }
    if (address->sa_family != AF_INET || length < sizeof(sockaddr_in)) return false;
    auto* ipv4 = reinterpret_cast<const sockaddr_in*>(address);
    if ((ntohl(ipv4->sin_addr.s_addr) >> 24) == 127) return false;
    int type = 0, size = sizeof(type);
    return getsockopt(socket, SOL_SOCKET, SO_TYPE, reinterpret_cast<char*>(&type), &size) == 0 && type == SOCK_DGRAM;
}

unsigned endpoint_detail(SOCKET socket, const sockaddr* address, int length) {
    if (!address || length < sizeof(sockaddr)) return 0;
    int type = 0, type_size = sizeof(type);
    if (getsockopt(socket, SOL_SOCKET, SO_TYPE, reinterpret_cast<char*>(&type), &type_size) != 0) type = 0;
    unsigned port = 0;
    if (address->sa_family == AF_INET && length >= sizeof(sockaddr_in))
        port = ntohs(reinterpret_cast<const sockaddr_in*>(address)->sin_port);
    if (address->sa_family == AF_INET6 && length >= sizeof(sockaddr_in6))
        port = ntohs(reinterpret_cast<const sockaddr_in6*>(address)->sin6_port);
    return ((static_cast<unsigned>(address->sa_family) & 0xff) << 24) |
        ((static_cast<unsigned>(type) & 0xff) << 16) | (port & 0xffff);
}

unsigned probe_remote_connectivity(const sockaddr_in& target) {
    SOCKET client = ::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (client == INVALID_SOCKET) return WSAGetLastError();
    unsigned long nonblocking = 1;
    if (ioctlsocket(client, FIONBIO, &nonblocking) != 0) {
        unsigned error = WSAGetLastError();
        closesocket(client);
        return error;
    }
    int result = real_connect(client, reinterpret_cast<const sockaddr*>(&target), sizeof(target));
    unsigned error = result == 0 ? 0 : WSAGetLastError();
    if (error == WSAEWOULDBLOCK) {
        fd_set writable;
        FD_ZERO(&writable);
        FD_SET(client, &writable);
        timeval timeout{2, 0};
        int ready = select(0, nullptr, &writable, nullptr, &timeout);
        if (ready == 1) {
            int so_error = 0, size = sizeof(so_error);
            error = getsockopt(client, SOL_SOCKET, SO_ERROR, reinterpret_cast<char*>(&so_error), &size) == 0
                ? static_cast<unsigned>(so_error) : static_cast<unsigned>(WSAGetLastError());
        } else error = ready == 0 ? WSAETIMEDOUT : WSAGetLastError();
    }
    closesocket(client);
    return error;
}

void probe_loopback_connectivity(const sockaddr_in& original) {
    wchar_t enabled[2]{};
    if (GetEnvironmentVariableW(L"PRICECHECK_PROXY_DIAG", enabled, 2) != 1 || enabled[0] != L'1' ||
        InterlockedCompareExchange(&connectivity_probe_ran, 1, 0) != 0) return;
    SOCKET listener = ::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    SOCKET client = ::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (listener == INVALID_SOCKET || client == INVALID_SOCKET) {
        TraceEvent("probe_socket_error", WSAGetLastError());
    } else {
        sockaddr_in local{};
        local.sin_family = AF_INET;
        local.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
        if (bind(listener, reinterpret_cast<const sockaddr*>(&local), sizeof(local)) != 0 ||
            listen(listener, 1) != 0) TraceEvent("probe_listener_error", WSAGetLastError());
        else {
            int size = sizeof(local);
            if (getsockname(listener, reinterpret_cast<sockaddr*>(&local), &size) == 0) {
                int result = real_connect(client, reinterpret_cast<const sockaddr*>(&local), sizeof(local));
                TraceEvent("probe_local_result", result == 0 ? 0 : WSAGetLastError());
                if (result == 0) {
                    SOCKET accepted = accept(listener, nullptr, nullptr);
                    if (accepted != INVALID_SOCKET) closesocket(accepted);
                }
            } else TraceEvent("probe_name_error", WSAGetLastError());
        }
    }
    if (client != INVALID_SOCKET) closesocket(client);
    if (listener != INVALID_SOCKET) closesocket(listener);
    TraceEvent("probe_proxy_result", probe_remote_connectivity(proxy_address));
    TraceEvent("probe_origin_result", probe_remote_connectivity(original));
}

template<typename ConnectCall>
int redirect(SOCKET socket, const sockaddr* address, int length, ConnectCall call) {
    if (!should_redirect(socket, address, length)) {
        TraceEvent("redirect_skipped", endpoint_detail(socket, address, length));
        return call(address, length);
    }
    TraceEvent("redirect_selected", endpoint_detail(socket, address, length));
    probe_loopback_connectivity(*reinterpret_cast<const sockaddr_in*>(address));
    SOCKET listener = ::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (listener == INVALID_SOCKET) { TraceEvent("listener_socket_failed", WSAGetLastError()); WSASetLastError(WSAENOBUFS); return SOCKET_ERROR; }
    sockaddr_in local{};
    local.sin_family = AF_INET;
    local.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    if (bind(listener, reinterpret_cast<const sockaddr*>(&local), sizeof(local)) != 0 || listen(listener, 1) != 0) {
        TraceEvent("listener_bind_failed", WSAGetLastError());
        closesocket(listener); WSASetLastError(WSAENOBUFS); return SOCKET_ERROR;
    }
    int size = sizeof(local);
    if (getsockname(listener, reinterpret_cast<sockaddr*>(&local), &size) != 0) {
        TraceEvent("listener_name_failed", WSAGetLastError());
        closesocket(listener); WSASetLastError(WSAENOBUFS); return SOCKET_ERROR;
    }
    auto relay = std::make_unique<Relay>();
    relay->listener = listener;
    relay->destination = *reinterpret_cast<const sockaddr_in*>(address);
    HANDLE thread = CreateThread(nullptr, 0, relay_thread, relay.get(), 0, nullptr);
    if (!thread) { TraceEvent("relay_thread_failed", GetLastError()); closesocket(listener); WSASetLastError(WSAENOBUFS); return SOCKET_ERROR; }
    relay.release();
    CloseHandle(thread);
    int result = call(reinterpret_cast<const sockaddr*>(&local), sizeof(local));
    int error = result == 0 ? 0 : WSAGetLastError();
    TraceEvent("local_connect_result", error);
    if (result != 0) WSASetLastError(error);
    return result;
}

int WSAAPI hooked_connect(SOCKET socket, const sockaddr* address, int length) {
    if (!bypass) TraceEvent("connect_target", endpoint_detail(socket, address, length));
    if (forbidden_direct(socket, address, length)) { TraceEvent("connect_blocked", endpoint_detail(socket, address, length)); WSASetLastError(WSAEACCES); return SOCKET_ERROR; }
    return redirect(socket, address, length, [=](const sockaddr* target, int size) { return real_connect(socket, target, size); });
}

int WSAAPI hooked_wsa_connect(SOCKET socket, const sockaddr* address, int length,
                             LPWSABUF caller, LPWSABUF callee, LPQOS send_qos, LPQOS receive_qos) {
    if (!bypass) TraceEvent("wsa_connect_target", endpoint_detail(socket, address, length));
    if (forbidden_direct(socket, address, length)) { TraceEvent("wsa_connect_blocked", endpoint_detail(socket, address, length)); WSASetLastError(WSAEACCES); return SOCKET_ERROR; }
    return redirect(socket, address, length, [=](const sockaddr* target, int size) {
        return real_wsa_connect(socket, target, size, caller, callee, send_qos, receive_qos);
    });
}

BOOL PASCAL hooked_connect_ex(SOCKET socket, const sockaddr* address, int length,
                              PVOID send_buffer, DWORD send_length, LPDWORD sent, LPOVERLAPPED overlapped) {
    if (!bypass) TraceEvent("connect_ex_target", endpoint_detail(socket, address, length));
    if (!real_connect_ex) { WSASetLastError(WSAEINVAL); return FALSE; }
    if (!should_redirect(socket, address, length)) {
        TraceEvent("connect_ex_skipped", endpoint_detail(socket, address, length));
        return real_connect_ex(socket, address, length, send_buffer, send_length, sent, overlapped);
    }
    TraceEvent("connect_ex_selected", endpoint_detail(socket, address, length));
    // ConnectEx is asynchronous. The local listener accepts immediately and
    // its worker completes the upstream HTTP CONNECT before forwarding bytes.
    SOCKET listener = ::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (listener == INVALID_SOCKET) return FALSE;
    sockaddr_in local{};
    local.sin_family = AF_INET;
    local.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    if (bind(listener, reinterpret_cast<const sockaddr*>(&local), sizeof(local)) != 0 || listen(listener, 1) != 0) {
        closesocket(listener); return FALSE;
    }
    int size = sizeof(local);
    if (getsockname(listener, reinterpret_cast<sockaddr*>(&local), &size) != 0) {
        closesocket(listener); return FALSE;
    }
    auto relay = std::make_unique<Relay>();
    relay->listener = listener;
    relay->destination = *reinterpret_cast<const sockaddr_in*>(address);
    HANDLE thread = CreateThread(nullptr, 0, relay_thread, relay.get(), 0, nullptr);
    if (!thread) { closesocket(listener); return FALSE; }
    relay.release();
    CloseHandle(thread);
    return real_connect_ex(socket, reinterpret_cast<const sockaddr*>(&local), sizeof(local),
                           send_buffer, send_length, sent, overlapped);
}

int WSAAPI hooked_wsa_ioctl(SOCKET socket, DWORD code, LPVOID input, DWORD input_size,
                            LPVOID output, DWORD output_size, LPDWORD bytes,
                            LPWSAOVERLAPPED overlapped, LPWSAOVERLAPPED_COMPLETION_ROUTINE completion) {
    int result = real_wsa_ioctl(socket, code, input, input_size, output, output_size, bytes, overlapped, completion);
    if (result == 0 && code == SIO_GET_EXTENSION_FUNCTION_POINTER && input && input_size >= sizeof(GUID) &&
        output && output_size >= sizeof(LPFN_CONNECTEX) &&
        memcmp(input, &connect_ex_id, sizeof(GUID)) == 0) {
        auto function = static_cast<LPFN_CONNECTEX*>(output);
        real_connect_ex = *function;
        *function = hooked_connect_ex;
    }
    return result;
}

int WSAAPI hooked_sendto(SOCKET socket, const char* data, int size, int flags, const sockaddr* target, int target_size) {
    if (forbidden_direct(socket, target, target_size)) { TraceEvent("sendto_blocked", endpoint_detail(socket, target, target_size)); WSASetLastError(WSAEACCES); return SOCKET_ERROR; }
    return real_sendto(socket, data, size, flags, target, target_size);
}

int WSAAPI hooked_wsa_sendto(SOCKET socket, LPWSABUF buffers, DWORD count, LPDWORD sent, DWORD flags,
                             const sockaddr* target, int target_size, LPWSAOVERLAPPED overlapped,
                             LPWSAOVERLAPPED_COMPLETION_ROUTINE completion) {
    if (forbidden_direct(socket, target, target_size)) { TraceEvent("wsa_sendto_blocked", endpoint_detail(socket, target, target_size)); WSASetLastError(WSAEACCES); return SOCKET_ERROR; }
    return real_wsa_sendto(socket, buffers, count, sent, flags, target, target_size, overlapped, completion);
}
} // namespace

bool InstallProxyHooks() {
    auto host = utf8(env(L"PRICECHECK_PROXY_HOST"));
    auto port_text = env(L"PRICECHECK_PROXY_PORT");
    if (host.empty() || port_text.empty()) return false;
    for (char value : host) if (value == '\r' || value == '\n') return false;
    int port = _wtoi(port_text.c_str());
    if (port < 1 || port > 65535) return false;
    addrinfo hints{};
    hints.ai_family = AF_INET;
    hints.ai_socktype = SOCK_STREAM;
    addrinfo* addresses = nullptr;
    if (getaddrinfo(host.c_str(), port == 0 ? nullptr : std::to_string(port).c_str(), &hints, &addresses) != 0) return false;
    proxy_address = *reinterpret_cast<sockaddr_in*>(addresses->ai_addr);
    freeaddrinfo(addresses);
    auto user = utf8(env(L"PRICECHECK_PROXY_USER"));
    auto pass = utf8(env(L"PRICECHECK_PROXY_PASS"));
    if (user.find_first_of("\r\n") != std::string::npos || pass.find_first_of("\r\n") != std::string::npos) return false;
    if (!user.empty()) authorization = base64(user + ":" + pass);
    if (MH_CreateHookApi(L"ws2_32.dll", "connect", hooked_connect, reinterpret_cast<void**>(&real_connect)) != MH_OK) return false;
    if (MH_CreateHookApi(L"ws2_32.dll", "WSAConnect", hooked_wsa_connect, reinterpret_cast<void**>(&real_wsa_connect)) != MH_OK) return false;
    if (MH_CreateHookApi(L"ws2_32.dll", "WSAIoctl", hooked_wsa_ioctl, reinterpret_cast<void**>(&real_wsa_ioctl)) != MH_OK) return false;
    if (MH_CreateHookApi(L"ws2_32.dll", "sendto", hooked_sendto, reinterpret_cast<void**>(&real_sendto)) != MH_OK) return false;
    if (MH_CreateHookApi(L"ws2_32.dll", "WSASendTo", hooked_wsa_sendto, reinterpret_cast<void**>(&real_wsa_sendto)) != MH_OK) return false;
    return true;
}
