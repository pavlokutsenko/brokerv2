using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PriceCheck.Collector.Services;

public sealed partial class LocalCycleStore
{
    private Task? _sender;
    private readonly CancellationTokenSource _senderStop=new();
    private readonly SemaphoreSlim _deliveryGate=new(1,1);
    private readonly HashSet<string> _sendingOperations=[];
    private void AddOutbox(string id,string kind,string endpoint,object payload)
    {
        var body=JsonSerializer.SerializeToElement(payload,Json);
        var key=body.TryGetProperty("traderKey",out var direct)?direct.GetString():body.TryGetProperty("trader",out var trader)?trader.GetProperty("traderKey").GetString():null;
        if(kind=="broker" && key is not null && _db.Query("SELECT 1 FROM outbox WHERE operation_id=?",id).Count==0)
            CompactBrokerTail(key,body);
        _db.Command("INSERT OR IGNORE INTO outbox(operation_id,kind,url,payload,next_attempt,trader_key) VALUES(?,?,?,?,?,?)",id,kind,
            $"{_serverUrl.TrimEnd('/')}/ingest/local-cycle/{endpoint}",body.GetRawText(),_clock().ToUnixTimeMilliseconds(),key);
    }
    private void CompactBrokerTail(string key,JsonElement current)
    {
        // A later complete inventory supersedes queued broker-only observations
        // from this profile. State and price operations are ordering barriers.
        if(!current.TryGetProperty("compositionComplete",out var complete) || complete.ValueKind!=JsonValueKind.True ||
            !current.TryGetProperty("sourceId",out var source) || source.ValueKind!=JsonValueKind.String ||
            !current.TryGetProperty("observedAtUtc",out var observed) || observed.ValueKind!=JsonValueKind.String ||
            !DateTimeOffset.TryParse(observed.GetString(),out var currentAt))return;
        foreach(var row in _db.Query("SELECT operation_id,kind,payload FROM outbox WHERE trader_key=? ORDER BY rowid DESC",key))
        {
            if(row[1]!="broker" || _sendingOperations.Contains(row[0]!))break;
            try
            {
                using var old=JsonDocument.Parse(row[2]!);
                if(!old.RootElement.TryGetProperty("sourceId",out var oldSource) || oldSource.ValueKind!=JsonValueKind.String ||
                    oldSource.GetString()!=source.GetString() ||
                    !old.RootElement.TryGetProperty("observedAtUtc",out var oldObserved) || oldObserved.ValueKind!=JsonValueKind.String ||
                    !DateTimeOffset.TryParse(oldObserved.GetString(),out var oldAt) || oldAt>currentAt)break;
            }
            catch(JsonException){break;}
            _db.Command("DELETE FROM outbox WHERE operation_id=?",row[0]);
        }
    }
    public int PendingUploads { get {lock(_sync)return int.Parse(_db.Query("SELECT COUNT(*) FROM outbox")[0][0]!); } }
    public BrokerDeliveryStatus? LatestBrokerDelivery
    {
        get { lock(_sync)
        {
            var row=_db.Query("SELECT traders,accepted,captured_at FROM latest_broker_delivery WHERE id=1").FirstOrDefault();
            return row is null?null:new(int.Parse(row[0]!),int.Parse(row[1]!),DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(row[2]!)));
        } }
    }
    public IReadOnlyList<LocalCycleOperation> PendingOperations()
    {lock(_sync)return _db.Query("SELECT operation_id,kind,payload,attempts,last_error FROM outbox ORDER BY rowid LIMIT 500")
        .Select(row=>new LocalCycleOperation(row[0]!,row[1]!,row[2]!,int.Parse(row[3]!),row[4])).ToArray();}
    public void WakeSender()
    { lock(_sync) if(_sender is null || _sender.IsCompleted) _sender=Task.Run(SendPendingAsync); }
    public async Task SendPendingAsync()
    {
        try{await _deliveryGate.WaitAsync(_senderStop.Token).ConfigureAwait(false);}
        catch(OperationCanceledException){return;}
        try
        {
            using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(30)};
            await Task.WhenAll(Enumerable.Range(0,8).Select(_=>SendLaneAsync(http))).ConfigureAwait(false);
        }
        finally{_deliveryGate.Release();}
    }
    private async Task SendLaneAsync(HttpClient http)
    {
        while(!_senderStop.IsCancellationRequested)
        {
            string?[]? row;
            lock(_sync)
            {
                // Different shops can upload concurrently. Each shop remains FIFO,
                // including retries, so no later generation can overtake its state.
                row=null;
                foreach(var kind in new[]{"state","price","broker"})
                {
                    row=_db.Query("SELECT operation_id,kind,url,payload,attempts,trader_key FROM outbox a WHERE a.kind=? AND next_attempt<=? AND NOT EXISTS (SELECT 1 FROM outbox b WHERE b.trader_key=a.trader_key AND b.rowid<a.rowid) ORDER BY next_attempt,a.rowid LIMIT 32",kind,_clock().ToUnixTimeMilliseconds())
                        .FirstOrDefault(candidate=>!_sendingOperations.Contains(candidate[0]!));
                    if(row is not null)break;
                }
                if(row is not null)_sendingOperations.Add(row[0]!);
            }
            if(row is null)
            {
                if(PendingUploads==0) return;
                try{await Task.Delay(100,_senderStop.Token).ConfigureAwait(false);}catch(OperationCanceledException){return;}continue;
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
            catch(OperationCanceledException) when(_senderStop.IsCancellationRequested){return;}
            catch(Exception e) when(e is HttpRequestException or TaskCanceledException or IOException)
            {
                var attempts=int.Parse(row[4]!)+1;
                lock(_sync)_db.Command("UPDATE outbox SET attempts=?,last_error=?,next_attempt=? WHERE operation_id=?",attempts,e.Message,
                    _clock().AddSeconds(Math.Min(30,Math.Pow(2,Math.Min(attempts,5)))).ToUnixTimeMilliseconds(),row[0]);
                if(attempts==1 || attempts%30==0)Message?.Invoke($"WARNING {_profile.Name}: trader={row[5]} · {row[1]} {row[0]} · delivery retry {attempts} · {e.Message}");
            }
            finally{lock(_sync)_sendingOperations.Remove(row[0]!);}
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
                    var serverNonTrading=row.TryGetProperty("kioskType",out var kind)&&kind.ValueKind==JsonValueKind.Number&&kind.GetInt32() is not (1 or 3 or 8);
                    var serverStateAt=row.TryGetProperty("stateObservedAtUtc",out var stateAt)&&stateAt.ValueKind==JsonValueKind.String?stateAt.GetDateTimeOffset():DateTimeOffset.MinValue;
                    if((serverClosed || serverNonTrading) && t.SeenOpenThisSession && !t.ClosedThisSession && t.StateAt>serverStateAt &&
                        (t.ServerClosedHandledAt is null || serverStateAt>t.ServerClosedHandledAt ||
                         t.ServerReopenObservedAt is null || t.StateAt>t.ServerReopenObservedAt))
                    {
                        Invalidate(t,"Shop reopened",t.StateAt);StateEvent(t,"reopened",t.StateAt,null,new(0,0,500));
                        t.ServerClosedHandledAt=serverStateAt;t.ServerReopenObservedAt=t.StateAt;
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
public sealed record BrokerDeliveryStatus(int Traders,int Accepted,DateTimeOffset CapturedAtUtc);
