/* Per-PID IPv4 TCP connect redirect. Registered only after an explicit IOCTL. */

#define LU4_PROXY_MAX_ROUTES 16
#define LU4_PROXY_POOL_TAG '4PcL'

DEFINE_GUID(g_ProxyCalloutKey, 0x87846dc1, 0x4e8a, 0x40b9, 0x91, 0x9b, 0x8e, 0xaa, 0x84, 0x36, 0xe1, 0x11);
DEFINE_GUID(g_ProxySublayerKey, 0x87846dc2, 0x4e8a, 0x40b9, 0x91, 0x9b, 0x8e, 0xaa, 0x84, 0x36, 0xe1, 0x11);

typedef struct _LU4_PROXY_ROUTE {
    ULONG ProcessId;
    ULONG HostProcessId;
    USHORT ListenerPort;
} LU4_PROXY_ROUTE;

static KSPIN_LOCK g_ProxyRouteLock;
static KMUTEX g_ProxySetupMutex;
static LU4_PROXY_ROUTE g_ProxyRoutes[LU4_PROXY_MAX_ROUTES];
static HANDLE g_ProxyEngine = NULL;
static HANDLE g_ProxyRedirectHandle = NULL;
static UINT32 g_ProxyCalloutId = 0;
static BOOLEAN g_ProxyProcessNotifyRegistered = FALSE;
static ULONG g_ProxyRegisterStage = 0;

static VOID ProxyRedirectInitialize(VOID)
{
    KeInitializeSpinLock(&g_ProxyRouteLock);
    KeInitializeMutex(&g_ProxySetupMutex, 0);
    RtlZeroMemory(g_ProxyRoutes, sizeof(g_ProxyRoutes));
}

static VOID ProxyProcessNotify(_Inout_ PEPROCESS Process, _In_ HANDLE ProcessId,
    _Inout_opt_ PPS_CREATE_NOTIFY_INFO CreateInfo)
{
    KIRQL irql;
    ULONG index;
    ULONG pid = HandleToULong(ProcessId);
    UNREFERENCED_PARAMETER(Process);
    if (CreateInfo != NULL) return;
    KeAcquireSpinLock(&g_ProxyRouteLock, &irql);
    for (index = 0; index < LU4_PROXY_MAX_ROUTES; ++index) {
        if (g_ProxyRoutes[index].ProcessId == pid || g_ProxyRoutes[index].HostProcessId == pid)
            RtlZeroMemory(&g_ProxyRoutes[index], sizeof(g_ProxyRoutes[index]));
    }
    KeReleaseSpinLock(&g_ProxyRouteLock, irql);
}

