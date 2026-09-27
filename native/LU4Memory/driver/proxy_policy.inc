/* Every protected process is denied egress except its exact TCP relay.
 * Child protection is inherited in the create callback before its first thread runs. */
DEFINE_GUID(g_ProxyAuthV4Key, 0x87846dc3, 0x4e8a, 0x40b9, 0x91, 0x9b, 0x8e, 0xaa, 0x84, 0x36, 0xe1, 0x11);
DEFINE_GUID(g_ProxyAuthV6Key, 0x87846dc4, 0x4e8a, 0x40b9, 0x91, 0x9b, 0x8e, 0xaa, 0x84, 0x36, 0xe1, 0x11);
DEFINE_GUID(g_ProxyBindV4Key, 0x87846dc5, 0x4e8a, 0x40b9, 0x91, 0x9b, 0x8e, 0xaa, 0x84, 0x36, 0xe1, 0x11);
DEFINE_GUID(g_ProxyBindV6Key, 0x87846dc6, 0x4e8a, 0x40b9, 0x91, 0x9b, 0x8e, 0xaa, 0x84, 0x36, 0xe1, 0x11);
static UINT32 g_ProxyAuthIds[4];

static LU4_PROXY_ROUTE ProxyRoute(ULONG pid)
{
    KIRQL irql;
    ULONG index;
    LU4_PROXY_ROUTE result = {0};
    KeAcquireSpinLock(&g_ProxyRouteLock, &irql);
    for (index = 0; index < LU4_PROXY_MAX_ROUTES; ++index)
        if (g_ProxyRoutes[index].ProcessId == pid) { result = g_ProxyRoutes[index]; break; }
    KeReleaseSpinLock(&g_ProxyRouteLock, irql);
    return result;
}

static VOID ProxyBlocked(ULONG pid, const FWPS_INCOMING_VALUES0* values)
{
    KIRQL irql;
    ULONG index;
    LU4_PROXY_ROUTE route = ProxyRoute(pid);
    KeAcquireSpinLock(&g_ProxyRouteLock, &irql);
    for (index = 0; index < LU4_PROXY_MAX_ROUTES; ++index)
        if (route.RootProcessId && g_ProxyRoutes[index].RootProcessId == route.RootProcessId &&
            g_ProxyRoutes[index].HostProcessId == route.HostProcessId &&
            g_ProxyRoutes[index].ListenerPort == route.ListenerPort) {
            LU4_PROXY_ROUTE* target = &g_ProxyRoutes[index];
            ++target->Blocked;
            target->BlockedLayer = values->layerId;
            target->BlockedAddress = 0; target->BlockedPort = 0;
            if (values->layerId == FWPS_LAYER_ALE_AUTH_CONNECT_V4) {
                target->BlockedProtocol = values->incomingValue[FWPS_FIELD_ALE_AUTH_CONNECT_V4_IP_PROTOCOL].value.uint8;
                target->BlockedAddress = values->incomingValue[FWPS_FIELD_ALE_AUTH_CONNECT_V4_IP_REMOTE_ADDRESS].value.uint32;
                target->BlockedPort = values->incomingValue[FWPS_FIELD_ALE_AUTH_CONNECT_V4_IP_REMOTE_PORT].value.uint16;
            } else if (values->layerId == FWPS_LAYER_ALE_AUTH_CONNECT_V6) {
                target->BlockedProtocol = values->incomingValue[FWPS_FIELD_ALE_AUTH_CONNECT_V6_IP_PROTOCOL].value.uint8;
                target->BlockedPort = values->incomingValue[FWPS_FIELD_ALE_AUTH_CONNECT_V6_IP_REMOTE_PORT].value.uint16;
            } else if (values->layerId == FWPS_LAYER_ALE_RESOURCE_ASSIGNMENT_V4)
                target->BlockedProtocol = values->incomingValue[FWPS_FIELD_ALE_RESOURCE_ASSIGNMENT_V4_IP_PROTOCOL].value.uint8;
            else if (values->layerId == FWPS_LAYER_ALE_RESOURCE_ASSIGNMENT_V6)
                target->BlockedProtocol = values->incomingValue[FWPS_FIELD_ALE_RESOURCE_ASSIGNMENT_V6_IP_PROTOCOL].value.uint8;
        }
    KeReleaseSpinLock(&g_ProxyRouteLock, irql);
}

