/* Per-process identity ownership. No persistent system changes. */
#define LU4_IDENTITY_MAX_CLIENTS LU4_PROXY_MAX_ROUTES
static KSPIN_LOCK g_IdentityLock;
static KMUTEX g_IdentitySetup;
static LARGE_INTEGER g_IdentityCookie;
static BOOLEAN g_IdentityRegistered;
typedef struct _LU4_IDENTITY_CLIENT {
    PEPROCESS Process;
    PEPROCESS Owner;
    LU4_PROCESS_IDENTITY_REQUEST Config;
} LU4_IDENTITY_CLIENT;
static LU4_IDENTITY_CLIENT g_IdentityClients[LU4_IDENTITY_MAX_CLIENTS];
static PEPROCESS g_IdentityHosts[LU4_IDENTITY_MAX_CLIENTS];
static LU4_PROCESS_IDENTITY_REQUEST g_DefaultIdentity;
static BOOLEAN g_DefaultIdentityActive;

static VOID IdentityInitialize(VOID)
{
    KeInitializeSpinLock(&g_IdentityLock);
    KeInitializeMutex(&g_IdentitySetup, 0);
    RtlZeroMemory(g_IdentityClients, sizeof(g_IdentityClients));
    RtlZeroMemory(g_IdentityHosts, sizeof(g_IdentityHosts));
}

static BOOLEAN IdentityConfigForCurrent(PLU4_PROCESS_IDENTITY_REQUEST config)
{
    KIRQL irql;
    ULONG i;
    BOOLEAN found = FALSE;
    PEPROCESS current = PsGetCurrentProcess();
    KeAcquireSpinLock(&g_IdentityLock, &irql);
    for (i = 0; i < LU4_IDENTITY_MAX_CLIENTS; ++i)
        if (g_IdentityClients[i].Process == current) {
            *config = g_IdentityClients[i].Config; found = TRUE; break;
        }
    if (!found && g_DefaultIdentityActive) {
        BOOLEAN controller = FALSE;
        for (i = 0; i < LU4_IDENTITY_MAX_CLIENTS; ++i)
            if (g_IdentityHosts[i] == current) { controller = TRUE; break; }
        if (!controller) { *config = g_DefaultIdentity; found = TRUE; }
    }
    KeReleaseSpinLock(&g_IdentityLock, irql);
    return found;
}

static VOID IdentityProcessNotify(PEPROCESS process, HANDLE processId, PPS_CREATE_NOTIFY_INFO createInfo)
{
    KIRQL irql;
    ULONG i;
    LONG parent = -1, vacant = -1;
    ULONGLONG created = createInfo ? PsGetProcessCreateTimeQuadPart(process) : 0;
    KeAcquireSpinLock(&g_IdentityLock, &irql);
    for (i = 0; i < LU4_IDENTITY_MAX_CLIENTS; ++i) {
        LU4_IDENTITY_CLIENT* client = &g_IdentityClients[i];
        if (!createInfo) {
            if (client->Process == process || client->Owner == process) {
                if (client->Process) ObDereferenceObject(client->Process);
                if (client->Owner) ObDereferenceObject(client->Owner);
                RtlZeroMemory(client, sizeof(*client));
            }
        } else {
            if (client->Process && client->Config.ProcessId == HandleToULong(createInfo->ParentProcessId)) parent = (LONG)i;
            if (!client->Process && vacant < 0) vacant = (LONG)i;
        }
    }
    if (createInfo && NT_SUCCESS(createInfo->CreationStatus) && parent >= 0) {
        if (vacant < 0) createInfo->CreationStatus = STATUS_INSUFFICIENT_RESOURCES;
        else {
            g_IdentityClients[vacant] = g_IdentityClients[parent];
            g_IdentityClients[vacant].Process = process;
            g_IdentityClients[vacant].Config.ProcessId = HandleToULong(processId);
            g_IdentityClients[vacant].Config.CreatedTime = created;
            ObReferenceObject(process); ObReferenceObject(g_IdentityClients[vacant].Owner);
        }
    }
    if (!createInfo) {
        BOOLEAN anyHost = FALSE;
        for (i = 0; i < LU4_IDENTITY_MAX_CLIENTS; ++i) {
            if (g_IdentityHosts[i] == process) { ObDereferenceObject(process); g_IdentityHosts[i] = NULL; }
            if (g_IdentityHosts[i]) anyHost = TRUE;
        }
        if (!anyHost) { g_DefaultIdentityActive = FALSE; RtlZeroMemory(&g_DefaultIdentity, sizeof(g_DefaultIdentity)); }
    }
    KeReleaseSpinLock(&g_IdentityLock, irql);
}

