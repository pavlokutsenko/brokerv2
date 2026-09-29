using PriceCheck.Collection;
using PriceCheck.Contracts;
using PriceCheck.Collector.Contracts;
using PriceCheck.Collector.Models;

internal sealed class FakeProcesses : IClientProcessService
{
    private readonly Dictionary<int, ClientSession> _sessions = [];
    private int _next = 100;
    public HashSet<int> Proxies { get; } = [];
    public List<(int Pid, GameWindowCorner Corner)> WindowPlacements { get; } = [];
    public int Terminations { get; private set; }
    public int Releases { get; private set; }
    public ClientSession? Identity(int pid) => _sessions.GetValueOrDefault(pid);
    public bool IsAlive(int pid) => _sessions.ContainsKey(pid);
    public Task<int> LaunchAndBindAsync(CollectorProfile profile, LaunchTemplate? template, CancellationToken token)
    {
        var pid = _next++;
        _sessions.Add(pid, new(pid, DateTimeOffset.UtcNow));
        Proxies.Add(pid);
        return Task.FromResult(pid);
    }
    public Task WaitForGameWindowAsync(int pid, CancellationToken token) => Task.CompletedTask;
    public bool TryPlaceGameWindow(ClientSession session, GameWindowCorner corner)
    {
        WindowPlacements.Add((session.ProcessId, corner));
        return true;
    }
    public Task ActivateLateAgentAsync(int pid, CancellationToken token) => Task.CompletedTask;
    public ClientProtectionStatus Protection(int pid) => new(true, true, true, 2, 64, 64, "TEST", null);
    public Task ValidateProtectionAsync(int pid, bool requireWorld, CancellationToken token) => Task.CompletedTask;
    public void Terminate(int pid) { Terminations++; _sessions.Remove(pid); Proxies.Remove(pid); }
    public void Release(int? pid) { Releases++; if (pid is int value) Proxies.Remove(value); }
    public void ReusePid(int pid) => _sessions[pid] = new(pid, _sessions[pid].StartedAtUtc.AddSeconds(1));
}

internal sealed class FakeRadar : IRadarSessions
{
    public int Starts { get; private set; }
    public HashSet<int> Active { get; } = [];
    public bool FailStart { get; set; }
    public TaskCompletionSource? PendingStart { get; set; }
    public RadarSnapshot? CurrentSnapshot { get; set; }
    public async Task StartAsync(int pid, MarketZone? zone, bool collectionEnabled, CancellationToken token)
    {
        Starts++;
        if (FailStart) throw new InvalidOperationException("Synthetic receive signature mismatch.");
        if (PendingStart is not null) await PendingStart.Task;
        Active.Add(pid);
    }
    public RadarSnapshot? Snapshot(int pid, MarketZone? zone, bool collectionEnabled) => CurrentSnapshot;
    public Task StopAsync(int pid) { Active.Remove(pid); return Task.CompletedTask; }
    public ValueTask DisposeAsync() { Active.Clear(); return ValueTask.CompletedTask; }
}

internal sealed class PendingWorker : ICollectionWorker
{
    public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Runs { get; private set; }
    public Task RunAsync(ClientSession session, string mode, string output, Action<System.Diagnostics.ProcessStartInfo> configure)
    {
        Runs++;
        return Complete.Task;
    }
}

