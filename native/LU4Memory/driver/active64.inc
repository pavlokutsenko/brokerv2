/*
 * Source-owned module. Compiled through driver.c as one translation unit.
 * Keep protocol behavior synchronized with include/lu4_protocol.h.
 */

static NTSTATUS FindActive64Module(_Out_ PVOID* BaseAddress, _Out_ PULONG ImageSize)
{
    PAUX_MODULE_EXTENDED_INFO modules = NULL;
    ULONG bytes = 0;
    ULONG count;
    ULONG index;
    NTSTATUS status;
    ANSI_STRING expectedName;

    *BaseAddress = NULL;
    *ImageSize = 0;
    status = AuxKlibInitialize();
    if (!NT_SUCCESS(status)) {
        return status;
    }

    status = AuxKlibQueryModuleInformation(&bytes, sizeof(AUX_MODULE_EXTENDED_INFO), NULL);
    if (!NT_SUCCESS(status) || bytes == 0) {
        return NT_SUCCESS(status) ? STATUS_NOT_FOUND : status;
    }

    modules = (PAUX_MODULE_EXTENDED_INFO)ExAllocatePool2(
        POOL_FLAG_NON_PAGED, bytes, '46UL');
    if (modules == NULL) {
        return STATUS_INSUFFICIENT_RESOURCES;
    }
    RtlZeroMemory(modules, bytes);
    status = AuxKlibQueryModuleInformation(
        &bytes, sizeof(AUX_MODULE_EXTENDED_INFO), modules);
    if (!NT_SUCCESS(status)) {
        ExFreePoolWithTag(modules, '46UL');
        return status;
    }

    RtlInitAnsiString(&expectedName, g_Active64Name);
    count = bytes / sizeof(AUX_MODULE_EXTENDED_INFO);
    status = STATUS_NOT_FOUND;
    for (index = 0; index < count; ++index) {
        ANSI_STRING moduleName;
        const char* name = (const char*)modules[index].FullPathName +
            modules[index].FileNameOffset;
        RtlInitAnsiString(&moduleName, name);
        if (RtlEqualString(&moduleName, &expectedName, TRUE)) {
            *BaseAddress = modules[index].BasicInfo.ImageBase;
            *ImageSize = modules[index].ImageSize;
            status = STATUS_SUCCESS;
            break;
        }
    }

    ExFreePoolWithTag(modules, '46UL');
    return status;
}

static NTSTATUS HandleQueryActive64(
    _Inout_updates_bytes_(BufferLength) PVOID Buffer,
    _In_ ULONG InputLength,
    _In_ ULONG OutputLength,
    _Out_ PULONG_PTR Information)
{
    PLU4_KERNEL_MODULE_INFO request;
    PVOID baseAddress;
    ULONG imageSize;
    NTSTATUS status;

    if (InputLength < sizeof(LU4_KERNEL_MODULE_INFO) ||
        OutputLength < sizeof(LU4_KERNEL_MODULE_INFO)) {
        return STATUS_BUFFER_TOO_SMALL;
    }
    request = (PLU4_KERNEL_MODULE_INFO)Buffer;
    if (request->Version != LU4_PROTOCOL_VERSION) {
        return STATUS_REVISION_MISMATCH;
    }

    status = FindActive64Module(&baseAddress, &imageSize);
    if (!NT_SUCCESS(status)) {
        return status;
    }
    request->Reserved = 0;
    request->BaseAddress = (ULONGLONG)(ULONG_PTR)baseAddress;
    request->ImageSize = imageSize;
    request->Reserved2 = 0;
    *Information = sizeof(LU4_KERNEL_MODULE_INFO);
    return STATUS_SUCCESS;
}

static NTSTATUS HandleReadActive64(
    _Inout_updates_bytes_(BufferLength) PVOID Buffer,
    _In_ ULONG InputLength,
    _In_ ULONG OutputLength,
    _Out_ PULONG_PTR Information)
{
    PLU4_MODULE_READ_REQUEST request;
    MM_COPY_ADDRESS source;
    PVOID baseAddress;
    ULONG imageSize;
    SIZE_T copied = 0;
    SIZE_T required;
    NTSTATUS status;

    if (InputLength < (ULONG)LU4_MODULE_READ_HEADER_SIZE) {
        return STATUS_BUFFER_TOO_SMALL;
    }
    request = (PLU4_MODULE_READ_REQUEST)Buffer;
    if (request->Version != LU4_PROTOCOL_VERSION) {
        return STATUS_REVISION_MISMATCH;
    }
    if (request->Size == 0 || request->Size > LU4_MAX_TRANSFER) {
        return STATUS_INVALID_PARAMETER;
    }
    required = (SIZE_T)LU4_MODULE_READ_HEADER_SIZE + request->Size;
    if (required > OutputLength) {
        return STATUS_BUFFER_TOO_SMALL;
    }

    status = FindActive64Module(&baseAddress, &imageSize);
    if (!NT_SUCCESS(status)) {
        return status;
    }
    if (request->Offset >= imageSize ||
        request->Size > imageSize - (ULONG)request->Offset) {
        return STATUS_INVALID_PARAMETER;
    }

    source.VirtualAddress = (PUCHAR)baseAddress + (ULONG_PTR)request->Offset;
    status = MmCopyMemory(
        request->Data,
        source,
        request->Size,
        MM_COPY_MEMORY_VIRTUAL,
        &copied);
    request->Reserved = 0;
    request->BytesTransferred = (ULONG)copied;
    *Information = LU4_MODULE_READ_HEADER_SIZE + copied;
    return status;
}

