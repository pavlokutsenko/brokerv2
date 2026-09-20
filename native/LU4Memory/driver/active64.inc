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

