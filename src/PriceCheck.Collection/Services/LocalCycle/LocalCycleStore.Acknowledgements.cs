using System.Text.Json;

namespace PriceCheck.Collector.Services;

public sealed partial class LocalCycleStore
{
    private const string AwaitingServerHistoryReason="Server retained historical read";

    private bool ApplyUploadAcknowledgement(string?[] operation,string response)
    {
        var historical=operation[1]=="price" && IsHistoricalAcknowledgement(response);
        var rejectedLatest=false;
        lock(_sync)Transaction(()=>{
            if(historical)
            {
                using var payload=JsonDocument.Parse(operation[3]!);
                var body=payload.RootElement;
                var key=CycleQueue.Key(body.GetProperty("traderKey").GetString()!);
                if(_traders.TryGetValue(key,out var trader) && !trader.Dirty && !trader.ClosedThisSession &&
                    LatestCleanReadMatches(trader,operation[0]!,body))
                {
                    // Invalidate only the acknowledged generation. Keep the durable
                    // capture/read time, then reconcile the server token off the UI thread.
                    Invalidate(trader,AwaitingServerHistoryReason,_clock());
                    trader.NeedsServerHistory=true;Save(trader);rejectedLatest=true;
                }
            }
            // A historical result was delivered successfully. It is not a transport retry.
            _db.Command("DELETE FROM outbox WHERE operation_id=?",operation[0]);
        });
        return rejectedLatest;
    }

    private static bool IsHistoricalAcknowledgement(string response)
    {
        try
        {
            using var parsed=JsonDocument.Parse(response);
            return parsed.RootElement.ValueKind==JsonValueKind.Object &&
                parsed.RootElement.TryGetProperty("current",out var current) && current.ValueKind==JsonValueKind.False;
        }
        catch(JsonException){return false;}
    }

    private bool LatestCleanReadMatches(LocalTrader trader,string snapshotId,JsonElement payload)
    {
        if(trader.LastRead is null || !payload.TryGetProperty("capturedAtUtc",out var captured) ||
            captured.ValueKind!=JsonValueKind.String || !captured.TryGetDateTimeOffset(out var at) || at!=trader.LastRead ||
            !payload.TryGetProperty("verificationRevision",out var token) || token.ValueKind!=JsonValueKind.String ||
            token.GetString()!=trader.VerificationToken)return false;
        if(trader.LastSnapshotId is not null)return trader.LastSnapshotId==snapshotId;
        // Pre-upgrade JSON has no LastSnapshotId. Prove identity from its durable
        // latest capture rather than treating any old HTTP reply as a current read.
        var newest=_db.Query("SELECT snapshot_id FROM snapshots WHERE trader_key=? AND captured_at=? AND revision=? ORDER BY rowid DESC LIMIT 1",
            trader.Key,at.ToUnixTimeMilliseconds(),trader.Revision).FirstOrDefault();
        return newest?[0]==snapshotId;
    }

    private void ReconcileRejectedGeneration(LocalTrader trader,DateTimeOffset? required,string? serverToken)
    {
        var mismatch=required is not null && serverToken is not null && serverToken!=trader.VerificationToken;
        if(!trader.NeedsServerHistory && (!mismatch || trader.Dirty || HasPendingOwnGeneration(trader)))return;
        if(serverToken is null)return;
        if(!trader.NeedsServerHistory)Invalidate(trader,"Server requires verification",required??_clock());
        // The server requirement can predate our rejected local capture. Token
        // equality, not timestamp ordering, decides which read can become current.
        trader.VerificationToken=serverToken;
        trader.NeedsServerHistory=false;trader.Dirty=true;trader.Reason="Server requires verification";
        trader.ServerRequiredAt=required;trader.Error=null;
        Save(trader);
    }

    private bool HasPendingOwnGeneration(LocalTrader trader)
    {
        foreach(var row in _db.Query("SELECT payload FROM outbox WHERE trader_key=? AND kind IN ('price','state')",trader.Key))
        {
            using var payload=JsonDocument.Parse(row[0]!);
            if(payload.RootElement.TryGetProperty("verificationRevision",out var token) && token.ValueKind==JsonValueKind.String &&
                token.GetString()==trader.VerificationToken)return true;
        }
        return false;
    }
}
