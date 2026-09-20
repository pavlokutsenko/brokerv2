#pragma once

#ifdef _KERNEL_MODE
#include <ntddk.h>
#else
#include <stdint.h>
#endif

#pragma pack(push, 1)
typedef struct _LU4_TARGET_PACKET {
#ifdef _KERNEL_MODE
    UCHAR Opcode;
    LONG ObjectId;
    LONG X;
    LONG Y;
    LONG Z;
    UCHAR ForceAttack;
#else
    uint8_t Opcode;
    int32_t ObjectId;
    int32_t X;
    int32_t Y;
    int32_t Z;
    uint8_t ForceAttack;
#endif
} LU4_TARGET_PACKET;
#pragma pack(pop)

#if defined(_KERNEL_MODE)
C_ASSERT(sizeof(LU4_TARGET_PACKET) == 18);
#elif defined(__cplusplus)
static_assert(sizeof(LU4_TARGET_PACKET) == 18, "LU4 target packet must be 18 bytes");
#else
typedef char LU4_TARGET_PACKET_must_be_18_bytes[
    sizeof(LU4_TARGET_PACKET) == 18 ? 1 : -1];
#endif

#define LU4_TARGET_OPCODE 0x0Fu
