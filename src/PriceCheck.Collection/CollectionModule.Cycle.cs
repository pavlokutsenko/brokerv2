using System.Text.Json;
using System.Net.Http;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    private readonly Dictionary<Guid, CycleRun> _cycles = [];
    private sealed class CycleRun
    {
        public CycleBindings Bindings { get; } = new();
        public CycleRadarPool RadarPool { get; init; } = new();
        public bool CollectingBrokerRadar { get; set; }
        public string WorkerId { get; init; } = "";
        public string? BrokerBatch { get; set; }
        public string ClaimRequest { get; set; } = Guid.NewGuid().ToString();
        public string ResumePhase { get; set; } = "Return to center";
        public bool ContinueAfterClientChange { get; set; }
        public BrokerSnapshot? PreviousBroker { get; set; }
        public required string Folder { get; init; }
        public required string Scope { get; init; }
        public required CollectorProfile UploadProfile { get; init; }
        public required LocalCycleStore Store { get; init; }
        public DateTimeOffset CenterEnteredAt { get; set; } = DateTimeOffset.MaxValue;
        public bool WasInsideCenter { get; set; }
        public string Phase { get; set; } = "Return to center";
        public DateTimeOffset Next { get; set; }
        public MarketZone? Center { get; set; }
        public double[]? PreviousDestination { get; set; }
        public string? ProgressFile { get; set; }
        public string? RadarFile { get; set; }
        public Task? BackgroundPlan { get; set; }
        public string PlanGeneration { get; set; } = Guid.NewGuid().ToString("N");
        public string NextSectionPlanFile => Path.Combine(Folder,$"next-section-{PlanGeneration}.plan.json");
        public Task? RouteSession { get; set; }
        public string? RouteCommandFile { get; set; }
        public string? PriceStreamPrefix { get; set; }
        public AppendOnlyJsonLineReader? PriceEventReader { get; set; }
        public System.Collections.Concurrent.ConcurrentDictionary<string,byte> PriceSpooled { get; } = new();
        public DateTimeOffset NextRadarWrite { get; set; }
        public HashSet<string> BrokerKeys { get; } = [];
        public DateTimeOffset? WorkerStartedAt { get; set; }
        public DateTimeOffset NextStateRefresh { get; set; }
        public Task? StateRefresh { get; set; }
        public int ReturnFailures { get; set; }
        public int BrokerFailures { get; set; }
        public HashSet<string> RemainingVisitKeys { get; } = [];
        public Dictionary<string,int> ApproachReplans { get; } = [];
        public HashSet<string> UnresolvedApproaches { get; } = [];
        public string StopFile => Path.Combine(Folder, "STOP");
    }

    private void StartCycle(ProfileRuntime runtime)
    {
        var folder = Path.Combine(CycleQueue.Root, runtime.Profile.Id.ToString("N"), "runs");
        Directory.CreateDirectory(folder);
        var store=GetLocalStore(runtime);
        store.ResetPass();
        _cycles[runtime.Profile.Id] = new CycleRun { Folder = folder, Store=store,
            RadarPool=CycleRadarPool.ForCity(runtime.Profile.City),
            WorkerId=$"collector:{runtime.Profile.Id:N}:{Guid.NewGuid():N}",
            Scope = $"{runtime.Profile.Name}:{runtime.Profile.City}", Center = GetCenterZone(runtime.Profile),
            UploadProfile=new() {Id=runtime.Profile.Id,Name=runtime.Profile.Name,City="Giran",ServerUrl=runtime.Profile.ServerUrl} };
        File.Delete(_cycles[runtime.Profile.Id].StopFile);
        runtime.Cycle = new() { Phase = "Return to center", Detail = "New client pass; local history and pending uploads retained." };
    }

    private void RequestCycleStop(ProfileRuntime runtime)
    {
        if (_cycles.TryGetValue(runtime.Profile.Id, out var cycle)) File.WriteAllText(cycle.StopFile, "stop");
    }

    private void TickCycle(ProfileRuntime runtime, RadarSnapshot radar)
    {
        if (!_cycles.TryGetValue(runtime.Profile.Id, out var cycle)) return;
        // An armed native command belongs to client recovery. Never send another
        // route into its stop marker or turn preserved collection intent into Stop.
        if (runtime.ClientFault is not null) return;
        if(!radar.WorldCharacterDataAvailable)
        {
            runtime.Status="Waiting for world character data after login";
            runtime.Cycle=runtime.Cycle with {Detail=runtime.Status};
            return;
        }
        if(cycle.Phase=="Resume route" && !radar.LivePlayerPositionAvailable)
        {
            runtime.Status="Waiting for the new character's live position";
            runtime.Cycle=runtime.Cycle with {Detail=runtime.Status};
            return;
        }
        if (cycle.Scope != $"{runtime.Profile.Name}:{runtime.Profile.City}" || GetCenterZone(runtime.Profile) != cycle.Center ||
            cycle.UploadProfile.ServerUrl!=runtime.Profile.ServerUrl)
        {
            RequestCycleStop(runtime);
            runtime.IsCollectionEnabled = runtime.Profile.CollectionEnabled = false;
            cycle.Phase="Stopped";
            runtime.Cycle = runtime.Cycle with { Phase = "Stopped", Detail = "Settings changed. Restart collection to use the new center or market." };
            return;
        }
        cycle.Bindings.Observe(radar);
        cycle.RadarPool.Observe(radar,cycle.BrokerKeys,cycle.CollectingBrokerRadar);
        WriteCycleRadarTargets(cycle,radar);
        UpdateCycleRadarCounters(runtime,cycle);
        var detail = runtime.Status;
        if (cycle.ProgressFile is { } progress && File.Exists(progress))
        {
            try
            {
                using var stream=new FileStream(progress,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                using var document = JsonDocument.Parse(stream);
                if (document.RootElement.TryGetProperty("detail", out var reported))
                    detail = reported.GetString() ?? detail;
                else if (document.RootElement.TryGetProperty("shops", out var shops))
                {
                    var exact = document.RootElement.TryGetProperty("exact", out var count) ? count.GetInt32() : 0;
                    detail = $"Reading on route · {shops.GetInt32()} shops · {exact} exact prices";
                }
                else if (document.RootElement.TryGetProperty("percent", out var percent))
                    detail = $"Route {percent.GetDouble():F0}% · smooth pass-by reads";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        }
        if (cycle.WorkerStartedAt is { } started)
            detail += $" · {(DateTimeOffset.UtcNow-started):mm\\:ss}";
        runtime.Cycle = runtime.Cycle with { Phase = cycle.Phase, Detail = detail, NextBrokerAt = null };
        if (_activeJobs.TryGetValue(runtime.Profile.Id, out var job) && !job.IsCompleted) return;
        if (DateTimeOffset.UtcNow < cycle.Next) return;
        TrackJob(runtime, StepCycleAsync(runtime, cycle, radar));
    }

    private async Task StepCycleAsync(ProfileRuntime runtime, CycleRun cycle, RadarSnapshot radar)
    {
        try
        {
            switch (cycle.Phase)
            {
                case "Resume route":
                    if(!radar.LivePlayerPositionAvailable) break;
                    if(cycle.Store.ContinuePass(radar.PlayerX,radar.PlayerY,cycle.RadarPool))
                    {
                        runtime.Status="Continuing unfinished price pool from new character position";
                        cycle.Phase="Reading prices";
                    }
                    else
                    {
                        runtime.Status="Price pool complete; returning to center for next broker";
                        cycle.Phase="Return to center";
                    }
                    cycle.ContinueAfterClientChange=false;
                    cycle.Next=DateTimeOffset.MinValue;
                    break;
                case "Waiting for server":
                    cycle.Phase="Return to center";
                    break;
                case "Return to center":
                    runtime.Status = "Returning to a random safe point in the center zone";
                    await RunCycleRouteAsync(runtime, cycle, [], true);
                    if(!runtime.IsCollectionEnabled) break;
                    cycle.Phase = "Broker inventory"; cycle.Next = DateTimeOffset.MinValue;
                    cycle.ReturnFailures = 0;
                    break;
                case "Settling":
                    if (!radar.IsInsideCenterZone) throw new InvalidOperationException("Character is outside the center zone.");
                    cycle.Phase = "Broker inventory";
                    break;
                case "Broker inventory":
                    if (!radar.IsInsideCenterZone)
                    {
                        runtime.Status="Outside center before broker retry; returning to center";
                        Log($"WARNING {runtime.Profile.Name}: {runtime.Status}");
                        cycle.Phase="Return to center";
                        cycle.Next=DateTimeOffset.MinValue;
                        break;
                    }
                    runtime.Status = "Broker: collecting all listings and quantities";
                    var centerReconciled=await RunCycleBrokerAsync(runtime, cycle, radar);
                    if(!runtime.IsCollectionEnabled) break;
                    if(!centerReconciled)
                    {
                        runtime.Status="Center radar incomplete; retaining history and retrying at center";
                        Log($"WARNING {runtime.Profile.Name}: {runtime.Status}");
                        cycle.Phase="Return to center";
                        cycle.Next=DateTimeOffset.UtcNow.AddSeconds(15);
                        if(++cycle.BrokerFailures>=3)
                        {
                            RequestCycleRecovery(runtime,cycle,"Three incomplete center radar captures");
                        }
                        break;
                    }
                    cycle.BrokerFailures = 0;
                    try { await cycle.Store.ReconcileAsync(); }
                    catch(Exception e) when(e is HttpRequestException or TaskCanceledException or IOException)
                    { Log($"WARNING {runtime.Profile.Name}: history reconciliation unavailable · {e.Message} · local history retained"); }
                    cycle.Store.BeginPass(radar.PlayerX,radar.PlayerY,cycle.RadarPool);
                    cycle.Phase = "Reading prices";
                    break;
                case "Reading prices":
                    await RunContinuousPricesAsync(runtime,cycle,radar);
                    break;
            }
            runtime.Cycle = runtime.Cycle with { Phase = cycle.Phase, Detail = runtime.Status };
        }
        catch (Exception e)
        {
            RecordCycleFailure(runtime, cycle, e);
        }
        finally { cycle.ProgressFile = null; cycle.WorkerStartedAt = null; }
    }

    private async Task RunCycleWorkerAsync(ProfileRuntime runtime, CycleRun cycle, string mode, string output, string? input = null)
    {
        if (!runtime.IsCollectionEnabled || runtime.Session is not { } session) throw new OperationCanceledException();
        cycle.WorkerStartedAt = DateTimeOffset.UtcNow;
        await _worker.RunAsync(session, mode, output, start =>
        {
            start.Environment["PRICECHECK_STOP_FILE"] = cycle.StopFile;
            if (cycle.ProgressFile is not null) start.Environment["PRICECHECK_PROGRESS_FILE"] = cycle.ProgressFile;
            if (input is not null) { start.ArgumentList.Add("--input"); start.ArgumentList.Add(input); }
        });
    }
}
