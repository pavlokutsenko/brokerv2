# Per-client Windows resource jobs, 2026-09-30

The Collector and Launcher now apply optional limits from each launch template
to that template's newly launched client. A suspended launcher is assigned to
a Windows Job Object, and the game child inherits membership. The memory
setting applies a hard maximum to the resident working set. It does not cap
private committed allocations, reduce game-owned allocations, or guarantee
that a 7 GiB machine can run several clients without paging. The CPU setting
is an independent hard CPU-rate cap, applied only after the agent reports
readiness; applying it during startup caused a 60-second readiness timeout.

New templates default to the memory setting enabled at 3072 MiB (3 GiB) and
the CPU setting disabled. Existing templates retain their saved selections.
The two active Gamma proxy templates were set to 3072 MiB and CPU disabled in
the local user configuration. User settings remain under LocalAppData and are
not included in portable packages. The third existing template was left alone.

## Runtime evidence

- Two simultaneous Gamma clients with the 3 GiB working-set cap were observed
  around 2.99 GiB resident each, while private allocations remained around
  8.5 GiB each. During that run, the broker completed and exact reads and
  account turns continued; the observed recent 400 events had no errors.
- A separate 20% CPU-cap run with two clients measured roughly 15-18% of the
  machine per game. Broker and exact reads continued, but navigation reported
  intermittent `world/pawn changed` errors. A bounded re-read guard was added
  and unit-tested; that guard has not been validated in another live run.
- A 10% CPU cap applied during suspended startup prevented agent readiness.
  CPU application now occurs after readiness. Because of the observed route
  sensitivity, the default is CPU unlimited.

The working-set Job Object smoke test uses a synthetic process and verifies
3 GiB readback and job membership. Module isolation, launcher and collector UI
smokes, and the route-guard unit tests provide additional coverage. These are
not proof of performance on a different computer; run a live two-client test
there before increasing the window count.

The experimental working-set driver source builds, but the installed and
packaged driver is the prior binary and does not implement that IOCTL. No
unsigned driver was loaded or replaced for this feature. The portable package
still contains the existing driver loader, whose operation may depend on the
destination Windows security configuration. Resource jobs do not require the
experimental driver IOCTL.