internal sealed class CycleReplayWorker:ICollectionWorker
{
    public int BrokerRuns {get;private set;}
    public bool UnboundBroker {get;set;}
    public bool IncompleteBroker {get;set;}
    public TaskCompletionSource? BrokerGate {get;set;}
    public System.Collections.Concurrent.ConcurrentDictionary<int,string> PriceInputs {get;}=[];
    public System.Collections.Concurrent.ConcurrentDictionary<int,string> InitialRadarFrames {get;}=[];
    public System.Collections.Concurrent.ConcurrentDictionary<int,TaskCompletionSource> PriceWaiters {get;}=[];
    public System.Collections.Concurrent.ConcurrentDictionary<int,string> StopFiles {get;}=[];
    public async Task RunAsync(ClientSession session,string mode,string output,Action<System.Diagnostics.ProcessStartInfo> configure)
    {
        var start=new System.Diagnostics.ProcessStartInfo();configure(start);
        StopFiles[session.ProcessId]=start.Environment["PRICECHECK_STOP_FILE"]!;
        var now=DateTimeOffset.UtcNow;
        if(mode=="broker")
        {
            BrokerRuns++;
            if(BrokerGate is not null) await BrokerGate.Task;
            await File.WriteAllTextAsync(output,System.Text.Json.JsonSerializer.Serialize(new {
                complete=!IncompleteBroker,captured_at=now,started_at=now,summary=new {unique_traders=2,listing_rows=2,market_item_requests=2,market_item_responses=IncompleteBroker?1:2},
                warnings=IncompleteBroker?new[]{"Synthetic broker omitted one response"}:Array.Empty<string>(),
                binding_pid=session.ProcessId,bindings=new[]{new {object_id=7,name="Shop",kiosk_type=1,x=100,y=100},new {object_id=8,name="Later",kiosk_type=1,x=200,y=200}}.Take(UnboundBroker?1:2),
                rows=new[]{new {trader_object_id=7,trader_name="Shop",item_id=100,store_type=1,amount=3},new {trader_object_id=8,trader_name="Later",item_id=100,store_type=1,amount=3}}}));return;
        }
        using var input=System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(start.ArgumentList[1]));
        if(input.RootElement.GetProperty("mode").GetString()=="center")
        { await File.WriteAllTextAsync(output,"{\"reason\":\"completed\",\"destination\":[0,0]}");return; }
        var wait=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PriceInputs[session.ProcessId]=input.RootElement.GetRawText();
        var radarPath=input.RootElement.GetProperty("radarFile").GetString();
        InitialRadarFrames[session.ProcessId]=File.Exists(radarPath)?await File.ReadAllTextAsync(radarPath!):"";
        PriceWaiters[session.ProcessId]=wait;await wait.Task;
        if(File.Exists(StopFiles[session.ProcessId]))
        { await File.WriteAllTextAsync(output,"{\"reason\":\"cancelled\",\"shops\":[],\"attempted\":[],\"failures\":[]}");return; }
        if(input.RootElement.GetProperty("targets").GetArrayLength()==0)
        {
            await File.WriteAllTextAsync(output,"{\"reason\":\"completed\",\"shops\":[],\"attempted\":[],\"failures\":[],\"radarReviewed\":[\"Late\"]}");return;
        }
        var targets=input.RootElement.GetProperty("targets").EnumerateArray().ToArray();
        await File.WriteAllTextAsync(output,System.Text.Json.JsonSerializer.Serialize(new {
            reason="completed",attempted=targets.Select(t=>t.GetProperty("traderKey").GetString()).ToArray(),failures=Array.Empty<object>(),shops=targets.Select(target=>new {
                trader_key=target.GetProperty("traderKey").GetString(),precision="wire_int64",trader=new {object_id=target.GetProperty("object_id").GetInt32(),name=target.GetProperty("name").GetString(),kiosk_type=target.GetProperty("kiosk_type").GetInt32(),x=target.GetProperty("x").GetDouble(),y=target.GetProperty("y").GetDouble()},side="sell",at=DateTimeOffset.UtcNow,read_started_at=DateTimeOffset.UtcNow.AddMilliseconds(-1),verification_revision=target.GetProperty("verification_revision").GetString(),
                rows=new[]{new {item_id=100L,item_object_id=700L,quantity=3L,enchant=7,price=9007199254740993L,base_price=0L}}}).ToArray()}));
    }
}

internal sealed class FakeMarketTasks : IMarketTaskClient
{
    public bool DelayNextBatchOnce { get; set; }
    public bool NoJobs {get;set;}
    public Task<MarketCycleStatus> StateAsync(CollectorProfile p) => Task.FromResult(new MarketCycleStatus {Active=1,Pending=1});
    public Task<MarketTaskClaim> ClaimAsync(CollectorProfile p,string workerId,string batchId,string requestId,
        IReadOnlyList<string> keys,double x,double y)
    {
        if(NoJobs) return Task.FromResult(new MarketTaskClaim(true,[]));
        if (DelayNextBatchOnce && !keys.Contains("SHOP") && keys.Contains("LATER"))
        { DelayNextBatchOnce=false; return Task.FromResult(new MarketTaskClaim(true,[],true)); }
        return Task.FromResult(new MarketTaskClaim(true,
            keys.Contains("SHOP")?[new("1","SHOP","Shop",1,100,100,"cycle:fixture","New shop",DateTimeOffset.UtcNow,requestId)]:
            keys.Contains("LATER")?[new("2","LATER","Later",1,200,200,"cycle:fixture","New shop",DateTimeOffset.UtcNow,requestId)]:[]));
    }
    public Task<IReadOnlyList<string>> RenewAsync(CollectorProfile p,string workerId,IReadOnlyList<ServerPriceJob> jobs) =>
        Task.FromResult<IReadOnlyList<string>>(jobs.Select(j=>j.TraderId).ToArray());
    public Task ReleaseAsync(CollectorProfile p,string workerId,IReadOnlyList<MarketTaskRelease> jobs) => Task.CompletedTask;
}

