using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PriceCheck.Collector.Services;

public sealed partial class LocalCycleStore
{
    private Task? _sender;
    private readonly CancellationTokenSource _senderStop=new();
    private void AddOutbox(string id,string kind,string endpoint,object payload)
    {
        var body=JsonSerializer.SerializeToElement(payload,Json);
        var key=body.TryGetProperty("traderKey",out var direct)?direct.GetString():body.TryGetProperty("trader",out var trader)?trader.GetProperty("traderKey").GetString():null;
        _db.Command("INSERT OR IGNORE INTO outbox(operation_id,kind,url,payload,next_attempt,trader_key) VALUES(?,?,?,?,?,?)",id,kind,
            $"{_serverUrl.TrimEnd('/')}/ingest/local-cycle/{endpoint}",body.GetRawText(),_clock().ToUnixTimeMilliseconds(),key);
    }
    public int PendingUploads { get {lock(_sync)return int.Parse(_db.Query("SELECT COUNT(*) FROM outbox")[0][0]!); } }
    public IReadOnlyList<LocalCycleOperation> PendingOperations()
    {lock(_sync)return _db.Query("SELECT operation_id,kind,payload,attempts,last_error FROM outbox ORDER BY rowid LIMIT 500")
        .Select(row=>new LocalCycleOperation(row[0]!,row[1]!,row[2]!,int.Parse(row[3]!),row[4])).ToArray();}
    public void WakeSender()
    { lock(_sync) if(_sender is null || _sender.IsCompleted) _sender=Task.Run(SendPendingAsync); }
    public async Task SendPendingAsync()
    {
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(30)};
        while(!_senderStop.IsCancellationRequested)
        {
            string?[]? row;
            lock(_sync) row=_db.Query("SELECT operation_id,kind,url,payload,attempts,trader_key FROM outbox a WHERE next_attempt<=? AND NOT EXISTS (SELECT 1 FROM outbox b WHERE b.trader_key=a.trader_key AND b.kind='state' AND b.rowid<a.rowid) ORDER BY CASE kind WHEN 'state' THEN 0 WHEN 'price' THEN 1 ELSE 2 END,next_attempt LIMIT 1",_clock().ToUnixTimeMilliseconds()).FirstOrDefault();
            if(row is null)
            {
                if(PendingUploads==0) return;
                try{await Task.Delay(1000,_senderStop.Token).ConfigureAwait(false);}catch(OperationCanceledException){return;}continue;
            }
            try
            {
                using var body=new StringContent(row[3]!,Encoding.UTF8,"application/json");
                using var response=await http.PostAsync(row[2],body,_senderStop.Token).ConfigureAwait(false);
                if(!response.IsSuccessStatusCode)
                {var detail=(await response.Content.ReadAsStringAsync()).Trim();throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {detail[..Math.Min(1000,detail.Length)]}");}
                var acknowledgement=row[1]=="price"?await response.Content.ReadAsStringAsync(_senderStop.Token).ConfigureAwait(false):"";
                var rejectedCurrent=ApplyUploadAcknowledgement(row,acknowledgement);
                if(row[1]=="price")Message?.Invoke($"{(rejectedCurrent?"WARNING":"INFO")} {_profile.Name}: trader={row[5]} · snapshot {row[0]} · {(rejectedCurrent?"server retained historical read; awaiting verification history":"server accepted individual trader")}");
            }
            catch(Exception e) when(e is HttpRequestException or TaskCanceledException or IOException)
            {
                var attempts=int.Parse(row[4]!)+1;
                lock(_sync)_db.Command("UPDATE outbox SET attempts=?,last_error=?,next_attempt=? WHERE operation_id=?",attempts,e.Message,
                    _clock().AddSeconds(Math.Min(30,Math.Pow(2,Math.Min(attempts,5)))).ToUnixTimeMilliseconds(),row[0]);
                if(attempts==1 || attempts%30==0)Message?.Invoke($"WARNING {_profile.Name}: trader={row[5]} · {row[1]} {row[0]} · delivery retry {attempts} · {e.Message}");
            }
        }
    }
    public async Task ReconcileAsync()
    {
        string[] keys;lock(_sync)keys=_traders.Keys.ToArray();
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(15)};
        foreach(var batch in keys.Chunk(500))
        {
            using var response=await http.PostAsync($"{_serverUrl.TrimEnd('/')}/ingest/local-cycle/history",
                new StringContent(JsonSerializer.Serialize(new{market=_profile.Name,city="Giran",traderKeys=batch},Json),Encoding.UTF8,"application/json"));
            response.EnsureSuccessStatusCode();
            using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            lock(_sync)Transaction(()=>{
                foreach(var row in document.RootElement.GetProperty("traders").EnumerateArray())
                {
                    if(!_traders.TryGetValue(CycleQueue.Key(row.GetProperty("traderKey").GetString()!),out var t))continue;
                    var read=row.TryGetProperty("lastReadAtUtc",out var captured)&&captured.ValueKind==JsonValueKind.String?captured.GetDateTimeOffset():(DateTimeOffset?)null;
                    var required=row.TryGetProperty("verificationRequiredAtUtc",out var need)&&need.ValueKind==JsonValueKind.String?need.GetDateTimeOffset():(DateTimeOffset?)null;
                    var serverToken=row.TryGetProperty("verificationRevision",out var token)&&token.ValueKind==JsonValueKind.String?token.GetString():null;
                    var serverClosed=row.TryGetProperty("isActive",out var active)&&active.ValueKind==JsonValueKind.False;
                    var serverStateAt=row.TryGetProperty("stateObservedAtUtc",out var stateAt)&&stateAt.ValueKind==JsonValueKind.String?stateAt.GetDateTimeOffset():DateTimeOffset.MinValue;
                    if(serverClosed && t.SeenOpenThisSession && !t.ClosedThisSession && t.StateAt>serverStateAt &&
                        (t.ServerClosedHandledAt is null || serverStateAt>t.ServerClosedHandledAt))
                    {
                        Invalidate(t,"Shop reopened",t.StateAt);StateEvent(t,"reopened",t.StateAt,null,new(0,0,500));t.ServerClosedHandledAt=serverStateAt;
                    }
                    var mayAdopt=t.Reason=="New shop" || t.Reason=="Server requires verification" || t.NeedsServerHistory || !t.Dirty;
                    if(read is {} at && (t.LastRead is null || at>t.LastRead) && (required is null || at>=required) && !t.ClosedThisSession &&
                        mayAdopt && (t.Reason=="New shop" || at>=t.ChangedAt))
                    {
                        t.LastRead=at;t.LastSnapshotId=null;t.NeedsServerHistory=false;t.Dirty=false;if(serverToken is not null)t.VerificationToken=serverToken;
                        t.CheckedX=row.TryGetProperty("checkedX",out var cx)&&cx.ValueKind==JsonValueKind.Number?cx.GetDouble():t.X;
                        t.CheckedY=row.TryGetProperty("checkedY",out var cy)&&cy.ValueKind==JsonValueKind.Number?cy.GetDouble():t.Y;
                        if(t.HasPosition&&Distance(t.X,t.Y,t.CheckedX,t.CheckedY)>=20)
                        {Invalidate(t,"Shop moved",_clock());StateEvent(t,"moved",_clock(),null,new(0,0,500));}
                        if(row.TryGetProperty("lastReadKioskType",out var readType)&&readType.ValueKind==JsonValueKind.Number&&readType.GetInt32()!=t.KioskType)
                        {Invalidate(t,"Shop type changed",_clock());StateEvent(t,"type_changed",_clock(),null,new(0,0,500));}
                    }
                    if(required is {} changed && (t.LastRead is null || changed>t.LastRead) && (t.ServerRequiredAt is null || changed>t.ServerRequiredAt))
                    {
                        if(mayAdopt || changed>t.ChangedAt)
                        {Invalidate(t,"Server requires verification",changed);if(serverToken is not null)t.VerificationToken=serverToken;}
                        t.ServerRequiredAt=changed;
                    }
                    ReconcileRejectedGeneration(t,required,serverToken);
                    Save(t);
                }
            });
        }
    }
}

public sealed record LocalCycleOperation(string Id,string Kind,string Payload,int Attempts,string? Error);