#ifdef LU4_PROBE_BUILD
/* A disposable probe's only write path: two adjacent six-byte active64 data slots. */
static NTSTATUS HandleProbeCasActive64Mac(
    _Inout_updates_bytes_(BufferLength) PVOID Buffer,
    _In_ ULONG InputLength,
    _In_ ULONG OutputLength,
    _Out_ PULONG_PTR Information)
{
    PLU4_PROBE_ACTIVE64_MAC_REQUEST request;
    PVOID baseAddress;
    ULONG imageSize;
    UCHAR current[6];
    MM_COPY_ADDRESS source;
    SIZE_T copied = 0;
    NTSTATUS status;
    const ULONG dataOffset = 0x12ABD50u;
    const ULONG macOffset = 0x12ABD56u;
    ULONG targetOffset;

    if (InputLength != sizeof(LU4_PROBE_ACTIVE64_MAC_REQUEST) ||
        OutputLength < sizeof(LU4_PROBE_ACTIVE64_MAC_REQUEST)) {
        return STATUS_BUFFER_TOO_SMALL;
    }
    request = (PLU4_PROBE_ACTIVE64_MAC_REQUEST)Buffer;
    request->BytesWritten = 0;
    if (request->Version != LU4_PROTOCOL_VERSION ||
        (request->Offset != dataOffset && request->Offset != macOffset)) {
        return STATUS_INVALID_PARAMETER;
    }
    targetOffset = request->Offset;
    if (KeGetCurrentIrql() != PASSIVE_LEVEL) {
        return STATUS_INVALID_DEVICE_STATE;
    }
    status = FindActive64Module(&baseAddress, &imageSize);
    if (!NT_SUCCESS(status)) {
        return status;
    }
    if (imageSize <= targetOffset || imageSize - targetOffset < sizeof(current)) {
        return STATUS_INVALID_PARAMETER;
    }
    source.VirtualAddress = (PUCHAR)baseAddress + targetOffset;
    status = MmCopyMemory(current, source, sizeof(current),
        MM_COPY_MEMORY_VIRTUAL, &copied);
    if (!NT_SUCCESS(status) || copied != sizeof(current)) {
        return NT_SUCCESS(status) ? STATUS_PARTIAL_COPY : status;
    }
    if (RtlCompareMemory(current, request->Expected, sizeof(current)) != sizeof(current)) {
        return STATUS_DATA_ERROR;
    }
    __try {
        RtlCopyMemory((PUCHAR)baseAddress + targetOffset,
            request->Replacement, sizeof(current));
        if (RtlCompareMemory((PUCHAR)baseAddress + targetOffset,
                request->Replacement, sizeof(current)) != sizeof(current)) {
            return STATUS_DATA_ERROR;
        }
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        return GetExceptionCode();
    }
    request->BytesWritten = sizeof(current);
    *Information = sizeof(*request);
    return STATUS_SUCCESS;
}

/* Test-only guarded CAS for the eight-byte 0x222044 trailer source. */
static NTSTATUS HandleProbeCasActive64Eight(
    _Inout_updates_bytes_(BufferLength) PVOID Buffer,
    _In_ ULONG InputLength,
    _In_ ULONG OutputLength,
    _Out_ PULONG_PTR Information)
{
    PLU4_PROBE_ACTIVE64_EIGHT_REQUEST request;
    PVOID baseAddress;
    ULONG imageSize;
    UCHAR current[8];
    MM_COPY_ADDRESS source;
    SIZE_T copied = 0;
    NTSTATUS status;
    const ULONG targetOffset = 0x12ABD68u;

    if (InputLength != sizeof(LU4_PROBE_ACTIVE64_EIGHT_REQUEST) ||
        OutputLength < sizeof(LU4_PROBE_ACTIVE64_EIGHT_REQUEST)) {
        return STATUS_BUFFER_TOO_SMALL;
    }
    request = (PLU4_PROBE_ACTIVE64_EIGHT_REQUEST)Buffer;
    request->BytesWritten = 0;
    if (request->Version != LU4_PROTOCOL_VERSION ||
        request->Offset != targetOffset) {
        return STATUS_INVALID_PARAMETER;
    }
    if (KeGetCurrentIrql() != PASSIVE_LEVEL) {
        return STATUS_INVALID_DEVICE_STATE;
    }
    status = FindActive64Module(&baseAddress, &imageSize);
    if (!NT_SUCCESS(status)) {
        return status;
    }
    if (imageSize <= targetOffset || imageSize - targetOffset < sizeof(current)) {
        return STATUS_INVALID_PARAMETER;
    }
    source.VirtualAddress = (PUCHAR)baseAddress + targetOffset;
    status = MmCopyMemory(current, source, sizeof(current),
        MM_COPY_MEMORY_VIRTUAL, &copied);
    if (!NT_SUCCESS(status) || copied != sizeof(current)) {
        return NT_SUCCESS(status) ? STATUS_PARTIAL_COPY : status;
    }
    if (RtlCompareMemory(current, request->Expected, sizeof(current)) != sizeof(current)) {
        return STATUS_DATA_ERROR;
    }
    __try {
        RtlCopyMemory((PUCHAR)baseAddress + targetOffset,
            request->Replacement, sizeof(current));
        if (RtlCompareMemory((PUCHAR)baseAddress + targetOffset,
                request->Replacement, sizeof(current)) != sizeof(current)) {
            return STATUS_DATA_ERROR;
        }
    }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        return GetExceptionCode();
    }
    request->BytesWritten = sizeof(current);
    *Information = sizeof(*request);
    return STATUS_SUCCESS;
}
#endif