static VOID ProxyProcessNotify(_Inout_ PEPROCESS process, _In_ HANDLE processId,
    _Inout_opt_ PPS_CREATE_NOTIFY_INFO createInfo)
{
    KIRQL irql;
    ULONG index, pid = HandleToULong(processId);
    LU4_PROXY_ROUTE parent = {0};
    LONG vacant = -1;
    UNREFERENCED_PARAMETER(process);
    KeAcquireSpinLock(&g_ProxyRouteLock, &irql);
    for (index = 0; index < LU4_PROXY_MAX_ROUTES; ++index) {
        if (createInfo != NULL) {
            if (g_ProxyRoutes[index].ProcessId == HandleToULong(createInfo->ParentProcessId))
                parent = g_ProxyRoutes[index];
            if (vacant < 0 && !g_ProxyRoutes[index].ProcessId) vacant = (LONG)index;
        } else {
            if (g_ProxyRoutes[index].ProcessId == pid)
                RtlZeroMemory(&g_ProxyRoutes[index], sizeof(g_ProxyRoutes[index]));
            /* Keep the route, with no permitted relay, when its controller dies. */
            else if (g_ProxyRoutes[index].HostProcessId == pid)
                g_ProxyRoutes[index].ListenerPort = 0;
        }
    }
    if (createInfo != NULL && parent.ProcessId) {
        if (vacant < 0) createInfo->CreationStatus = STATUS_INSUFFICIENT_RESOURCES;
        else {
            parent.ProcessId = pid;
            g_ProxyRoutes[vacant] = parent;
        }
    }
    KeReleaseSpinLock(&g_ProxyRouteLock, irql);
}

static VOID NTAPI ProxyAuthorize(const FWPS_INCOMING_VALUES0* values,
    const FWPS_INCOMING_METADATA_VALUES0* metadata, VOID* layerData,
    const VOID* context, const FWPS_FILTER1* filter, UINT64 flow,
    FWPS_CLASSIFY_OUT0* output)
{
    LU4_PROXY_ROUTE route;
    BOOLEAN permitted = FALSE;
    UNREFERENCED_PARAMETER(layerData); UNREFERENCED_PARAMETER(context);
    UNREFERENCED_PARAMETER(filter); UNREFERENCED_PARAMETER(flow);
    /* Resource-assignment is an authorization layer: CONTINUE is not an
     * authorization result. Keep action-write rights for the other filters. */
    output->actionType = FWP_ACTION_PERMIT;
    if (!(metadata->currentMetadataValues & FWPS_METADATA_FIELD_PROCESS_ID)) return;
    route = ProxyRoute((ULONG)metadata->processId);
    if (!route.ProcessId) return;
    if (values->layerId == FWPS_LAYER_ALE_AUTH_CONNECT_V4 && route.ListenerPort) {
        permitted =
            values->incomingValue[FWPS_FIELD_ALE_AUTH_CONNECT_V4_IP_PROTOCOL].value.uint8 == IPPROTO_TCP &&
            values->incomingValue[FWPS_FIELD_ALE_AUTH_CONNECT_V4_IP_REMOTE_ADDRESS].value.uint32 == 0x7F000001 &&
            values->incomingValue[FWPS_FIELD_ALE_AUTH_CONNECT_V4_IP_REMOTE_PORT].value.uint16 == route.ListenerPort;
    }
    /* Creating a normal IPv4 socket does not grant egress. AUTH_CONNECT above
     * rejects every UDP destination, including the first sendto datagram. */
    if (values->layerId == FWPS_LAYER_ALE_RESOURCE_ASSIGNMENT_V4)
        permitted = (values->incomingValue[FWPS_FIELD_ALE_RESOURCE_ASSIGNMENT_V4_IP_PROTOCOL].value.uint8 == IPPROTO_TCP ||
            values->incomingValue[FWPS_FIELD_ALE_RESOURCE_ASSIGNMENT_V4_IP_PROTOCOL].value.uint8 == IPPROTO_UDP) &&
            !(values->incomingValue[FWPS_FIELD_ALE_RESOURCE_ASSIGNMENT_V4_FLAGS].value.uint32 & FWP_CONDITION_FLAG_IS_RAW_ENDPOINT);
    if (values->layerId == FWPS_LAYER_ALE_RESOURCE_ASSIGNMENT_V6)
        permitted = (values->incomingValue[FWPS_FIELD_ALE_RESOURCE_ASSIGNMENT_V6_IP_PROTOCOL].value.uint8 == IPPROTO_TCP ||
            values->incomingValue[FWPS_FIELD_ALE_RESOURCE_ASSIGNMENT_V6_IP_PROTOCOL].value.uint8 == IPPROTO_UDP) &&
            !(values->incomingValue[FWPS_FIELD_ALE_RESOURCE_ASSIGNMENT_V6_FLAGS].value.uint32 & FWP_CONDITION_FLAG_IS_RAW_ENDPOINT);
    if (!permitted && (output->rights & FWPS_RIGHT_ACTION_WRITE)) {
        ProxyBlocked(route.ProcessId, values);
        output->actionType = FWP_ACTION_BLOCK;
        output->rights &= ~FWPS_RIGHT_ACTION_WRITE;
    }
}

