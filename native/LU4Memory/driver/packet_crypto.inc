/*
 * Source-owned module. Compiled through driver.c as one translation unit.
 * Keep protocol behavior synchronized with include/lu4_protocol.h.
 */

static NTSTATUS HandleEncryptPacket(
    _Inout_updates_bytes_(BufferLength) PVOID Buffer,
    _In_ ULONG InputLength,
    _In_ ULONG OutputLength,
    _Out_ PULONG_PTR Information)
{
    PLU4_PACKET_CRYPTO_REQUEST request;
    PEPROCESS process = NULL;
    UCHAR originalKey[LU4_ROLLING_KEY_SIZE];
    UCHAR transformedKey[LU4_ROLLING_KEY_SIZE];
    UCHAR* packetStorage = NULL;
    UCHAR* originalPacket;
    UCHAR* transformedPacket;
    PVOID active64Base = NULL;
    ULONG active64Size = 0;
    ULONGLONG slotOffset = 0;
    LU4_RC4_STATE originalState = { 0 };
    LU4_RC4_STATE transformedState = { 0 };
    BOOLEAN fullTransform;
    BOOLEAN processSuspended = FALSE;
    NTSTATUS status;

    if (InputLength < sizeof(LU4_PACKET_CRYPTO_REQUEST) ||
        OutputLength < sizeof(LU4_PACKET_CRYPTO_REQUEST)) {
        return STATUS_BUFFER_TOO_SMALL;
    }
    request = (PLU4_PACKET_CRYPTO_REQUEST)Buffer;
    request->BytesTransferred = 0;
    if (request->Version != LU4_PROTOCOL_VERSION) {
        return STATUS_REVISION_MISMATCH;
    }
    if (request->ProcessId == 0 ||
        request->ProcessId == HandleToULong(PsGetCurrentProcessId()) ||
        request->Size == 0 ||
        request->Size > LU4_MAX_PACKET_SIZE ||
        request->CounterBias != 0 ||
        !IsValidUserRange(request->BufferAddress, request->Size) ||
        !IsValidUserRange(request->RollingKeyAddress, LU4_ROLLING_KEY_SIZE)) {
        return STATUS_INVALID_PARAMETER;
    }
    fullTransform = request->StateIndex == LU4_TARGET_STATE_INDEX;
    if ((!fullTransform && (request->StateIndex != 0 || request->StateOffset != 0)) ||
        (fullTransform && request->StateOffset < 0x240CC)) {
        return STATUS_INVALID_PARAMETER;
    }

    if (fullTransform) {
        status = FindActive64Module(&active64Base, &active64Size);
        if (!NT_SUCCESS(status)) {
            return status;
        }
        if (request->StateOffset > active64Size ||
            sizeof(LU4_RC4_STATE) > active64Size - request->StateOffset) {
            return STATUS_INVALID_PARAMETER;
        }
        slotOffset = request->StateOffset - 0x240CC;
        if (slotOffset > active64Size || 0x64 > active64Size - slotOffset ||
            *((volatile UCHAR*)((UCHAR*)active64Base + slotOffset + 0x50)) != 1 ||
            *((volatile ULONG*)((UCHAR*)active64Base + slotOffset + 0x60)) != request->ProcessId) {
            return STATUS_NOT_FOUND;
        }
    }

    status = LookupTargetProcess(request->ProcessId, &process);
    if (!NT_SUCCESS(status)) {
        return status;
    }
    packetStorage = (UCHAR*)ExAllocatePool2(
        POOL_FLAG_NON_PAGED,
        (SIZE_T)request->Size * 2,
        'c4UL');
    if (packetStorage == NULL) {
        ObDereferenceObject(process);
        return STATUS_INSUFFICIENT_RESOURCES;
    }
    originalPacket = packetStorage;
    transformedPacket = packetStorage + request->Size;

    ExAcquireFastMutex(&g_CryptoMutex);
    status = PsSuspendProcess(process);
    if (!NT_SUCCESS(status)) {
        goto Cleanup;
    }
    processSuspended = TRUE;
    if (fullTransform) {
        RtlCopyMemory(
            &originalState,
            (UCHAR*)active64Base + request->StateOffset,
            sizeof(originalState));
        if (!IsValidRc4State(&originalState)) {
            status = STATUS_DATA_ERROR;
            goto Cleanup;
        }
    }
    status = CopyFromTargetProcess(
        process,
        request->RollingKeyAddress,
        originalKey,
        sizeof(originalKey));
    if (!NT_SUCCESS(status)) {
        goto Cleanup;
    }
    status = CopyFromTargetProcess(
        process,
        request->BufferAddress,
        originalPacket,
        request->Size);
    if (!NT_SUCCESS(status)) {
        goto Cleanup;
    }

    RtlCopyMemory(transformedKey, originalKey, sizeof(transformedKey));
    RtlCopyMemory(transformedPacket, originalPacket, request->Size);
    TransformType2Packet(
        transformedPacket,
        request->Size,
        transformedKey);
    if (fullTransform) {
        transformedState = originalState;
        TransformRc4Packet(transformedPacket, request->Size, &transformedState);
    }

    status = CopyToTargetProcess(
        process,
        request->BufferAddress,
        transformedPacket,
        request->Size);
    if (!NT_SUCCESS(status)) {
        goto Cleanup;
    }
    status = CopyToTargetProcess(
        process,
        request->RollingKeyAddress,
        transformedKey,
        sizeof(transformedKey));
    if (!NT_SUCCESS(status)) {
        (VOID)CopyToTargetProcess(
            process,
            request->BufferAddress,
            originalPacket,
            request->Size);
        goto Cleanup;
    }
    if (fullTransform) {
        RtlCopyMemory(
            (UCHAR*)active64Base + request->StateOffset,
            &transformedState,
            sizeof(transformedState));
    }
    request->BytesTransferred = request->Size;
    *Information = sizeof(LU4_PACKET_CRYPTO_REQUEST);

Cleanup:
    if (processSuspended) {
        NTSTATUS resumeStatus = PsResumeProcess(process);
        if (!NT_SUCCESS(resumeStatus) && NT_SUCCESS(status)) {
            status = resumeStatus;
        }
    }
    ExReleaseFastMutex(&g_CryptoMutex);
    RtlSecureZeroMemory(packetStorage, (SIZE_T)request->Size * 2);
    ExFreePoolWithTag(packetStorage, 'c4UL');
    ObDereferenceObject(process);
    return status;
}