static VOID NTAPI ProxyClassify(
    _In_ const FWPS_INCOMING_VALUES0* fixedValues,
    _In_ const FWPS_INCOMING_METADATA_VALUES0* metadata,
    _Inout_opt_ VOID* layerData,
    _In_opt_ const VOID* classifyContext,
    _In_ const FWPS_FILTER1* filter,
    _In_ UINT64 flowContext,
    _Inout_ FWPS_CLASSIFY_OUT0* classifyOut)
{
    KIRQL irql;
    ULONG index;
    ULONG pid;
    LU4_PROXY_ROUTE route = {0};
    UINT64 classifyHandle = 0;
    FWPS_CONNECT_REQUEST0* request = NULL;
    SOCKADDR_IN* original;
    PLU4_PROXY_REDIRECT_CONTEXT context = NULL;
    NTSTATUS status;
    FWPS_CONNECTION_REDIRECT_STATE redirectState;

    UNREFERENCED_PARAMETER(layerData);
    UNREFERENCED_PARAMETER(classifyContext);
    UNREFERENCED_PARAMETER(flowContext);
    classifyOut->actionType = FWP_ACTION_CONTINUE;
    if ((classifyOut->rights & FWPS_RIGHT_ACTION_WRITE) == 0 ||
        (metadata->currentMetadataValues & FWPS_METADATA_FIELD_PROCESS_ID) == 0 ||
        fixedValues->incomingValue[FWPS_FIELD_ALE_CONNECT_REDIRECT_V4_IP_PROTOCOL].value.uint8 != IPPROTO_TCP)
        return;

    pid = (ULONG)metadata->processId;
    KeAcquireSpinLock(&g_ProxyRouteLock, &irql);
    for (index = 0; index < LU4_PROXY_MAX_ROUTES; ++index) {
        if (g_ProxyRoutes[index].ProcessId == pid) {
            route = g_ProxyRoutes[index];
            break;
        }
    }
    KeReleaseSpinLock(&g_ProxyRouteLock, irql);
    if (route.ProcessId == 0 || g_ProxyRedirectHandle == NULL) return;

    redirectState = FwpsQueryConnectionRedirectState0(metadata->redirectRecords,
        g_ProxyRedirectHandle, NULL);
    if (redirectState != FWPS_CONNECTION_NOT_REDIRECTED) return;

    status = FwpsAcquireClassifyHandle0((PVOID)classifyContext, 0, &classifyHandle);
    if (!NT_SUCCESS(status)) return;
    status = FwpsAcquireWritableLayerDataPointer0(classifyHandle, filter->filterId, 0,
        (PVOID*)&request, classifyOut);
    if (!NT_SUCCESS(status)) goto Done;

    original = (SOCKADDR_IN*)&request->remoteAddressAndPort;
    if (original->sin_family != AF_INET ||
        (RtlUlongByteSwap(original->sin_addr.S_un.S_addr) >> 24) == 127) {
        FwpsApplyModifiedLayerData0(classifyHandle, request, 0);
        goto Done;
    }
    context = (PLU4_PROXY_REDIRECT_CONTEXT)ExAllocatePool2(
        POOL_FLAG_NON_PAGED, sizeof(*context), LU4_PROXY_POOL_TAG);
    if (context == NULL) {
        FwpsApplyModifiedLayerData0(classifyHandle, request, 0);
        goto Done;
    }
    context->Magic = LU4_PROXY_REDIRECT_MAGIC;
    context->ProcessId = pid;
    context->AddressNetworkOrder = original->sin_addr.S_un.S_addr;
    context->PortNetworkOrder = original->sin_port;
    context->Reserved = 0;

    original->sin_addr.S_un.S_addr = RtlUlongByteSwap(0x7F000001);
    original->sin_port = RtlUshortByteSwap(route.ListenerPort);
    request->localRedirectTargetPID = route.HostProcessId;
    request->localRedirectHandle = g_ProxyRedirectHandle;
    request->modifierFilterId = filter->filterId;
    request->localRedirectContext = context;
    request->localRedirectContextSize = sizeof(*context);
    FwpsApplyModifiedLayerData0(classifyHandle, request, 0);
    classifyOut->actionType = FWP_ACTION_PERMIT;
    classifyOut->rights &= ~FWPS_RIGHT_ACTION_WRITE;
Done:
    FwpsReleaseClassifyHandle0(classifyHandle);
}

static NTSTATUS NTAPI ProxyNotify(
    _In_ FWPS_CALLOUT_NOTIFY_TYPE notifyType,
    _In_ const GUID* filterKey,
    _Inout_ FWPS_FILTER1* filter)
{
    UNREFERENCED_PARAMETER(notifyType);
    UNREFERENCED_PARAMETER(filterKey);
    UNREFERENCED_PARAMETER(filter);
    return STATUS_SUCCESS;
}

static VOID ProxyRedirectShutdown(VOID)
{
    KIRQL irql;
    KeWaitForSingleObject(&g_ProxySetupMutex, Executive, KernelMode, FALSE, NULL);
    KeAcquireSpinLock(&g_ProxyRouteLock, &irql);
    RtlZeroMemory(g_ProxyRoutes, sizeof(g_ProxyRoutes));
    KeReleaseSpinLock(&g_ProxyRouteLock, irql);
    if (g_ProxyEngine != NULL) {
        FwpmEngineClose0(g_ProxyEngine);
        g_ProxyEngine = NULL;
    }
    if (g_ProxyCalloutId != 0) {
        FwpsCalloutUnregisterById0(g_ProxyCalloutId);
        g_ProxyCalloutId = 0;
    }
    if (g_ProxyRedirectHandle != NULL) {
        FwpsRedirectHandleDestroy0(g_ProxyRedirectHandle);
        g_ProxyRedirectHandle = NULL;
    }
    if (g_ProxyProcessNotifyRegistered) {
        PsSetCreateProcessNotifyRoutineEx(ProxyProcessNotify, TRUE);
        g_ProxyProcessNotifyRegistered = FALSE;
    }
    KeReleaseMutex(&g_ProxySetupMutex, FALSE);
}