#include "identity_registry_callback.inc"

static VOID IdentityShutdown(VOID)
{
    KIRQL irql;
    ULONG i;
    if (g_IdentityRegistered) {
        CmUnRegisterCallback(g_IdentityCookie);
        g_IdentityRegistered = FALSE;
    }
    KeAcquireSpinLock(&g_IdentityLock, &irql);
    for (i = 0; i < LU4_IDENTITY_MAX_CLIENTS; ++i) {
        if (g_IdentityClients[i].Process) ObDereferenceObject(g_IdentityClients[i].Process);
        if (g_IdentityClients[i].Owner) ObDereferenceObject(g_IdentityClients[i].Owner);
        RtlZeroMemory(&g_IdentityClients[i], sizeof(g_IdentityClients[i]));
        if (g_IdentityHosts[i]) ObDereferenceObject(g_IdentityHosts[i]);
        g_IdentityHosts[i] = NULL;
    }
    g_DefaultIdentityActive = FALSE;
    KeReleaseSpinLock(&g_IdentityLock, irql);
}

static BOOLEAN IdentityGuidValid(PCWSTR text, BOOLEAN plain)
{
    ULONG i;
    GUID guid;
    UNICODE_STRING value;
    for (i = 0; i < 40; ++i) if (!text[i]) break;
    if (i == 40 || (plain ? i != 36 : i != 36 && i != 38)) return FALSE;
    /* RtlGUIDFromString requires braces. */
    if (i == 36) {
        WCHAR braces[39];
        braces[0] = L'{'; RtlCopyMemory(braces + 1, text, 36 * sizeof(WCHAR));
        braces[37] = L'}'; braces[38] = 0;
        RtlInitUnicodeString(&value, braces);
        return NT_SUCCESS(RtlGUIDFromString(&value, &guid));
    }
    RtlInitUnicodeString(&value, text);
    return NT_SUCCESS(RtlGUIDFromString(&value, &guid));
}

static BOOLEAN IdentityAsciiText(PCWSTR text, ULONG count)
{
    ULONG i;
    if (!text[0]) return FALSE;
    for (i = 0; i < count; ++i) {
        if (!text[i]) return TRUE;
        if (text[i] < 32 || text[i] > 126) return FALSE;
    }
    return FALSE;
}