static VOID ProxyAuthShutdown(VOID)
{
    ULONG index;
    for (index = 0; index < RTL_NUMBER_OF(g_ProxyAuthIds); ++index)
        if (g_ProxyAuthIds[index]) {
            FwpsCalloutUnregisterById0(g_ProxyAuthIds[index]);
            g_ProxyAuthIds[index] = 0;
        }
}

static NTSTATUS ProxyAuthRegister(PDEVICE_OBJECT device)
{
    ULONG index;
    const GUID* keys[] = {&g_ProxyAuthV4Key, &g_ProxyAuthV6Key, &g_ProxyBindV4Key, &g_ProxyBindV6Key};
    const GUID* layers[] = {&FWPM_LAYER_ALE_AUTH_CONNECT_V4, &FWPM_LAYER_ALE_AUTH_CONNECT_V6,
        &FWPM_LAYER_ALE_RESOURCE_ASSIGNMENT_V4, &FWPM_LAYER_ALE_RESOURCE_ASSIGNMENT_V6};
    NTSTATUS status;
    for (index = 0; index < RTL_NUMBER_OF(g_ProxyAuthIds); ++index) {
        FWPS_CALLOUT1 kernel = {0};
        FWPM_CALLOUT0 callout = {0};
        FWPM_FILTER0 filter = {0};
        kernel.calloutKey = *keys[index];
        kernel.classifyFn = ProxyAuthorize;
        kernel.notifyFn = ProxyNotify;
        status = FwpsCalloutRegister1(device, &kernel, &g_ProxyAuthIds[index]);
        if (!NT_SUCCESS(status)) return status;
        callout.calloutKey = *keys[index]; callout.applicableLayer = *layers[index];
        callout.displayData.name = L"PriceCheck deny direct egress";
        status = FwpmCalloutAdd0(g_ProxyEngine, &callout, NULL, NULL);
        if (!NT_SUCCESS(status)) return status;
        filter.layerKey = *layers[index]; filter.subLayerKey = g_ProxySublayerKey;
        filter.displayData.name = L"PriceCheck protected game traffic";
        filter.action.type = FWP_ACTION_CALLOUT_TERMINATING;
        filter.action.calloutKey = *keys[index];
        filter.weight.type = FWP_EMPTY;
        status = FwpmFilterAdd0(g_ProxyEngine, &filter, NULL, NULL);
        if (!NT_SUCCESS(status)) return status;
    }
    return STATUS_SUCCESS;
}
