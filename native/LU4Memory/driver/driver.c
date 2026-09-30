#include <ntifs.h>
#include <wdmsec.h>
#include <aux_klib.h>
#include <initguid.h>
#define NDIS630 1
#pragma warning(push)
#pragma warning(disable:4201)
#include <ndis.h>
#include <fwpsk.h>
#include <fwpmk.h>
#pragma warning(pop)

#include "../include/lu4_protocol.h"
#include "../include/lu4_target_packet.h"

DEFINE_GUID(GUID_DEVCLASS_LU4_MEMORY,
    0x98bc6514, 0x4a71, 0x4f68, 0xa7, 0x8d, 0xe3, 0x31, 0x69, 0xe7, 0x52, 0xb1);

C_ASSERT(sizeof(LU4_PROCESS_IDENTITY_REQUEST) == 1048);
C_ASSERT(sizeof(LU4_BASE_REQUEST) == 16);
C_ASSERT(LU4_COPY_HEADER_SIZE == 24);
C_ASSERT(sizeof(LU4_TARGET_COMMAND) == 48);
C_ASSERT(sizeof(LU4_KERNEL_MODULE_INFO) == 24);
C_ASSERT(LU4_MODULE_READ_HEADER_SIZE == 24);
C_ASSERT(sizeof(LU4_PROBE_ACTIVE64_MAC_REQUEST) == 24);
C_ASSERT(sizeof(LU4_PACKET_CRYPTO_REQUEST) == 48);
C_ASSERT(sizeof(LU4_RC4_STATE) == LU4_RC4_STATE_SIZE);
C_ASSERT(sizeof(LU4_VIRTUAL_MEMORY_REQUEST) == 40);
C_ASSERT(sizeof(LU4_PROXY_REDIRECT_REQUEST) == 28);
C_ASSERT(sizeof(LU4_PROXY_GUARD_REQUEST) == 32);
C_ASSERT(sizeof(LU4_PROXY_GUARD_DETAILS) == 48);
C_ASSERT(sizeof(LU4_PROCESS_STATUS_REQUEST) == 24);
C_ASSERT(sizeof(LU4_PROXY_REDIRECT_CONTEXT) == 16);
C_ASSERT(sizeof(LU4_WORKING_SET_REQUEST) == 48);

#ifdef LU4_PROBE_BUILD
static const UNICODE_STRING g_DeviceName = RTL_CONSTANT_STRING(L"\\Device\\LU4Probe");
static const UNICODE_STRING g_SymbolicLink = RTL_CONSTANT_STRING(L"\\DosDevices\\LU4Probe");
#else
static const UNICODE_STRING g_DeviceName = RTL_CONSTANT_STRING(L"\\Device\\LU4Memory");
static const UNICODE_STRING g_SymbolicLink = RTL_CONSTANT_STRING(L"\\DosDevices\\LU4Memory");
#endif
static FAST_MUTEX g_TargetMutex;
static FAST_MUTEX g_CryptoMutex;
static LU4_TARGET_COMMAND g_TargetCommand;
static const CHAR g_Active64Name[] = "active64.sys";

NTSYSAPI PVOID NTAPI PsGetProcessSectionBaseAddress(_In_ PEPROCESS Process);
NTSYSAPI NTSTATUS NTAPI MmCopyVirtualMemory(
    _In_ PEPROCESS SourceProcess,
    _In_ PVOID SourceAddress,
    _In_ PEPROCESS TargetProcess,
    _Out_ PVOID TargetAddress,
    _In_ SIZE_T BufferSize,
    _In_ KPROCESSOR_MODE PreviousMode,
    _Out_ PSIZE_T ReturnSize);
NTSYSAPI NTSTATUS NTAPI ZwProtectVirtualMemory(
    _In_ HANDLE ProcessHandle,
    _Inout_ PVOID* BaseAddress,
    _Inout_ PSIZE_T RegionSize,
    _In_ ULONG NewProtect,
    _Out_ PULONG OldProtect);
NTSYSAPI NTSTATUS NTAPI PsSuspendProcess(_In_ PEPROCESS Process);
NTSYSAPI NTSTATUS NTAPI PsResumeProcess(_In_ PEPROCESS Process);

#include "common.inc"
#include "process_memory.inc"
#include "target_mailbox.inc"
#include "active64.inc"
#include "packet_transform.inc"
#include "packet_crypto.inc"
#include "virtual_memory.inc"
static VOID IdentityProcessNotify(PEPROCESS process, HANDLE processId, PPS_CREATE_NOTIFY_INFO createInfo);
#include "proxy_redirect.inc"
#include "identity_registry.inc"
#include "working_set.inc"
#include "dispatch.inc"
