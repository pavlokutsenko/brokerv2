using PriceCheck.Collection;
using PriceCheck.Contracts;
using PriceCheck.Launching;
using PriceCheck.Collector.Models;

if (args.Contains("--route-continuation-only"))
{
    await LocalRouteContinuationTests.Run();
    return;
}

if (args.Contains("--rotation-recovery-only"))
{
    await WindowCornerTests.Run();
    await LocalNativeFailureTests.Run();
    await CharacterRotationTests.Run();
    await ClientRecoveryTests.Run();
    Console.WriteLine("ModuleIsolation.Smoke rotation/native recovery: PASS");
    return;
}

if (args.Contains("--recovery-only"))
{
    await ClientRecoveryTests.Run();
    Console.WriteLine("ModuleIsolation.Smoke recovery: PASS");
    return;
}

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Reject(Func<Task> action)
{
    try { await action(); }
    catch (InvalidOperationException) { return; }
    throw new Exception("Expected rejection.");
}

var processes = new FakeProcesses();
var loginCount = 0;
var launcher = new LaunchModule(processes, (_, _, _) => { loginCount++; return Task.CompletedTask; }, processes.Identity);
var radar = new FakeRadar();
var reader = new CollectionModule(radar, session => processes.Identity(session.ProcessId) == session);
var profiles = Enumerable.Range(0, 2).Select(index => new CollectorProfile
{
    Name = index == 0 ? "Gamma" : "Black", LoginServerName = index == 0 ? "Gamma" : "Black",
    LoginServerId = index == 0 ? 1 : 10, AutoLoginEnabled = true,
    LoginName = "test-user", LoginPassword = "synthetic-password"
}).ToArray();
var runtimes = new List<ProfileRuntime>();
foreach (var profile in profiles)
{
    var session = await launcher.LaunchAsync(profile, null, _ => { }, CancellationToken.None);
    runtimes.Add(new() { Profile = profile, Session = session });
}
Check(loginCount == 2 && radar.Starts == 0, "Launching must not connect the reader.");
Check(processes.Proxies.Count == 2 && runtimes.Select(value => value.ProcessId).Distinct().Count() == 2, "Pair launch ownership.");

await reader.AttachAsync(runtimes[0], CancellationToken.None);
await reader.DetachAsync(runtimes[0]);
Check(processes.Proxies.Count == 2 && processes.Terminations == 0 && processes.Releases == 0, "Solo detach must leave games and proxies alive.");
foreach (var runtime in runtimes) await reader.AttachAsync(runtime, CancellationToken.None);
await reader.DetachAsync(runtimes[0]);
Check(runtimes[1].ReaderAttached && radar.Active.Count == 1 && processes.Proxies.Count == 2, "Paired detach affects only selected reader.");
Check(launcher.Owns(profiles[0].Id, runtimes[0].Session!), "Reader may not release launcher ownership.");

radar.FailStart = true;
await Reject(() => reader.AttachAsync(runtimes[0], CancellationToken.None));
Check(processes.Terminations == 0 && processes.Proxies.Count == 2 && runtimes[1].ReaderAttached, "Reader version failure must not stop either game.");
radar.FailStart = false;
await reader.AttachAsync(runtimes[0], CancellationToken.None);
var duplicate = new ProfileRuntime { Profile = new CollectorProfile(), Session = runtimes[0].Session };
await Reject(() => reader.AttachAsync(duplicate, CancellationToken.None));
Check(radar.Active.Count == 2, "Duplicate PID rejected without stopping the owner's reader.");

var oldSession = runtimes[0].Session!;
processes.ReusePid(oldSession.ProcessId);
await reader.RefreshAsync(runtimes[0]);
Check(!runtimes[0].ReaderAttached && runtimes[1].ReaderAttached, "PID reuse invalidates only its old reader.");
await Reject(() => reader.AttachAsync(runtimes[0], CancellationToken.None));
launcher.Stop(profiles[0].Id);
Check(processes.Terminations == 0 && processes.Releases == 1, "Stop must never terminate a replacement process with the same PID.");
await reader.DetachAsync(runtimes[1]);
launcher.Stop(profiles[1].Id);
Check(processes.Terminations == 1 && processes.Proxies.Count == 0, "Explicit launcher stop releases owned game and proxy.");

var failedLaunch = new LaunchModule(processes, (_, _, _) => throw new InvalidOperationException("Synthetic login failure"), processes.Identity);
var startsBeforeFailure = radar.Starts;
await Reject(async () => { await failedLaunch.LaunchAsync(new CollectorProfile { LoginServerId = 1, AutoLoginEnabled = true, LoginName = "test", LoginPassword = "test" }, null, _ => { }, CancellationToken.None); });
Check(processes.Terminations == 2 && radar.Starts == startsBeforeFailure, "Failed launch cleanup is entirely launcher-owned.");