static NTSTATUS HandleProcessIdentity(PDEVICE_OBJECT device, PVOID buffer, ULONG inputLength,
    ULONG outputLength, PULONG_PTR information)
{
    PLU4_PROCESS_IDENTITY_REQUEST request = buffer;
    LU4_PROXY_ROUTE route;
    PEPROCESS process = NULL, owner = PsGetCurrentProcess();
    KIRQL irql;
    LONG slot = -1, hostSlot = -1;
    ULONG i;
    NTSTATUS status;
    UNICODE_STRING altitude = RTL_CONSTANT_STRING(L"385421.918"); /* Development altitude; production allocation required. */
    if (inputLength != sizeof(*request) || outputLength < sizeof(*request) ||
        request->Version != LU4_PROTOCOL_VERSION || request->ProcessId == 0 || request->CreatedTime == 0 ||
        request->Capabilities || request->Reserved || request->Reserved2 || !request->InstallDate ||
        !IdentityAsciiText(request->ProductId, 64) || !IdentityAsciiText(request->ComputerName, 64) ||
        !IdentityAsciiText(request->ProcessorModel, 128) ||
        !IdentityGuidValid(request->SusClientId, FALSE) || !IdentityGuidValid(request->SqmMachineId, FALSE) ||
        !IdentityGuidValid(request->VideoIdentifier, FALSE) ||
        !IdentityGuidValid(request->MachineGuid, FALSE) || !IdentityGuidValid(request->HardwareProfileGuid, FALSE) ||
        !IdentityGuidValid(request->SystemUuid, TRUE)) return STATUS_INVALID_PARAMETER;
    route = ProxyRoute(request->ProcessId);
    if (route.HostProcessId != HandleToULong(PsGetCurrentProcessId()) || !route.ListenerPort) return STATUS_ACCESS_DENIED;
    status = PsLookupProcessByProcessId(ULongToHandle(request->ProcessId), &process);
    if (!NT_SUCCESS(status)) return status;
    if ((ULONGLONG)PsGetProcessCreateTimeQuadPart(process) != request->CreatedTime ||
        PsGetProcessExitStatus(process) != STATUS_PENDING) { ObDereferenceObject(process); return STATUS_INVALID_CID; }
    KeWaitForSingleObject(&g_IdentitySetup, Executive, KernelMode, FALSE, NULL);
    if (!g_IdentityRegistered) {
        status = CmRegisterCallbackEx(IdentityRegistryCallback, &altitude, device->DriverObject, NULL, &g_IdentityCookie, NULL);
        if (!NT_SUCCESS(status)) goto done;
        g_IdentityRegistered = TRUE;
    }
    KeAcquireSpinLock(&g_IdentityLock, &irql);
    for (i = 0; i < LU4_IDENTITY_MAX_CLIENTS; ++i) {
        if (g_IdentityClients[i].Process == process) {
            LU4_PROCESS_IDENTITY_REQUEST current = g_IdentityClients[i].Config;
            current.Capabilities = 0;
            status = g_IdentityClients[i].Owner == owner &&
                RtlCompareMemory(&current, request, sizeof(current)) == sizeof(current) ? STATUS_SUCCESS : STATUS_OBJECT_NAME_COLLISION;
            goto unlock;
        }
        if (!g_IdentityClients[i].Process && slot < 0) slot = (LONG)i;
    }
    if (slot < 0) { status = STATUS_INSUFFICIENT_RESOURCES; goto unlock; }
    for (i = 0; i < LU4_IDENTITY_MAX_CLIENTS; ++i) {
        if (g_IdentityHosts[i] == owner) { hostSlot = (LONG)i; break; }
        if (!g_IdentityHosts[i] && hostSlot < 0) hostSlot = (LONG)i;
    }
    if (hostSlot < 0) { status = STATUS_INSUFFICIENT_RESOURCES; goto unlock; }
    if (!g_IdentityHosts[hostSlot]) { ObReferenceObject(owner); g_IdentityHosts[hostSlot] = owner; }
    g_IdentityClients[slot].Process = process; ObReferenceObject(process);
    g_IdentityClients[slot].Owner = owner; ObReferenceObject(owner);
    g_IdentityClients[slot].Config = *request;
    g_IdentityClients[slot].Config.Capabilities = LU4_PROCESS_IDENTITY_CAPABILITIES;
    if (!g_DefaultIdentityActive) { g_DefaultIdentity = *request; g_DefaultIdentityActive = TRUE; }
    status = STATUS_SUCCESS;
unlock:
    KeReleaseSpinLock(&g_IdentityLock, irql);
    if (NT_SUCCESS(status)) { request->Capabilities = LU4_PROCESS_IDENTITY_CAPABILITIES; *information = sizeof(*request); }
done:
    KeReleaseMutex(&g_IdentitySetup, FALSE);
    ObDereferenceObject(process);
    return status;
}