static NTSTATUS ProxyRedirectRegister(_In_ PDEVICE_OBJECT deviceObject)
{
    NTSTATUS status;
    FWPS_CALLOUT1 kernelCallout = {0};
    FWPM_CALLOUT0 callout = {0};
    FWPM_SUBLAYER0 sublayer = {0};
    FWPM_FILTER0 filter = {0};
    FWPM_SESSION0 session = {0};

    KeWaitForSingleObject(&g_ProxySetupMutex, Executive, KernelMode, FALSE, NULL);
    if (g_ProxyEngine != NULL) {
        KeReleaseMutex(&g_ProxySetupMutex, FALSE);
        return STATUS_SUCCESS;
    }
    g_ProxyRegisterStage = 1;
    status = PsSetCreateProcessNotifyRoutineEx(ProxyProcessNotify, FALSE);
    if (!NT_SUCCESS(status)) goto Done;
    g_ProxyProcessNotifyRegistered = TRUE;

    session.flags = FWPM_SESSION_FLAG_DYNAMIC;
    session.displayData.name = L"PriceCheck proxy redirect session";
    g_ProxyRegisterStage = 2;
    status = FwpmEngineOpen0(NULL, RPC_C_AUTHN_WINNT, NULL, &session, &g_ProxyEngine);
    if (!NT_SUCCESS(status)) goto Done;

    sublayer.subLayerKey = g_ProxySublayerKey;
    sublayer.displayData.name = L"PriceCheck proxy redirect";
    sublayer.weight = 65500;
    g_ProxyRegisterStage = 3;
    status = FwpmSubLayerAdd0(g_ProxyEngine, &sublayer, NULL);
    if (!NT_SUCCESS(status)) goto Done;

    kernelCallout.calloutKey = g_ProxyCalloutKey;
    kernelCallout.classifyFn = ProxyClassify;
    kernelCallout.notifyFn = ProxyNotify;
    g_ProxyRegisterStage = 4;
    status = FwpsCalloutRegister1(deviceObject, &kernelCallout, &g_ProxyCalloutId);
    if (!NT_SUCCESS(status)) goto Done;
    g_ProxyRegisterStage = 5;
    status = FwpsRedirectHandleCreate0(&g_ProxyCalloutKey, 0, &g_ProxyRedirectHandle);
    if (!NT_SUCCESS(status)) goto Done;

    callout.calloutKey = g_ProxyCalloutKey;
    callout.displayData.name = L"PriceCheck proxy connect callout";
    callout.applicableLayer = FWPM_LAYER_ALE_CONNECT_REDIRECT_V4;
    g_ProxyRegisterStage = 6;
    status = FwpmCalloutAdd0(g_ProxyEngine, &callout, NULL, NULL);
    if (!NT_SUCCESS(status)) goto Done;

    filter.displayData.name = L"PriceCheck IPv4 TCP proxy filter";
    filter.layerKey = FWPM_LAYER_ALE_CONNECT_REDIRECT_V4;
    filter.subLayerKey = g_ProxySublayerKey;
    filter.action.type = FWP_ACTION_CALLOUT_UNKNOWN;
    filter.action.calloutKey = g_ProxyCalloutKey;
    filter.weight.type = FWP_EMPTY;
    g_ProxyRegisterStage = 7;
    status = FwpmFilterAdd0(g_ProxyEngine, &filter, NULL, NULL);
Done:
    if (!NT_SUCCESS(status)) {
        if (g_ProxyEngine != NULL) {
            FwpmEngineClose0(g_ProxyEngine);
            g_ProxyEngine = NULL;
        }
        if (g_ProxyCalloutId != 0) {
            FwpsCalloutUnregisterById0(g_ProxyCalloutId);
            g_ProxyCalloutId = 0;
        }
        if (g_ProxyRedirectHandle != NULL) {
            FwpsRedirectHandleDestroy0(g_ProxyRedirectHandle);
            g_ProxyRedirectHandle = NULL;
        }
        if (g_ProxyProcessNotifyRegistered) {
            PsSetCreateProcessNotifyRoutineEx(ProxyProcessNotify, TRUE);
            g_ProxyProcessNotifyRegistered = FALSE;
        }
    }
    KeReleaseMutex(&g_ProxySetupMutex, FALSE);
    return status;
}

