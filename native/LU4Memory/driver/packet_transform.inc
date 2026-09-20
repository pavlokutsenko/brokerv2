/*
 * Source-owned module. Compiled through driver.c as one translation unit.
 * Keep protocol behavior synchronized with include/lu4_protocol.h.
 */

static NTSTATUS CopyFromTargetProcess(
    _In_ PEPROCESS Process,
    _In_ ULONGLONG Address,
    _Out_writes_bytes_(Size) PVOID Destination,
    _In_ SIZE_T Size)
{
    SIZE_T copied = 0;
    NTSTATUS status = MmCopyVirtualMemory(
        Process,
        (PVOID)(ULONG_PTR)Address,
        PsGetCurrentProcess(),
        Destination,
        Size,
        KernelMode,
        &copied);
    if (NT_SUCCESS(status) && copied != Size) {
        return STATUS_PARTIAL_COPY;
    }
    return status;
}

static NTSTATUS CopyToTargetProcess(
    _In_ PEPROCESS Process,
    _In_ ULONGLONG Address,
    _In_reads_bytes_(Size) const VOID* Source,
    _In_ SIZE_T Size)
{
    SIZE_T copied = 0;
    NTSTATUS status = MmCopyVirtualMemory(
        PsGetCurrentProcess(),
        (PVOID)Source,
        Process,
        (PVOID)(ULONG_PTR)Address,
        Size,
        KernelMode,
        &copied);
    if (NT_SUCCESS(status) && copied != Size) {
        return STATUS_PARTIAL_COPY;
    }
    return status;
}

static VOID TransformType2Packet(
    _Inout_updates_bytes_(Size) UCHAR* Packet,
    _In_ ULONG Size,
    _Inout_updates_(LU4_ROLLING_KEY_SIZE) UCHAR* RollingKey)
{
    UCHAR localKey[LU4_ROLLING_KEY_SIZE];
    ULONGLONG counter;
    ULONG index;

    RtlCopyMemory(localKey, RollingKey, sizeof(localKey));
    Packet[0] ^= localKey[1];
    for (index = 1; index < Size; ++index) {
        Packet[index] ^=
            localKey[(index & 0x0F) + 1] ^ Packet[index - 1];
    }

    RtlCopyMemory(&counter, RollingKey + 9, sizeof(counter));
    counter += Size;
    RtlCopyMemory(RollingKey + 9, &counter, sizeof(counter));
}

static VOID TransformRc4Packet(
    _Inout_updates_bytes_(Size) UCHAR* Packet,
    _In_ ULONG Size,
    _Inout_ PLU4_RC4_STATE State)
{
    ULONG index;
    UCHAR first;
    UCHAR temporary;

    for (index = 0; index < Size; ++index) {
        State->I = (State->I + 1) & 0xFF;
        first = State->Sbox[State->I];
        State->J = (State->J + first) & 0xFF;
        temporary = State->Sbox[State->I];
        State->Sbox[State->I] = State->Sbox[State->J];
        State->Sbox[State->J] = temporary;
        Packet[index] ^= State->Sbox[(first + State->Sbox[State->I]) & 0xFF];
    }
}

static BOOLEAN IsValidRc4State(_In_ const LU4_RC4_STATE* State)
{
    UCHAR seen[LU4_RC4_SBOX_SIZE];
    ULONG index;

    if (State->I > 0xFF || State->J > 0xFF) {
        return FALSE;
    }
    RtlZeroMemory(seen, sizeof(seen));
    for (index = 0; index < LU4_RC4_SBOX_SIZE; ++index) {
        if (seen[State->Sbox[index]] != 0) {
            return FALSE;
        }
        seen[State->Sbox[index]] = 1;
    }
    return TRUE;
}

static BOOLEAN SelfTestPacketTransform(VOID)
{
    static const UCHAR expectedPacket[18] = {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09,
        0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x01, 0x12
    };
    static const UCHAR expectedKey[LU4_ROLLING_KEY_SIZE] = {
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x1B, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10
    };
    static const UCHAR expectedFullPacket[18] = {
        0x03, 0x07, 0x04, 0x09, 0x08, 0x11, 0x18, 0x20, 0x21,
        0x32, 0x39, 0x44, 0x5B, 0x6B, 0x7A, 0x96, 0x83, 0xB2
    };
    UCHAR packet[18];
    UCHAR key[LU4_ROLLING_KEY_SIZE];
    LU4_RC4_STATE state;
    ULONG index;

    for (index = 0; index < RTL_NUMBER_OF(packet); ++index) {
        packet[index] = (UCHAR)index;
    }
    for (index = 0; index < RTL_NUMBER_OF(key); ++index) {
        key[index] = (UCHAR)index;
    }
    TransformType2Packet(packet, sizeof(packet), key);
    if (RtlCompareMemory(packet, expectedPacket, sizeof(packet)) != sizeof(packet) ||
        RtlCompareMemory(key, expectedKey, sizeof(key)) != sizeof(key)) {
        return FALSE;
    }
    for (index = 0; index < RTL_NUMBER_OF(packet); ++index) {
        packet[index] = (UCHAR)index;
    }
    for (index = 0; index < RTL_NUMBER_OF(key); ++index) {
        key[index] = (UCHAR)index;
    }
    state.I = 0;
    state.J = 0;
    for (index = 0; index < LU4_RC4_SBOX_SIZE; ++index) {
        state.Sbox[index] = (UCHAR)index;
    }
    TransformType2Packet(packet, sizeof(packet), key);
    TransformRc4Packet(packet, sizeof(packet), &state);
    return RtlCompareMemory(packet, expectedFullPacket, sizeof(packet)) == sizeof(packet) &&
        RtlCompareMemory(key, expectedKey, sizeof(key)) == sizeof(key) &&
        state.I == 18 && state.J == 142 && IsValidRc4State(&state);
}

