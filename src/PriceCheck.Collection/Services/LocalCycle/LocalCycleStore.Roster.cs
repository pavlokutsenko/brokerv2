using System.Text;
using System.Text.Json;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public sealed partial class LocalCycleStore
{
    private readonly HashSet<string> _serverActiveRosterKeys=[];
    private readonly HashSet<string> _serverActiveTradingKeys=[];
    private RadarSnapshot? _marketRadar;
    private int? _serverCurrentPriceTraders;
    private DateTimeOffset? _serverPriceCountAt;
    private DateTimeOffset _nextServerCountRequest;
    private Task? _serverCountRequest;
    public RadarSnapshot? MarketRadar { get { lock(_sync)return _marketRadar; } }
    public void RecordMarketRadar(RadarSnapshot radar)
    {
        lock(_sync)if(_marketRadar is null || radar.CapturedAtUtc>=_marketRadar.CapturedAtUtc)_marketRadar=radar;
    }
    public void RequestServerPriceCount()
    {
        lock(_sync)
        {
            if(_serverCountRequest is {IsCompleted:false} || _clock()<_nextServerCountRequest)return;
            _nextServerCountRequest=_clock().AddMinutes(1);
            _serverCountRequest=Task.Run(RefreshServerPriceCountAsync);
        }
    }
    private async Task RefreshServerPriceCountAsync()
    {
        try
        {
            using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(20)};
            string url;double recheck;
            lock(_sync){url=_serverUrl;recheck=_recheckHours;}
            using var response=await http.PostAsync($"{url.TrimEnd('/')}/ingest/local-cycle/history",
                new StringContent(JsonSerializer.Serialize(new {market=_profile.Name,city="Giran",
                    traderKeys=Array.Empty<string>(),includeActiveRoster=true},Json),Encoding.UTF8,"application/json"));
            response.EnsureSuccessStatusCode();
            using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root=document.RootElement;
            if(!root.TryGetProperty("activeRosterComplete",out var complete) || complete.ValueKind!=JsonValueKind.True)
                throw new InvalidDataException("Server price count requires a complete active roster");
            var rows=root.GetProperty("traders");
            if(rows.ValueKind!=JsonValueKind.Array || rows.GetArrayLength()>10000)
                throw new InvalidDataException("Server active roster is invalid or exceeds its bound");
            var now=_clock();var count=CountCurrentServerPrices(rows,now,recheck);
            lock(_sync){_serverCurrentPriceTraders=count;_serverPriceCountAt=now;}
        }
        catch(Exception error) when(error is HttpRequestException or TaskCanceledException or IOException or JsonException or InvalidDataException)
        { Message?.Invoke($"WARNING {_profile.Name}: server price count unavailable · {error.Message}"); }
    }
    public static int CountCurrentServerPrices(JsonElement rows,DateTimeOffset now,double recheckHours)
    {
        var count=0;
        foreach(var row in rows.EnumerateArray())
        {
            if(!row.TryGetProperty("isActive",out var active) || active.ValueKind!=JsonValueKind.True ||
               !row.TryGetProperty("kioskType",out var kind) || kind.ValueKind!=JsonValueKind.Number || kind.GetInt32() is not (1 or 3 or 8) ||
               !row.TryGetProperty("lastReadAtUtc",out var read) || read.ValueKind!=JsonValueKind.String ||
               !read.TryGetDateTimeOffset(out var readAt) || readAt>now || now-readAt>TimeSpan.FromHours(recheckHours))continue;
            if(row.TryGetProperty("verificationRequiredAtUtc",out var required) && required.ValueKind==JsonValueKind.String &&
               required.TryGetDateTimeOffset(out var requiredAt) && readAt<requiredAt)continue;
            if(row.TryGetProperty("lastReadKioskType",out var readType) && readType.ValueKind==JsonValueKind.Number &&
               readType.GetInt32()!=kind.GetInt32())continue;
            count++;
        }
        return count;
    }
    public async Task ImportActiveServerRosterAsync(CycleRadarPool boundary)
    {
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(20)};
        using var response=await http.PostAsync($"{_serverUrl.TrimEnd('/')}/ingest/local-cycle/history",
            new StringContent(JsonSerializer.Serialize(new {market=_profile.Name,city="Giran",
                traderKeys=Array.Empty<string>(),includeActiveRoster=true},Json),Encoding.UTF8,"application/json"));
        response.EnsureSuccessStatusCode();
        using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var imported=ImportActiveServerRoster(document.RootElement,boundary);
        Message?.Invoke($"INFO {_profile.Name}: server active roster · {imported} historical identities imported for center radar reconciliation");
    }

    // Importing history is not a live observation, successful price read or
    // closure. Only a subsequent complete center radar can retire these names.
    public int ImportActiveServerRoster(JsonElement response,CycleRadarPool boundary)
    {
        if(!response.TryGetProperty("activeRosterComplete",out var complete) || complete.ValueKind!=JsonValueKind.True)
            throw new InvalidDataException("Server did not return a complete active trader roster");
        var rows=response.GetProperty("traders");
        if(rows.ValueKind!=JsonValueKind.Array || rows.GetArrayLength()>10000)
            throw new InvalidDataException("Server active roster is invalid or exceeds its bound");
        lock(_sync){_serverCurrentPriceTraders=CountCurrentServerPrices(rows,_clock(),_recheckHours);_serverPriceCountAt=_clock();}
        var added=0;
        lock(_sync)Transaction(()=>{
            _collectionBoundary=boundary;
            _serverActiveRosterKeys.Clear();
            _serverActiveTradingKeys.Clear();
            foreach(var row in rows.EnumerateArray())
            {
                var key=CycleQueue.Key(row.GetProperty("traderKey").GetString()!);
                if(key.Length==0 || row.GetProperty("isActive").ValueKind!=JsonValueKind.True)continue;
                _serverActiveRosterKeys.Add(key);
                if(row.GetProperty("kioskType").GetInt32() is 1 or 3 or 8)_serverActiveTradingKeys.Add(key);
                if(_traders.ContainsKey(key) ||
                    !row.TryGetProperty("worldX",out var x) || x.ValueKind!=JsonValueKind.Number || !x.TryGetDouble(out var px) || !double.IsFinite(px) ||
                    !row.TryGetProperty("worldY",out var y) || y.ValueKind!=JsonValueKind.Number || !y.TryGetDouble(out var py) || !double.IsFinite(py) || !boundary.Inside(px,py))continue;
                var type=row.GetProperty("kioskType").GetInt32();if(type is not (1 or 3 or 8))continue;
                var seen=row.GetProperty("lastSeenAtUtc").GetDateTimeOffset();
                var name=row.TryGetProperty("displayName",out var display)?display.GetString():key;
                var trader=new LocalTrader{Key=key,Name=string.IsNullOrWhiteSpace(name)?key:name,X=px,Y=py,HasPosition=true,
                    KioskType=type,ObservedAt=seen,ChangedAt=seen,Reason="Server roster: awaiting live verification"};
                if(row.TryGetProperty("stateObservedAtUtc",out var at) && at.ValueKind==JsonValueKind.String)trader.StateAt=at.GetDateTimeOffset();
                _traders.Add(key,trader);Save(trader);added++;
            }
        });
        return added;
    }
}
