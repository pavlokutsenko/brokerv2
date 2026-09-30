/* Runs the actual command handler against isolated Windows-service substitutes.
 * No driver is loaded and no process handle or memory policy is changed. */
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef unsigned long ULONG, ACCESS_MASK, *PULONG;
typedef long NTSTATUS;
typedef unsigned long long ULONGLONG;
typedef uintptr_t ULONG_PTR, *PULONG_PTR;
typedef size_t SIZE_T;
typedef void *PVOID, *HANDLE, *PEPROCESS;
typedef int PROCESSINFOCLASS;
#define NTSYSAPI
#define NTAPI
#define NT_SUCCESS(s) ((s) >= 0)
#define STATUS_SUCCESS 0L
#define STATUS_PENDING 0x103L
#define STATUS_ACCESS_DENIED ((NTSTATUS)0xc0000022L)
#define STATUS_INVALID_CID ((NTSTATUS)0xc000000bL)
#define STATUS_INVALID_PARAMETER ((NTSTATUS)0xc000000dL)
#define STATUS_INVALID_DEVICE_STATE ((NTSTATUS)0xc0000184L)
#define STATUS_BUFFER_TOO_SMALL ((NTSTATUS)0xc0000023L)
#define PAGE_SIZE 4096u
#define PASSIVE_LEVEL 0
#define KernelMode 0
#define OBJ_KERNEL_HANDLE 0x200u
#define OBJ_FORCE_ACCESS_CHECK 0x400u
#define ProcessQuotaLimits 1
#define ObjectBasicInformation 0
#define LU4_PROTOCOL_VERSION 3
#define LU4_WORKING_SET_APPLY 1u
#define LU4_WORKING_SET_RESTORE 2u
#define HandleToULong(h) ((ULONG)(uintptr_t)(h))
#define RtlZeroMemory(p, s) memset(p, 0, s)
#define RtlCopyMemory(d, s, n) memcpy(d, s, n)
#define QUOTA_FIELDS SIZE_T PagedPoolLimit, NonPagedPoolLimit, MinimumWorkingSetSize, MaximumWorkingSetSize, PagefileLimit; long long TimeLimit
typedef struct { QUOTA_FIELDS; } QUOTA_LIMITS;
typedef struct { QUOTA_FIELDS; SIZE_T WorkingSetLimit, Reserved2, Reserved3, Reserved4; ULONG Flags, CpuRate; } QUOTA_LIMITS_EX;
typedef struct { ACCESS_MASK GrantedAccess; } PUBLIC_OBJECT_BASIC_INFORMATION;
typedef struct { ULONG ProcessId, ListenerPort, HostProcessId; } LU4_PROXY_ROUTE;
typedef struct { ULONG Version, ProcessId; ULONGLONG CreationTime, MinimumBytes, MaximumBytes; ULONG Flags, Operation; NTSTATUS Status; ULONG Reserved; } LU4_WORKING_SET_REQUEST, *PLU4_WORKING_SET_REQUEST;

static QUOTA_LIMITS testQuotas;
static QUOTA_LIMITS_EX submitted;
static ULONG grantedAccess, owner;
static int irql, opens, queries, sets, closes, dereferences, failReadback;
static ULONG openAttributes;
static NTSTATUS openStatus, setStatus;
static ULONGLONG birth;
static void *processType;
static void **PsProcessType = &processType;
static int KeGetCurrentIrql(void) { return irql; }
static HANDLE PsGetCurrentProcessId(void) { return (HANDLE)(uintptr_t)1; }
static LU4_PROXY_ROUTE ProxyRoute(ULONG pid) { LU4_PROXY_ROUTE route = { pid, 1234, owner }; return route; }
static NTSTATUS LookupTargetProcess(ULONG pid, PEPROCESS *process) { *process = (PEPROCESS)(uintptr_t)pid; return STATUS_SUCCESS; }
static long long PsGetProcessCreateTimeQuadPart(PEPROCESS p) { (void)p; return (long long)birth; }
static NTSTATUS PsGetProcessExitStatus(PEPROCESS p) { (void)p; return STATUS_PENDING; }
static void ObDereferenceObject(PEPROCESS p) { (void)p; dereferences++; }
static NTSTATUS ObOpenObjectByPointer(PEPROCESS p, ULONG attributes, PVOID state, ACCESS_MASK access, PVOID type, int mode, HANDLE *handle)
{
    (void)p; (void)state; (void)access; (void)type; (void)mode;
    opens++; openAttributes = attributes; *handle = (HANDLE)(uintptr_t)123; return openStatus;
}
static NTSTATUS ZwQueryObject(HANDLE h, int cls, PVOID info, ULONG size, PULONG length)
{
    (void)h; (void)cls; (void)size; (void)length;
    ((PUBLIC_OBJECT_BASIC_INFORMATION *)info)->GrantedAccess = grantedAccess; return STATUS_SUCCESS;
}
static void ZwClose(HANDLE h) { (void)h; closes++; }
NTSTATUS ZwQueryInformationProcess(HANDLE h, PROCESSINFOCLASS cls, PVOID info, ULONG size, PULONG length)
{
    (void)h; (void)cls; (void)size; (void)length;
    queries++;
    if (failReadback && queries == 2) return STATUS_ACCESS_DENIED;
    memcpy(info, &testQuotas, sizeof(testQuotas)); return STATUS_SUCCESS;
}
NTSTATUS ZwSetInformationProcess(HANDLE h, PROCESSINFOCLASS cls, PVOID info, ULONG size)
{
    (void)h; (void)cls;
    if (size != sizeof(submitted)) abort();
    sets++; memcpy(&submitted, info, sizeof(submitted));
    if (NT_SUCCESS(setStatus)) memcpy(&testQuotas, info, sizeof(testQuotas));
    return setStatus;
}
#include "../../native/LU4Memory/driver/working_set.inc"

