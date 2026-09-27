using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using PriceCheck.Collection;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;
using PriceCheck.Windows;
using PriceCheck.Launching;
using PriceCheck.Contracts;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length is <4 or >5 || args.Length==5 && args[4]!="--broker-only")
            throw new ArgumentException("pid|launch-early profile-guid seconds output-prefix [--broker-only]; live reader must be disconnected in the desktop");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try { await RunAsync(args); }
            catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        Dispatcher.Run();
    }

    private static async Task RunAsync(string[] args)
    {
        var launchEarly=args[0]=="launch-early";
        var brokerOnly=args.Length==5;
        ClientSession? session = launchEarly ? null : ClientProcessIdentity.Read(int.Parse(args[0])) ?? throw new InvalidOperationException("Client not running");
        var seconds = Math.Clamp(int.Parse(args[2]), 30, 1200);
        var prefix = Path.GetFullPath(args[3]);
        Directory.CreateDirectory(Path.GetDirectoryName(prefix)!);
        var profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PriceCheckCollector", "profiles.json");
        var json = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var saved = JsonSerializer.Deserialize<CollectorProfile[]>(await File.ReadAllTextAsync(profilePath), json)!
            .Single(p => p.Id == Guid.Parse(args[1]));
        if (!launchEarly && (saved.LastProcessId != session!.ProcessId || saved.LastProcessStartUtc != session.StartedAtUtc))
            throw new InvalidOperationException("Profile process identity mismatch");
        // Only market settings enter the reader; no launch, identity, or credentials.
        var profile = new CollectorProfile { Id = saved.Id, Name = saved.Name, City = saved.City,
            ServerUrl = saved.ServerUrl, BrokerIntervalMinutes = saved.BrokerIntervalMinutes,
            CenterZonesByCity = saved.CenterZonesByCity };
        var outboxPath = Path.Combine(CycleQueue.Root, "live-validation", Path.GetFileName(prefix), "outbox");
        var lastUpload = DateTimeOffset.MinValue;
        void StartUpload()
        {
            if (DateTimeOffset.UtcNow - lastUpload < TimeSpan.FromSeconds(3)) return;
            Process.Start(new ProcessStartInfo { FileName = @"C:\broker\release\PriceCheckCollector\PriceCheck.Collector.exe",
                UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--upload-worker", outboxPath } });
            lastUpload = DateTimeOffset.UtcNow;
        }
        var reader = new CollectionModule(startUploadWorker: StartUpload, uploadOutbox: new(StartUpload, outboxPath));
        reader.Message += message => { Console.WriteLine(message); File.AppendAllText(prefix + ".events.log", message + "\n"); };
        var runtime = new ProfileRuntime { Profile = profile, Session = session };
        var launcher=new LaunchModule();
        var stop = false;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop = true; };
        try
        {
            if(launchEarly)
            {
                if(PriceCheck.Windows.ClientProcessIdentity.Read(saved.LastProcessId??-1) is not null)
                    throw new InvalidOperationException("Close the previous owned client before the early-reader test.");
                saved.LoginPassword=System.Text.Encoding.UTF8.GetString(System.Security.Cryptography.ProtectedData.Unprotect(
                    Convert.FromBase64String(saved.LoginPasswordProtected!),null,System.Security.Cryptography.DataProtectionScope.CurrentUser));
                session=await launcher.LaunchAsync(saved,null,Console.WriteLine,CancellationToken.None,async (newSession,token)=>
                {
                    runtime.Session=newSession;
                    await reader.AttachAsync(runtime,token);
                    Console.WriteLine($"EARLY_READER_ATTACHED {newSession.ProcessId} before login");
                });
                saved.LoginPassword="";
            }
            else await reader.AttachAsync(runtime, CancellationToken.None);
            if(launchEarly)
            {
                var worldDeadline=DateTimeOffset.UtcNow.AddMinutes(2);
                while(runtime.Radar is not {PositionedActors:>0,LivePlayerPositionAvailable:true})
                {
                    if(DateTimeOffset.UtcNow>=worldDeadline) throw new TimeoutException("No world character data received after login.");
                    await reader.RefreshAsync(runtime);
                    await Task.Delay(500);
                }
                Console.WriteLine($"WORLD_PACKETS_READY {runtime.Radar.PositionedActors} characters");
            }
            await reader.SetCollectionAsync(runtime, true);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(seconds);
            var nextReport = DateTimeOffset.MinValue;
            while (!stop && !File.Exists(prefix + ".stop") && runtime.IsCollectionEnabled && DateTimeOffset.UtcNow < deadline)
            {
                if(brokerOnly && runtime.Cycle.Cycles>0) break;
                await reader.RefreshAsync(runtime);
                if (DateTimeOffset.UtcNow >= nextReport)
                {
                    var report = JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, pid = session!.ProcessId,
                        runtime.Cycle, runtime.Status, runtime.UploadStatus, x = runtime.Radar?.PlayerX, y = runtime.Radar?.PlayerY,
                        broker = runtime.Broker, radar=runtime.Radar, outboxPath });
                    File.WriteAllText(prefix + ".status.json", report);
                    File.AppendAllText(prefix + ".jsonl", report + "\n");
                    Console.WriteLine($"{DateTime.Now:T} {runtime.Cycle.Phase}: {runtime.Cycle.Detail}");
                    nextReport = DateTimeOffset.UtcNow.AddSeconds(5);
                }
                await Task.Delay(500);
            }
        }
        finally { await reader.DetachAsync(runtime); StartUpload(); if(launchEarly) launcher.Stop(saved.Id); }
        if(brokerOnly && runtime.Cycle.Cycles==0) throw new InvalidOperationException("Broker-only validation did not complete a full valid broker epoch.");
        Console.WriteLine(launchEarly ? "LIVE_CYCLE_STOPPED reader detached; owned game closed" : "LIVE_CYCLE_STOPPED reader detached; game and launcher preserved");
    }
}
