/* Windows-managed residency only. No allocation release, commit cap or callback patching. */
#define LU4_PROCESS_QUERY_INFORMATION 0x0400u
#define LU4_PROCESS_SET_QUOTA 0x0100u
NTSYSAPI NTSTATUS NTAPI ZwQueryInformationProcess(
    HANDLE ProcessHandle, PROCESSINFOCLASS ProcessInformationClass,
    PVOID ProcessInformation, ULONG ProcessInformationLength, PULONG ReturnLength);
NTSYSAPI NTSTATUS NTAPI ZwSetInformationProcess(
    HANDLE ProcessHandle, PROCESSINFOCLASS ProcessInformationClass,
    PVOID ProcessInformation, ULONG ProcessInformationLength);

static NTSTATUS HandleWorkingSet(PVOID buffer, ULONG inputLength,
    ULONG outputLength, PULONG_PTR information)
{
    PLU4_WORKING_SET_REQUEST request = buffer;
    PEPROCESS process = NULL;
    HANDLE handle = NULL;
    QUOTA_LIMITS current;
    QUOTA_LIMITS_EX limits;
    LU4_PROXY_ROUTE route;
    PUBLIC_OBJECT_BASIC_INFORMATION handleInformation;
    const ACCESS_MASK requiredAccess = LU4_PROCESS_QUERY_INFORMATION | LU4_PROCESS_SET_QUOTA;
    NTSTATUS status;
    if (KeGetCurrentIrql() != PASSIVE_LEVEL) return STATUS_INVALID_DEVICE_STATE;
    if (inputLength != sizeof(*request) || outputLength < sizeof(*request))
        return STATUS_BUFFER_TOO_SMALL;
    if (request->Version != LU4_PROTOCOL_VERSION || !request->ProcessId ||
        !request->CreationTime || request->Reserved ||
        (request->Operation != LU4_WORKING_SET_APPLY && request->Operation != LU4_WORKING_SET_RESTORE))
        return STATUS_INVALID_PARAMETER;
    if (request->MinimumBytes < 20u * PAGE_SIZE || request->MinimumBytes > request->MaximumBytes ||
        (request->Operation == LU4_WORKING_SET_APPLY &&
            (request->MaximumBytes < 256ull * 1024 * 1024 ||
             request->MaximumBytes > 64ull * 1024 * 1024 * 1024 || request->Flags != 6u)) ||
        (request->Flags & ~0x0fu) || (request->Flags & 3u) == 3u || (request->Flags & 12u) == 12u)
        return STATUS_INVALID_PARAMETER;
    /* Existing route ownership is the authorization boundary for foreign clients. */
    if (request->ProcessId != HandleToULong(PsGetCurrentProcessId())) {
        route = ProxyRoute(request->ProcessId);
        if (route.ProcessId != request->ProcessId || !route.ListenerPort ||
            route.HostProcessId != HandleToULong(PsGetCurrentProcessId())) return STATUS_ACCESS_DENIED;
    }
    status = LookupTargetProcess(request->ProcessId, &process);
    if (!NT_SUCCESS(status)) return status;
    if ((ULONGLONG)PsGetProcessCreateTimeQuadPart(process) != request->CreationTime ||
        PsGetProcessExitStatus(process) != STATUS_PENDING) {
        ObDereferenceObject(process);
        return STATUS_INVALID_CID;
    }
    /* Keep Windows access checks enabled even though this request runs in kernel mode. */
    status = ObOpenObjectByPointer(process, OBJ_KERNEL_HANDLE | OBJ_FORCE_ACCESS_CHECK,
        NULL, requiredAccess, *PsProcessType, KernelMode, &handle);
    if (NT_SUCCESS(status)) {
        RtlZeroMemory(&handleInformation, sizeof(handleInformation));
        status = ZwQueryObject(handle, ObjectBasicInformation, &handleInformation, sizeof(handleInformation), NULL);
        /* Open can succeed with rights removed by a process-handle callback.
         * Zw calls trust kernel handles, so explicitly reject that reduced grant. */
        if (NT_SUCCESS(status) && (handleInformation.GrantedAccess & requiredAccess) != requiredAccess)
            status = STATUS_ACCESS_DENIED;
        RtlZeroMemory(&current, sizeof(current));
        if (NT_SUCCESS(status))
            status = ZwQueryInformationProcess(handle, ProcessQuotaLimits, &current, sizeof(current), NULL);
        if (NT_SUCCESS(status)) {
            RtlZeroMemory(&limits, sizeof(limits));
            RtlCopyMemory(&limits, &current, sizeof(current));
            limits.MinimumWorkingSetSize = (SIZE_T)request->MinimumBytes;
            limits.MaximumWorkingSetSize = (SIZE_T)request->MaximumBytes;
            limits.Flags = request->Flags;
            status = ZwSetInformationProcess(handle, ProcessQuotaLimits, &limits, sizeof(limits));
            if (NT_SUCCESS(status)) {
                status = ZwQueryInformationProcess(handle, ProcessQuotaLimits, &current, sizeof(current), NULL);
                if (NT_SUCCESS(status)) {
                    request->MinimumBytes = current.MinimumWorkingSetSize;
                    request->MaximumBytes = current.MaximumWorkingSetSize;
                }
            }
        }
        ZwClose(handle);
    }
    ObDereferenceObject(process);
    request->Status = status;
    *information = sizeof(*request);
    /* Transport success does not imply policy success: callers must inspect Status and read back. */
    return STATUS_SUCCESS;
}