static NTSTATUS HandleProxyRedirect(_In_ PDEVICE_OBJECT deviceObject, _Inout_ PVOID buffer,
    _In_ ULONG inputLength, _In_ ULONG outputLength, _Out_ PULONG_PTR information)
{
    PLU4_PROXY_REDIRECT_REQUEST request;
    KIRQL irql;
    ULONG index;
    LONG vacant = -1;
    ULONG owner = HandleToULong(PsGetCurrentProcessId());
    PEPROCESS target;
    NTSTATUS status;

    if (inputLength != sizeof(LU4_PROXY_REDIRECT_REQUEST) ||
        outputLength < sizeof(LU4_PROXY_REDIRECT_REQUEST)) return STATUS_BUFFER_TOO_SMALL;
    request = (PLU4_PROXY_REDIRECT_REQUEST)buffer;
    if (request->Version != LU4_PROTOCOL_VERSION || request->ProcessId == 0 ||
        request->Enable > 1 || request->ListenerPort > 65535 ||
        (request->Enable && request->ListenerPort == 0)) return STATUS_INVALID_PARAMETER;
    if (request->Enable) {
        status = PsLookupProcessByProcessId(ULongToHandle(request->ProcessId), &target);
        if (!NT_SUCCESS(status)) return status;
        ObDereferenceObject(target);
        status = ProxyRedirectRegister(deviceObject);
        if (!NT_SUCCESS(status)) {
            request->Status = status;
            request->Stage = g_ProxyRegisterStage;
            *information = sizeof(*request);
            return STATUS_SUCCESS;
        }
    }
    KeAcquireSpinLock(&g_ProxyRouteLock, &irql);
    for (index = 0; index < LU4_PROXY_MAX_ROUTES; ++index) {
        if (g_ProxyRoutes[index].ProcessId == request->ProcessId) {
            if (g_ProxyRoutes[index].HostProcessId != owner) {
                KeReleaseSpinLock(&g_ProxyRouteLock, irql);
                return STATUS_ACCESS_DENIED;
            }
            break;
        }
        if (vacant < 0 && g_ProxyRoutes[index].ProcessId == 0) vacant = (LONG)index;
    }
    if (index == LU4_PROXY_MAX_ROUTES && request->Enable) {
        if (vacant < 0) {
            KeReleaseSpinLock(&g_ProxyRouteLock, irql);
            return STATUS_INSUFFICIENT_RESOURCES;
        }
        index = (ULONG)vacant;
    }
    if (index < LU4_PROXY_MAX_ROUTES) {
        if (request->Enable) {
            g_ProxyRoutes[index].ProcessId = request->ProcessId;
            g_ProxyRoutes[index].HostProcessId = owner;
            g_ProxyRoutes[index].ListenerPort = (USHORT)request->ListenerPort;
        } else RtlZeroMemory(&g_ProxyRoutes[index], sizeof(g_ProxyRoutes[index]));
    }
    KeReleaseSpinLock(&g_ProxyRouteLock, irql);
    request->HostProcessId = owner;
    request->Status = STATUS_SUCCESS;
    request->Stage = 0;
    *information = sizeof(*request);
    return STATUS_SUCCESS;
}
