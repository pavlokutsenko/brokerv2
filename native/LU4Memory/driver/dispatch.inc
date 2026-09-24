/*
 * Source-owned module. Compiled through driver.c as one translation unit.
 * Keep protocol behavior synchronized with include/lu4_protocol.h.
 */

static NTSTATUS DispatchCreateClose(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp)
{
    UNREFERENCED_PARAMETER(DeviceObject);
    return CompleteIrp(Irp, STATUS_SUCCESS, 0);
}

static NTSTATUS DispatchDeviceControl(_In_ PDEVICE_OBJECT DeviceObject, _Inout_ PIRP Irp)
{
    PIO_STACK_LOCATION stack;
    PVOID buffer;
    ULONG inputLength;
    ULONG outputLength;
    ULONG controlCode;
    ULONG_PTR information = 0;
    NTSTATUS status;

    UNREFERENCED_PARAMETER(DeviceObject);

    stack = IoGetCurrentIrpStackLocation(Irp);
    buffer = Irp->AssociatedIrp.SystemBuffer;
    inputLength = stack->Parameters.DeviceIoControl.InputBufferLength;
    outputLength = stack->Parameters.DeviceIoControl.OutputBufferLength;
    controlCode = stack->Parameters.DeviceIoControl.IoControlCode;

    if (buffer == NULL) {
        return CompleteIrp(Irp, STATUS_INVALID_USER_BUFFER, 0);
    }

    switch (controlCode) {
    case IOCTL_LU4_GET_PROCESS_BASE:
        status = HandleGetBase(buffer, inputLength, outputLength, &information);
        break;
    case IOCTL_LU4_READ_PROCESS_MEMORY:
        status = HandleRead(buffer, inputLength, outputLength, &information);
        break;
    case IOCTL_LU4_WRITE_PROCESS_MEMORY:
        status = HandleWrite(buffer, inputLength, &information);
        break;
    case IOCTL_LU4_SUBMIT_TARGET:
        status = HandleSubmitTarget(buffer, inputLength, &information);
        break;
    case IOCTL_LU4_CLAIM_TARGET:
        status = HandleClaimTarget(buffer, inputLength, outputLength, &information);
        break;
    case IOCTL_LU4_COMPLETE_TARGET:
        status = HandleCompleteTarget(buffer, inputLength, &information);
        break;
    case IOCTL_LU4_QUERY_TARGET:
        status = HandleQueryTarget(buffer, inputLength, outputLength, &information);
        break;
    case IOCTL_LU4_QUERY_ACTIVE64:
        status = HandleQueryActive64(
            buffer, inputLength, outputLength, &information);
        break;
    case IOCTL_LU4_READ_ACTIVE64:
        status = HandleReadActive64(
            buffer, inputLength, outputLength, &information);
        break;
#ifdef LU4_PROBE_BUILD
    case IOCTL_LU4_PROBE_CAS_ACTIVE64_MAC:
        status = HandleProbeCasActive64Mac(
            buffer, inputLength, outputLength, &information);
        break;
    case IOCTL_LU4_PROBE_CAS_ACTIVE64_EIGHT:
        status = HandleProbeCasActive64Eight(
            buffer, inputLength, outputLength, &information);
        break;
#endif
    case IOCTL_LU4_ENCRYPT_PACKET:
        status = HandleEncryptPacket(
            buffer, inputLength, outputLength, &information);
        break;
    case IOCTL_LU4_ALLOCATE_PROCESS_MEMORY:
        status = HandleAllocateProcessMemory(
            buffer, inputLength, outputLength, &information);
        break;
    case IOCTL_LU4_PROTECT_PROCESS_MEMORY:
        status = HandleProtectProcessMemory(
            buffer, inputLength, outputLength, &information);
        break;
    case IOCTL_LU4_FREE_PROCESS_MEMORY:
        status = HandleFreeProcessMemory(
            buffer, inputLength, outputLength, &information);
        break;
    case IOCTL_LU4_SET_PROXY_REDIRECT:
        status = HandleProxyRedirect(
            DeviceObject, buffer, inputLength, outputLength, &information);
        break;
    default:
        status = STATUS_INVALID_DEVICE_REQUEST;
        break;
    }

    return CompleteIrp(Irp, status, information);
}

static VOID DriverUnload(_In_ PDRIVER_OBJECT DriverObject)
{
#ifndef LU4_PROBE_BUILD
    ProxyRedirectShutdown();
#endif
    IoDeleteSymbolicLink((PUNICODE_STRING)&g_SymbolicLink);
    if (DriverObject->DeviceObject != NULL) {
        IoDeleteDevice(DriverObject->DeviceObject);
    }
}

NTSTATUS DriverEntry(_In_ PDRIVER_OBJECT DriverObject, _In_ PUNICODE_STRING RegistryPath)
{
    PDEVICE_OBJECT deviceObject = NULL;
    NTSTATUS status;

    UNREFERENCED_PARAMETER(RegistryPath);

    if (!SelfTestPacketTransform()) {
        return STATUS_DATA_ERROR;
    }

    ExInitializeFastMutex(&g_TargetMutex);
    ExInitializeFastMutex(&g_CryptoMutex);
#ifndef LU4_PROBE_BUILD
    ProxyRedirectInitialize();
#endif
    RtlZeroMemory(&g_TargetCommand, sizeof(g_TargetCommand));
    g_TargetCommand.Version = LU4_PROTOCOL_VERSION;

    status = IoCreateDeviceSecure(
        DriverObject,
        0,
        (PUNICODE_STRING)&g_DeviceName,
        LU4_DEVICE_TYPE,
        FILE_DEVICE_SECURE_OPEN,
        FALSE,
        &SDDL_DEVOBJ_SYS_ALL_ADM_ALL,
        &GUID_DEVCLASS_LU4_MEMORY,
        &deviceObject);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    deviceObject->Flags |= DO_BUFFERED_IO;
    DriverObject->MajorFunction[IRP_MJ_CREATE] = DispatchCreateClose;
    DriverObject->MajorFunction[IRP_MJ_CLOSE] = DispatchCreateClose;
    DriverObject->MajorFunction[IRP_MJ_DEVICE_CONTROL] = DispatchDeviceControl;
    DriverObject->DriverUnload = DriverUnload;

    status = IoCreateSymbolicLink((PUNICODE_STRING)&g_SymbolicLink, (PUNICODE_STRING)&g_DeviceName);
    if (!NT_SUCCESS(status)) {
        IoDeleteDevice(deviceObject);
        return status;
    }

    deviceObject->Flags &= ~DO_DEVICE_INITIALIZING;
    return STATUS_SUCCESS;
}
