static NTSTATUS HandleProxyGuard(PDEVICE_OBJECT device, PVOID buffer,
    ULONG inputLength, ULONG outputLength, PULONG_PTR information)
{
    PLU4_PROXY_GUARD_REQUEST request = buffer;
    LU4_PROXY_ROUTE route = {0};
    NTSTATUS status;
    if (inputLength != sizeof(*request) || outputLength < sizeof(*request)) return STATUS_BUFFER_TOO_SMALL;
    if (request->Version != LU4_PROTOCOL_VERSION) return STATUS_INVALID_PARAMETER;
    if (!request->ProcessId) {
        status = ProxyRedirectRegister(device);
        if (!NT_SUCCESS(status)) return status;
        request->Active = 0;
    } else {
        route = ProxyRoute(request->ProcessId);
        if (route.ProcessId && route.HostProcessId != HandleToULong(PsGetCurrentProcessId()))
            return STATUS_ACCESS_DENIED;
        request->HostProcessId = route.HostProcessId;
        request->ListenerPort = route.ListenerPort;
        request->Active = route.ProcessId && route.ListenerPort && g_ProxyEngine &&
            g_ProxyAuthIds[0] && g_ProxyAuthIds[1] && g_ProxyAuthIds[2] && g_ProxyAuthIds[3] ? 1 : 0;
        request->BlockedConnections = route.Blocked;
    }
    request->Capabilities = LU4_PROXY_GUARD_CAPABILITIES;
    *information = sizeof(*request);
    if (outputLength >= sizeof(LU4_PROXY_GUARD_DETAILS)) {
        PLU4_PROXY_GUARD_DETAILS details = buffer;
        details->BlockedProtocol = route.BlockedProtocol;
        details->BlockedLayer = route.BlockedLayer;
        details->BlockedAddress = route.BlockedAddress;
        details->BlockedPort = route.BlockedPort;
        *information = sizeof(*details);
    }
    return STATUS_SUCCESS;
}

static NTSTATUS HandleProcessStatus(PVOID buffer, ULONG inputLength,
    ULONG outputLength, PULONG_PTR information)
{
    PLU4_PROCESS_STATUS_REQUEST request = buffer;
    PEPROCESS process = NULL;
    NTSTATUS status;
    if (inputLength != sizeof(*request) || outputLength < sizeof(*request)) return STATUS_BUFFER_TOO_SMALL;
    if (request->Version != LU4_PROTOCOL_VERSION || !request->ProcessId) return STATUS_INVALID_PARAMETER;
    status = LookupTargetProcess(request->ProcessId, &process);
    request->CreationTime = 0; request->Active = 0; request->ExitStatus = status;
    if (NT_SUCCESS(status)) {
        request->CreationTime = (ULONGLONG)PsGetProcessCreateTimeQuadPart(process);
        request->ExitStatus = PsGetProcessExitStatus(process);
        request->Active = request->ExitStatus == STATUS_PENDING ? 1 : 0;
        ObDereferenceObject(process);
    }
    *information = sizeof(*request);
    return STATUS_SUCCESS;
}
