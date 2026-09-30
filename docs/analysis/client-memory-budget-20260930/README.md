# LU4 physical memory budget experiment, 2026-09-30

Later runtime results and the shipped Job Object implementation are recorded in
`docs/analysis/client-resource-job-20260930/README.md`. The candidate driver
described below was not installed; the warnings below describe this earlier
driver-only stage, not the later Job Object implementation.

Outcome: a 5120 MiB resident working-set budget was prepared but was not applied to LU4. Do not report this as an enabled memory limit or a successful RAM optimization.

The experiment used documented Windows APIs. It did not limit committed allocations, release game-owned allocations, alter trader distances, modify client/anti-cheat code, or add a kernel bypass. No automatic memory cleaner was installed.

## Evidence

- Existing collector: PID 12100, Gamma, two accounts. Initial test clients: Wics PID 12584 and Uce PID 16124.
- `EmptyWorkingSet` and its documented equivalent `SetProcessWorkingSetSize(-1,-1)` both returned Win32 error 5 on Wics. The same `EmptyWorkingSet` call succeeded on the current experiment PowerShell process.
- `GetProcessWorkingSetSizeEx` succeeded on Uce: minimum 204800 bytes, maximum 1413120 bytes, flags 10. These default soft bounds are not the current memory consumption.
- `SetProcessWorkingSetSizeEx` requesting the original minimum, maximum 5368709120 bytes and flags 6 (hard maximum, soft minimum) returned error 5. No committed-memory cap was requested.
- An independent Job Object with only `JOB_OBJECT_LIMIT_WORKINGSET`, minimum 204800 and maximum 5368709120 bytes was configured successfully. Assignment to the restarted Wics PID 7320 failed with error 5. No CPU, process count, committed-memory or kill-on-close limits were configured, and no process was assigned to that job.
- A read-only handle-access audit on Wics PID 7320 and Uce PID 11464 requested `0x1100` (`PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SET_QUOTA`) but found actual granted access `0x1000`: `PROCESS_SET_QUOTA` was absent. The control PowerShell handle retained `0x1100`. The component responsible for reducing access was not independently identified.

At the end of the initial Windows-API experiment the driver had no working-set operation. The following implementation phase adds one to source; the installed driver has not been changed.

## Collection and interpretation

Before the trimming attempt, the initial broker had 1814/1814 responses but four unbound identities covering six rows; the center radar reported complete with 1764 native identities. This warning predates every memory modification attempt and cannot be attributed to a memory limit.

At the post-trim checkpoint there were 19 complete shop captures, 76 price rows, all 1600 broker deliveries accepted, and an empty outbox. Later, ordinary connection loss triggered automatic client restarts: Wics at 12:18 Kyiv time and Uce subsequently. No memory policy had been applied. PID birth checks prevented targeting exited/reused processes.

The observation that another laptop uses about 3 GiB resident RAM supports investigating residency under memory pressure. It does not establish equivalent committed allocation sizes, scene population, cache state, or radar completeness across computers.

## Artifacts and reproduction

Local scripts and JSON evidence are in `C:\broker\workspace\memory-trim-20260930`:

- `limit-working-set.ps1`: one-target physical working-set budget prototype; saves original limits and the actual API result.
- `job-working-set.ps1`: bounded alternate experiment; removes its policy if assignment succeeds and never sets kill-on-close.
- `limit-result.json`, `job-limit-result.json`, `handle-rights.json`, `self-control.json`: results.
- `measurements.json`, `audit-before.json`, `audit-after.json`: measurements and collection checkpoints.

The command used for the direct budget was `./workspace/memory-trim-20260930/limit-working-set.ps1 -TargetProcessId 16124 -MaximumMiB 5120`. The historical PID must not be reused. These are diagnostic scripts, not a production feature or a startup policy.

## Driver implementation phase

The source now includes `native/LU4Memory/driver/working_set.inc` and managed
`Lu4Device.WorkingSet.cs`, using protocol-3 function `0x813`. The command changes
Windows working-set bounds through the memory manager. It does not cap committed
allocations or touch game data. It requires the caller's process ownership route,
the target's creation time and actual query/set-quota handle rights. Forced access
checks remain enabled. Explicit granted-rights inspection prevents a successful
open with stripped rights from reaching the quota setter.

Templates have an optional physical-memory limit, with an initial value of
5120 MiB. Existing settings remain disabled. Application happens independently
per newly launched session before login/reader attachment, including replacement
clients. Original limits are saved and verified on restoration. Failed application
is reported and cleans up that new launch; another profile is unaffected. Logging
uses `%LocalAppData%\PriceCheckCollector\logs\memory-budget.log`.

The x64 WDK driver build completed with zero warnings and errors. Launcher and
Collector were built through the root `build.ps1`, using isolated outputs under
`workspace/memory-budget-build`. The signed/verified production driver was not
replaced. The candidate is unsigned; existing packaged applications still carry
the verified older driver, which does not implement this command.

Passed checks:

- `WorkingSet.NativeSmoke`: executes the actual command handler with isolated
  Windows-service substitutes. Checks ownership, PID reuse, stripped rights,
  unchanged commit/pool/time/CPU quotas, restore flags, IRQL, structured failure
  after setting and handle/reference cleanup. This is not a loaded-driver test.
- `WorkingSet.Smoke` without `--driver`: real Windows query with limited access
  and rejection of an incorrect birth time. No memory policy was changed.
- `ModuleIsolation.Smoke --memory-budget-only`: independent budgets for two
  synthetic sessions, default-off migration, serialization, ordering before
  login, repeated-launch behavior and failure isolation.
- `CollectorUi.Smoke`: toggle, numeric binding, template cloning/save, rendering
  and preservation of production settings. Screenshot:
  `workspace/memory-trim-20260930/ui/templates.png`.

The current loader contains KDU/DSE bypass steps. It was not invoked for this
candidate. Runtime verification requires a legitimately signed driver or a
separate Windows driver-test environment. Do not enable the template option on
the current packaged driver or mark this candidate verified. Signing/loading,
loaded-driver application/rollback and live LU4 radar/broker completeness remain
unverified. Access restrictions may still reject the command even after legitimate
installation; the implementation reports that restriction instead of bypassing it.

After legitimate candidate installation in a test environment, the synthetic
loaded-driver check is:

```powershell
& C:/tools/dev/.tools/dotnet/dotnet.exe run --project tests/WorkingSet.Smoke/WorkingSet.Smoke.csproj -c Release -- --driver
```

It targets itself, verifies a 256 MiB hard resident maximum, checks allocation
contents and restores the original bounds in `finally`. It never targets LU4.
Only after that passes should a 5120 MiB live-client test compare working set,
private committed bytes, page-fault pressure, route latency and complete radar/
broker results against an equivalent baseline. No claim of RAM savings or trader
visibility under the cap is supported yet.

## Sources

- [Working sets](https://learn.microsoft.com/en-us/windows/win32/memory/working-set): residency and page faults.
- [SetProcessWorkingSetSizeEx](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-setprocessworkingsetsizeex): required rights and hard/soft residency bounds.
- [GetProcessWorkingSetSizeEx](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-getprocessworkingsetsizeex): readback.
- [AssignProcessToJobObject](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-assignprocesstojobobject): required assignment rights.
- [Job limit semantics](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_limit_information): working-set budgets versus allocation-failure limits.