// A delayed attach failure must release its reservation so another profile can retry.
var fresh = new ClientSession(9999, DateTimeOffset.UtcNow);
var controlledRadar = new FakeRadar { PendingStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
var controlledReader = new CollectionModule(controlledRadar, _ => true);
var first = new ProfileRuntime { Profile = new(), Session = fresh };
var second = new ProfileRuntime { Profile = new(), Session = fresh };
var attaching = controlledReader.AttachAsync(first, CancellationToken.None);
await Reject(() => controlledReader.AttachAsync(second, CancellationToken.None));
controlledRadar.PendingStart.SetException(new InvalidOperationException("Synthetic offset mismatch"));
await Reject(() => attaching);
controlledRadar.PendingStart = null;
await controlledReader.AttachAsync(second, CancellationToken.None);
await controlledReader.DetachAsync(second);

// Disconnect drains an in-flight job; it must not remove hooks under that job.
var pendingWorker = new PendingWorker();
var busyRadar = new FakeRadar { CurrentSnapshot = new RadarSnapshot { IsInsideCenterZone = true, CenterZoneConfigured = true } };
var uploadStarts = 0;
var busyReader = new CollectionModule(busyRadar, _ => true, pendingWorker, () => uploadStarts++,
    new PriceCheck.Collector.Services.ServerUploadOutbox(()=>uploadStarts++,Path.Combine(Path.GetTempPath(),"PriceCheck-cycle-tests",Guid.NewGuid().ToString("N"))),new FakeMarketTasks());
var busyRuntime = new ProfileRuntime { Profile = new CollectorProfile { Name="Synthetic-Busy-"+Guid.NewGuid().ToString("N"),ServerUrl="http://127.0.0.1:1",CenterZonesByCity = new() { ["Giran"] = new() { X = 0, Y = 0 } } }, Session = fresh };
using var busyFixture=new LocalTestFixture();busyFixture.Install(busyReader,busyRuntime.Profile);
await busyReader.AttachAsync(busyRuntime, CancellationToken.None);
Check(uploadStarts == 0, "Connecting read-only must not start price/upload workers.");
await busyReader.SetCollectionAsync(busyRuntime, true);
await busyReader.RefreshAsync(busyRuntime);
await busyReader.RefreshAsync(busyRuntime);
for(var i=0;i<100 && pendingWorker.Runs==0;i++) await Task.Delay(10);
Check(pendingWorker.Runs == 1 && uploadStarts >= 1, "Collection starts its worker and durable status uploads.");
var disconnecting = busyReader.DetachAsync(busyRuntime);
Check(!disconnecting.IsCompleted && !busyRuntime.IsCollectionEnabled && busyRuntime.ReaderAttached, "Disconnect waits for in-flight work with collection disabled.");
await Reject(() => busyReader.SetCollectionAsync(busyRuntime, true));
pendingWorker.Complete.SetResult();
await disconnecting;
Check(!busyRuntime.ReaderAttached && busyRadar.Active.Count == 0, "Reader cleanup follows worker completion.");
Check(processes.Terminations == 2, "Draining collection must never terminate a game.");

// Active architecture tests use private synthetic markets, offline transport and
// a temp SQLite store. Historical lease/cohort fixtures remain in separate files.
await LocalCoordinatorTests.Run();
await LocalRouteContinuationTests.Run();
await LocalBrokerWarningTests.Run();
await LocalNativeFailureTests.Run();
await LocalCenterArrivalTests.Run();
await LocalApproachErrorTests.Run();
RadarFrameSharingTests.Run();
await CharacterRotationTests.Run();
await WindowCornerTests.Run();
await ClientRecoveryTests.Run();
await BrokerIdentityTests.Run();
var launchRefs=typeof(LaunchModule).Assembly.GetReferencedAssemblies().Select(v=>v.Name).ToArray();
var readerRefs=typeof(CollectionModule).Assembly.GetReferencedAssemblies().Select(v=>v.Name).ToArray();
Check(!launchRefs.Contains("PriceCheck.Collection")&&!readerRefs.Contains("PriceCheck.Launching"),"Module ownership references stay independent.");
Check(!launchRefs.Contains("PriceCheck.Collector")&&!readerRefs.Contains("PriceCheck.Collector"),"Modules do not reference the UI.");
Console.WriteLine("MODULE_ISOLATION_OK launch_only solo_detach paired_detach reader_failure duplicate_pid pid_reuse attach_race drain_worker local_four_markets warning_continuation new_client_fresh_history no_server_claims assembly_boundaries");
