/*
 * Source-owned module. Compiled through driver.c as one translation unit.
 * Keep protocol behavior synchronized with include/lu4_protocol.h.
 */

static NTSTATUS ValidateTargetCommand(_In_ const LU4_TARGET_COMMAND* Command)
{
    if (Command->Version != LU4_PROTOCOL_VERSION) {
        return STATUS_REVISION_MISMATCH;
    }
    if (Command->ProcessId == 0 || Command->Sequence == 0 || Command->ObjectId <= 0) {
        return STATUS_INVALID_PARAMETER;
    }
    if (Command->Flags != 0) {
        return STATUS_NOT_SUPPORTED;
    }
    return STATUS_SUCCESS;
}

static NTSTATUS HandleSubmitTarget(
    _Inout_updates_bytes_(BufferLength) PVOID Buffer,
    _In_ ULONG InputLength,
    _Out_ PULONG_PTR Information)
{
    PLU4_TARGET_COMMAND command;
    NTSTATUS status;

    if (InputLength < sizeof(LU4_TARGET_COMMAND)) {
        return STATUS_BUFFER_TOO_SMALL;
    }
    command = (PLU4_TARGET_COMMAND)Buffer;
    status = ValidateTargetCommand(command);
    if (!NT_SUCCESS(status)) {
        return status;
    }

    ExAcquireFastMutex(&g_TargetMutex);
    if (g_TargetCommand.State == LU4_TARGET_STATE_PENDING ||
        g_TargetCommand.State == LU4_TARGET_STATE_CLAIMED) {
        status = STATUS_DEVICE_BUSY;
    }
    else {
        g_TargetCommand = *command;
        g_TargetCommand.State = LU4_TARGET_STATE_PENDING;
        g_TargetCommand.Result = LU4_TARGET_RESULT_NONE;
        g_TargetCommand.Reserved = 0;
        *command = g_TargetCommand;
        *Information = sizeof(LU4_TARGET_COMMAND);
        status = STATUS_SUCCESS;
    }
    ExReleaseFastMutex(&g_TargetMutex);
    return status;
}

static NTSTATUS HandleClaimTarget(
    _Inout_updates_bytes_(BufferLength) PVOID Buffer,
    _In_ ULONG InputLength,
    _In_ ULONG OutputLength,
    _Out_ PULONG_PTR Information)
{
    PLU4_TARGET_COMMAND request;
    ULONG callerPid;
    NTSTATUS status = STATUS_NO_MORE_ENTRIES;

    if (InputLength < sizeof(LU4_TARGET_COMMAND) || OutputLength < sizeof(LU4_TARGET_COMMAND)) {
        return STATUS_BUFFER_TOO_SMALL;
    }
    request = (PLU4_TARGET_COMMAND)Buffer;
    if (request->Version != LU4_PROTOCOL_VERSION) {
        return STATUS_REVISION_MISMATCH;
    }
    callerPid = HandleToULong(PsGetCurrentProcessId());

    ExAcquireFastMutex(&g_TargetMutex);
    if (g_TargetCommand.State == LU4_TARGET_STATE_PENDING &&
        g_TargetCommand.ProcessId == callerPid &&
        (request->ProcessId == 0 || request->ProcessId == callerPid)) {
        g_TargetCommand.State = LU4_TARGET_STATE_CLAIMED;
        *request = g_TargetCommand;
        *Information = sizeof(LU4_TARGET_COMMAND);
        status = STATUS_SUCCESS;
    }
    ExReleaseFastMutex(&g_TargetMutex);
    return status;
}

static NTSTATUS HandleCompleteTarget(
    _Inout_updates_bytes_(BufferLength) PVOID Buffer,
    _In_ ULONG InputLength,
    _Out_ PULONG_PTR Information)
{
    PLU4_TARGET_COMMAND request;
    ULONG callerPid;
    NTSTATUS status = STATUS_NOT_FOUND;

    if (InputLength < sizeof(LU4_TARGET_COMMAND)) {
        return STATUS_BUFFER_TOO_SMALL;
    }
    request = (PLU4_TARGET_COMMAND)Buffer;
    if (request->Version != LU4_PROTOCOL_VERSION) {
        return STATUS_REVISION_MISMATCH;
    }
    if (request->Result < LU4_TARGET_RESULT_OK ||
        request->Result > LU4_TARGET_RESULT_EXCEPTION) {
        return STATUS_INVALID_PARAMETER;
    }
    callerPid = HandleToULong(PsGetCurrentProcessId());

    ExAcquireFastMutex(&g_TargetMutex);
    if (g_TargetCommand.State == LU4_TARGET_STATE_CLAIMED &&
        g_TargetCommand.ProcessId == callerPid &&
        g_TargetCommand.Sequence == request->Sequence) {
        g_TargetCommand.State = LU4_TARGET_STATE_COMPLETED;
        g_TargetCommand.Result = request->Result;
        *request = g_TargetCommand;
        *Information = sizeof(LU4_TARGET_COMMAND);
        status = STATUS_SUCCESS;
    }
    ExReleaseFastMutex(&g_TargetMutex);
    return status;
}

static NTSTATUS HandleQueryTarget(
    _Inout_updates_bytes_(BufferLength) PVOID Buffer,
    _In_ ULONG InputLength,
    _In_ ULONG OutputLength,
    _Out_ PULONG_PTR Information)
{
    PLU4_TARGET_COMMAND request;

    if (InputLength < sizeof(LU4_TARGET_COMMAND) || OutputLength < sizeof(LU4_TARGET_COMMAND)) {
        return STATUS_BUFFER_TOO_SMALL;
    }
    request = (PLU4_TARGET_COMMAND)Buffer;
    if (request->Version != LU4_PROTOCOL_VERSION) {
        return STATUS_REVISION_MISMATCH;
    }

    ExAcquireFastMutex(&g_TargetMutex);
    if (request->Sequence != 0 && request->Sequence != g_TargetCommand.Sequence) {
        ExReleaseFastMutex(&g_TargetMutex);
        return STATUS_NOT_FOUND;
    }
    *request = g_TargetCommand;
    *Information = sizeof(LU4_TARGET_COMMAND);
    ExReleaseFastMutex(&g_TargetMutex);
    return STATUS_SUCCESS;
}

