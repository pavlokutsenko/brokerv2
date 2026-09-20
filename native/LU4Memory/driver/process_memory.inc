/*
 * Source-owned module. Compiled through driver.c as one translation unit.
 * Keep protocol behavior synchronized with include/lu4_protocol.h.
 */

static NTSTATUS HandleGetBase(
    _Inout_updates_bytes_(BufferLength) PVOID Buffer,
    _In_ ULONG InputLength,
    _In_ ULONG OutputLength,
    _Out_ PULONG_PTR Information)
{
    PLU4_BASE_REQUEST request;
    PEPROCESS process = NULL;
    NTSTATUS status;

    if (InputLength < sizeof(LU4_BASE_REQUEST) || OutputLength < sizeof(LU4_BASE_REQUEST)) {
        return STATUS_BUFFER_TOO_SMALL;
    }

    request = (PLU4_BASE_REQUEST)Buffer;
    if (request->Version != LU4_PROTOCOL_VERSION) {
        return STATUS_REVISION_MISMATCH;
    }

    status = LookupTargetProcess(request->ProcessId, &process);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    request->BaseAddress = (ULONGLONG)(ULONG_PTR)PsGetProcessSectionBaseAddress(process);
    ObDereferenceObject(process);
    *Information = sizeof(LU4_BASE_REQUEST);
    return request->BaseAddress ? STATUS_SUCCESS : STATUS_NOT_FOUND;
}

static NTSTATUS HandleRead(
    _Inout_updates_bytes_(BufferLength) PVOID Buffer,
    _In_ ULONG InputLength,
    _In_ ULONG OutputLength,
    _Out_ PULONG_PTR Information)
{
    PLU4_COPY_REQUEST request;
    PEPROCESS process = NULL;
    SIZE_T copied = 0;
    SIZE_T required;
    NTSTATUS status;

    if (InputLength < (ULONG)LU4_COPY_HEADER_SIZE) {
        return STATUS_BUFFER_TOO_SMALL;
    }

    request = (PLU4_COPY_REQUEST)Buffer;
    if (request->Version != LU4_PROTOCOL_VERSION) {
        return STATUS_REVISION_MISMATCH;
    }
    if (!IsValidUserRange(request->Address, request->Size)) {
        return STATUS_INVALID_PARAMETER;
    }

    required = (SIZE_T)LU4_COPY_HEADER_SIZE + request->Size;
    if (required > (SIZE_T)OutputLength) {
        return STATUS_BUFFER_TOO_SMALL;
    }

    status = LookupTargetProcess(request->ProcessId, &process);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    status = MmCopyVirtualMemory(
        process,
        (PVOID)(ULONG_PTR)request->Address,
        PsGetCurrentProcess(),
        request->Data,
        request->Size,
        KernelMode,
        &copied);

    ObDereferenceObject(process);
    request->BytesTransferred = (ULONG)copied;
    *Information = LU4_COPY_HEADER_SIZE + copied;
    return status;
}

static NTSTATUS HandleWrite(
    _Inout_updates_bytes_(BufferLength) PVOID Buffer,
    _In_ ULONG InputLength,
    _Out_ PULONG_PTR Information)
{
    PLU4_COPY_REQUEST request;
    PEPROCESS process = NULL;
    SIZE_T copied = 0;
    SIZE_T required;
    NTSTATUS status;

    if (InputLength < (ULONG)LU4_COPY_HEADER_SIZE) {
        return STATUS_BUFFER_TOO_SMALL;
    }

    request = (PLU4_COPY_REQUEST)Buffer;
    if (request->Version != LU4_PROTOCOL_VERSION) {
        return STATUS_REVISION_MISMATCH;
    }
    if (!IsValidUserRange(request->Address, request->Size)) {
        return STATUS_INVALID_PARAMETER;
    }

    required = (SIZE_T)LU4_COPY_HEADER_SIZE + request->Size;
    if (required > (SIZE_T)InputLength) {
        return STATUS_BUFFER_TOO_SMALL;
    }

    status = LookupTargetProcess(request->ProcessId, &process);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    status = MmCopyVirtualMemory(
        PsGetCurrentProcess(),
        request->Data,
        process,
        (PVOID)(ULONG_PTR)request->Address,
        request->Size,
        KernelMode,
        &copied);

    ObDereferenceObject(process);
    request->BytesTransferred = (ULONG)copied;
    *Information = LU4_COPY_HEADER_SIZE;
    return status;
}

