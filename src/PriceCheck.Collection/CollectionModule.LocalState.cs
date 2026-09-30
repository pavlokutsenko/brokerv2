using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    private readonly Dictionary<Guid,LocalCycleStore> _localStores=[];
    private readonly Dictionary<string,LocalCycleStore> _marketStores=[];
    private readonly Dictionary<Guid,string> _localStoreMarkets=[];
    private readonly Dictionary<Guid,string> _localStoreSessions=[];
    private readonly Dictionary<Guid,DateTimeOffset> _enteredCenters=[];
    private readonly Dictionary<Guid,bool> _insideCenters=[];
    private readonly Dictionary<Guid,string> _worldLoggedSessions=[];
    public MarketCycleStatus MarketStatus(string market,MarketCycleStatus current)
    {
        if(!_marketStores.TryGetValue(CycleQueue.Key(market),out var store))return current;
        store.RequestServerPriceCount();
        return store.Status(current);
    }
    public RadarSnapshot? MarketRadar(string market) =>
        _marketStores.TryGetValue(CycleQueue.Key(market),out var store)?store.MarketRadar:null;
    public void InitializeLocalHistory(IEnumerable<CollectorProfile> profiles)
    {
        foreach(var profile in profiles)
        {
            var runtime=new ProfileRuntime {Profile=profile};
            GetLocalStore(runtime);
        }
    }
    private LocalCycleStore GetLocalStore(ProfileRuntime runtime,bool configure=true)
    {
        var market=CycleQueue.Key(runtime.Profile.Name);
        if(!_localStores.TryGetValue(runtime.Profile.Id,out var store) || _localStoreMarkets.GetValueOrDefault(runtime.Profile.Id)!=market)
        {
            if(!_marketStores.TryGetValue(market,out store))
            {store=new(runtime.Profile);store.Message+=Log;_marketStores.Add(market,store);}
            _localStores[runtime.Profile.Id]=store;_localStoreMarkets[runtime.Profile.Id]=market;
            _localStoreSessions.Remove(runtime.Profile.Id);
            RecoverLocalCaptureFiles(runtime.Profile,store);
        }
        if(configure)store.Configure(runtime.Profile);
        store.WakeSender();return store;
    }
    private void ObserveLocalState(ProfileRuntime runtime,RadarSnapshot radar)
    {
        if(GetCenterZone(runtime.Profile) is not {} center)return;
        var store=GetLocalStore(runtime);
        var session=$"{radar.ProcessId}:{runtime.Session?.StartedAtUtc:O}";
        if(!_localStoreSessions.TryGetValue(runtime.Profile.Id,out var previous)||previous!=session)
        {
            store.BeginSession(session,_cycles.TryGetValue(runtime.Profile.Id,out var pendingCycle) && pendingCycle.ContinueAfterClientChange);
            _localStoreSessions[runtime.Profile.Id]=session;
            _insideCenters[runtime.Profile.Id]=false;
        }
        if(radar.WorldCharacterDataAvailable&&radar.LivePlayerPositionAvailable&&_worldLoggedSessions.GetValueOrDefault(runtime.Profile.Id)!=session)
        {
            _worldLoggedSessions[runtime.Profile.Id]=session;
            Log($"INFO {runtime.Profile.Name}: world loaded · PID {runtime.ProcessId} · reader data and current player position available");
        }
        var inside=radar.LivePlayerPositionAvailable&&Math.Sqrt(Math.Pow(radar.PlayerX-center.X,2)+Math.Pow(radar.PlayerY-center.Y,2))<=center.Radius;
        if(inside&&!_insideCenters.GetValueOrDefault(runtime.Profile.Id))_enteredCenters[runtime.Profile.Id]=DateTimeOffset.UtcNow;
        _insideCenters[runtime.Profile.Id]=inside;
        var entered=_enteredCenters.GetValueOrDefault(runtime.Profile.Id,DateTimeOffset.MaxValue);
        var boundary=_cycles.TryGetValue(runtime.Profile.Id,out var cycle)?cycle.RadarPool:CycleRadarPool.ForCity("Giran");
        store.Observe(radar,boundary,center,entered);
        if(cycle is not null){cycle.CenterEnteredAt=entered;cycle.WasInsideCenter=inside;}
    }
}