static void Check(int condition, const char *message) { if (!condition) { puts(message); exit(1); } }
static LU4_WORKING_SET_REQUEST Reset(void)
{
    LU4_WORKING_SET_REQUEST request = { 3, 2, 500, 204800, 256ull * 1024 * 1024, 6, LU4_WORKING_SET_APPLY, 0, 0 };
    memset(&testQuotas, 0, sizeof(testQuotas)); memset(&submitted, 0, sizeof(submitted));
    testQuotas.MinimumWorkingSetSize = 204800; testQuotas.MaximumWorkingSetSize = 1413120;
    testQuotas.PagedPoolLimit = 12345; testQuotas.NonPagedPoolLimit = 23456;
    testQuotas.PagefileLimit = 8ull * 1024 * 1024 * 1024; testQuotas.TimeLimit = -1;
    grantedAccess = 0x500; owner = 1; birth = 500;
    irql = opens = queries = sets = closes = dereferences = failReadback = 0;
    openAttributes = 0; openStatus = setStatus = STATUS_SUCCESS;
    return request;
}
static NTSTATUS Invoke(LU4_WORKING_SET_REQUEST *request)
{
    ULONG_PTR bytes = 0;
    NTSTATUS status = HandleWorkingSet(request, sizeof(*request), sizeof(*request), &bytes);
    Check(!NT_SUCCESS(status) || bytes == sizeof(*request), "Incorrect structured response length.");
    return status;
}
int main(void)
{
    LU4_WORKING_SET_REQUEST request = Reset();
    Check(sizeof(request) == 48 && sizeof(testQuotas) == 48 && sizeof(submitted) == 88, "Quota/protocol layout changed.");
    Check(Invoke(&request) == STATUS_SUCCESS && request.Status == STATUS_SUCCESS && sets == 1, "Valid budget failed.");
    Check(testQuotas.MaximumWorkingSetSize == 256ull * 1024 * 1024 && submitted.Flags == 6, "Resident maximum was not set.");
    Check(submitted.PagefileLimit == 8ull * 1024 * 1024 * 1024 && submitted.PagedPoolLimit == 12345 &&
        submitted.NonPagedPoolLimit == 23456 && submitted.TimeLimit == -1 && submitted.CpuRate == 0,
        "Command changed commit, pool, time or CPU quotas.");
    Check((openAttributes & (OBJ_KERNEL_HANDLE | OBJ_FORCE_ACCESS_CHECK)) == (OBJ_KERNEL_HANDLE | OBJ_FORCE_ACCESS_CHECK) &&
        closes == 1 && dereferences == 1, "Handle security/lifecycle changed.");
    request = Reset(); grantedAccess = 0x400;
    Check(Invoke(&request) == STATUS_SUCCESS && request.Status == STATUS_ACCESS_DENIED && sets == 0 && closes == 1,
        "A handle with stripped SET_QUOTA rights reached the quota setter.");
    request = Reset(); owner = 99;
    Check(Invoke(&request) == STATUS_ACCESS_DENIED && opens == 0, "Foreign owner accepted.");
    request = Reset(); birth++;
    Check(Invoke(&request) == STATUS_INVALID_CID && opens == 0 && dereferences == 1, "Recycled PID accepted.");
    request = Reset(); request.MaximumBytes = 255ull * 1024 * 1024;
    Check(Invoke(&request) == STATUS_INVALID_PARAMETER && opens == 0, "Too-small budget accepted.");
    request = Reset(); request.Flags = 12;
    Check(Invoke(&request) == STATUS_INVALID_PARAMETER && opens == 0, "Conflicting flags accepted.");
    request = Reset(); request.Operation = LU4_WORKING_SET_RESTORE; request.Flags = 10; request.MaximumBytes = 1413120;
    Check(Invoke(&request) == STATUS_SUCCESS && request.Status == STATUS_SUCCESS && submitted.Flags == 10,
        "Original soft bounds could not be restored.");
    request = Reset(); irql = 1;
    Check(Invoke(&request) == STATUS_INVALID_DEVICE_STATE && opens == 0, "Wrong IRQL accepted.");
    request = Reset(); openStatus = STATUS_ACCESS_DENIED;
    Check(Invoke(&request) == STATUS_SUCCESS && request.Status == STATUS_ACCESS_DENIED && sets == 0 && dereferences == 1,
        "Open failure lost or quota changed despite rejection.");
    request = Reset(); failReadback = 1;
    Check(Invoke(&request) == STATUS_SUCCESS && request.Status == STATUS_ACCESS_DENIED && sets == 1 && closes == 1,
        "Post-set readback failure was hidden from the caller.");
    puts("WORKING_SET_NATIVE_OK ownership, PID birth, access stripping, quota isolation, rollback flags, errors, IRQL and handle cleanup");
    return 0;
}
