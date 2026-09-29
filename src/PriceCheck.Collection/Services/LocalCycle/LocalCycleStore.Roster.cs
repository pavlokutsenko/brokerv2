using System.Text;
using System.Text.Json;
using PriceCheck.Collector.Models;

namespace PriceCheck.Collector.Services;

public sealed partial class LocalCycleStore
{
    private readonly HashSet<string> _serverActiveRosterKeys=[];
    private readonly HashSet<string> _serverActiveTradingKeys=[];
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
