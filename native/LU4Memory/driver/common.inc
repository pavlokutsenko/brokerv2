/*
 * Source-owned module. Compiled through driver.c as one translation unit.
 * Keep protocol behavior synchronized with include/lu4_protocol.h.
 */

static NTSTATUS CompleteIrp(_Inout_ PIRP Irp, _In_ NTSTATUS Status, _In_ ULONG_PTR Information)
{
    Irp->IoStatus.Status = Status;
    Irp->IoStatus.Information = Information;
    IoCompleteRequest(Irp, IO_NO_INCREMENT);
    return Status;
}

static BOOLEAN IsValidUserRange(_In_ ULONGLONG Address, _In_ ULONG Size)
{
    ULONGLONG end;

    if (Size == 0 || Size > LU4_MAX_TRANSFER) {
        return FALSE;
    }

    end = Address + (ULONGLONG)Size - 1;
    if (end < Address) {
        return FALSE;
    }

    return end <= (ULONGLONG)(ULONG_PTR)MmHighestUserAddress;
}

static NTSTATUS LookupTargetProcess(_In_ ULONG ProcessId, _Out_ PEPROCESS* Process)
{
    if (ProcessId == 0) {
        return STATUS_INVALID_CID;
    }

    return PsLookupProcessByProcessId(ULongToHandle(ProcessId), Process);
}

