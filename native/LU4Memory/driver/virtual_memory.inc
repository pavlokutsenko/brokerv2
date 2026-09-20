/*
 * Source-owned module. Compiled through driver.c as one translation unit.
 * Keep protocol behavior synchronized with include/lu4_protocol.h.
 */

static BOOLEAN IsAllowedProtection(_In_ ULONG Protection)
{
    return Protection == PAGE_READONLY ||
        Protection == PAGE_READWRITE ||
        Protection == PAGE_EXECUTE_READ ||
        Protection == PAGE_EXECUTE_READWRITE;
}

static NTSTATUS HandleAllocateProcessMemory(
    _Inout_updates_bytes_(BufferLength) PVOID Buffer,
    _In_ ULONG InputLength,
    _In_ ULONG OutputLength,
    _Out_ PULONG_PTR Information)
{
    PLU4_VIRTUAL_MEMORY_REQUEST request;
    PEPROCESS process = NULL;
    KAPC_STATE apcState;
    PVOID address = NULL;
    SIZE_T size;
    NTSTATUS status;

    if (InputLength < sizeof(LU4_VIRTUAL_MEMORY_REQUEST) ||
        OutputLength < sizeof(LU4_VIRTUAL_MEMORY_REQUEST)) {
        return STATUS_BUFFER_TOO_SMALL;
    }
    request = (PLU4_VIRTUAL_MEMORY_REQUEST)Buffer;
    if (request->Version != LU4_PROTOCOL_VERSION) {
        return STATUS_REVISION_MISMATCH;
    }
    if (request->ProcessId == 0 ||
        request->Address != 0 ||
        request->Size == 0 ||
        request->Size > 0x10000 ||
        request->Protection != PAGE_EXECUTE_READWRITE ||
        request->OldProtection != 0 ||
        request->Reserved != 0) {
        return STATUS_INVALID_PARAMETER;
    }

    status = LookupTargetProcess(request->ProcessId, &process);
    if (!NT_SUCCESS(status)) {
        return status;
    }
    size = (SIZE_T)request->Size;
    KeStackAttachProcess(process, &apcState);
    status = ZwAllocateVirtualMemory(
        NtCurrentProcess(),
        &address,
        0,
        &size,
        MEM_RESERVE | MEM_COMMIT,
        PAGE_EXECUTE_READWRITE);
    KeUnstackDetachProcess(&apcState);
    ObDereferenceObject(process);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    request->Address = (ULONGLONG)(ULONG_PTR)address;
    request->Size = size;
    request->OldProtection = 0;
    *Information = sizeof(LU4_VIRTUAL_MEMORY_REQUEST);
    return STATUS_SUCCESS;
}

static NTSTATUS HandleProtectProcessMemory(
    _Inout_updates_bytes_(BufferLength) PVOID Buffer,
    _In_ ULONG InputLength,
    _In_ ULONG OutputLength,
    _Out_ PULONG_PTR Information)
{
    PLU4_VIRTUAL_MEMORY_REQUEST request;
    PEPROCESS process = NULL;
    KAPC_STATE apcState;
    PVOID address;
    SIZE_T size;
    ULONG oldProtection = 0;
    NTSTATUS status;

    if (InputLength < sizeof(LU4_VIRTUAL_MEMORY_REQUEST) ||
        OutputLength < sizeof(LU4_VIRTUAL_MEMORY_REQUEST)) {
        return STATUS_BUFFER_TOO_SMALL;
    }
    request = (PLU4_VIRTUAL_MEMORY_REQUEST)Buffer;
    if (request->Version != LU4_PROTOCOL_VERSION) {
        return STATUS_REVISION_MISMATCH;
    }
    if (request->ProcessId == 0 ||
        request->Size == 0 ||
        request->Size > 0x10000 ||
        !IsValidUserRange(request->Address, (ULONG)request->Size) ||
        !IsAllowedProtection(request->Protection) ||
        request->OldProtection != 0 ||
        request->Reserved != 0) {
        return STATUS_INVALID_PARAMETER;
    }

    status = LookupTargetProcess(request->ProcessId, &process);
    if (!NT_SUCCESS(status)) {
        return status;
    }
    address = (PVOID)(ULONG_PTR)request->Address;
    size = (SIZE_T)request->Size;
    KeStackAttachProcess(process, &apcState);
    status = ZwProtectVirtualMemory(
        NtCurrentProcess(),
        &address,
        &size,
        request->Protection,
        &oldProtection);
    KeUnstackDetachProcess(&apcState);
    ObDereferenceObject(process);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    request->Address = (ULONGLONG)(ULONG_PTR)address;
    request->Size = size;
    request->OldProtection = oldProtection;
    *Information = sizeof(LU4_VIRTUAL_MEMORY_REQUEST);
    return STATUS_SUCCESS;
}

static NTSTATUS HandleFreeProcessMemory(
    _Inout_updates_bytes_(BufferLength) PVOID Buffer,
    _In_ ULONG InputLength,
    _In_ ULONG OutputLength,
    _Out_ PULONG_PTR Information)
{
    PLU4_VIRTUAL_MEMORY_REQUEST request;
    PEPROCESS process = NULL;
    KAPC_STATE apcState;
    PVOID address;
    SIZE_T size = 0;
    NTSTATUS status;

    if (InputLength < sizeof(LU4_VIRTUAL_MEMORY_REQUEST) ||
        OutputLength < sizeof(LU4_VIRTUAL_MEMORY_REQUEST)) {
        return STATUS_BUFFER_TOO_SMALL;
    }
    request = (PLU4_VIRTUAL_MEMORY_REQUEST)Buffer;
    if (request->Version != LU4_PROTOCOL_VERSION) {
        return STATUS_REVISION_MISMATCH;
    }
    if (request->ProcessId == 0 ||
        request->Address == 0 ||
        request->Address > (ULONGLONG)(ULONG_PTR)MmHighestUserAddress ||
        request->Size != 0 ||
        request->Protection != 0 ||
        request->OldProtection != 0 ||
        request->Reserved != 0) {
        return STATUS_INVALID_PARAMETER;
    }

    status = LookupTargetProcess(request->ProcessId, &process);
    if (!NT_SUCCESS(status)) {
        return status;
    }
    address = (PVOID)(ULONG_PTR)request->Address;
    KeStackAttachProcess(process, &apcState);
    status = ZwFreeVirtualMemory(
        NtCurrentProcess(),
        &address,
        &size,
        MEM_RELEASE);
    KeUnstackDetachProcess(&apcState);
    ObDereferenceObject(process);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    request->Address = 0;
    request->Size = 0;
    *Information = sizeof(LU4_VIRTUAL_MEMORY_REQUEST);
    return STATUS_SUCCESS;
}

