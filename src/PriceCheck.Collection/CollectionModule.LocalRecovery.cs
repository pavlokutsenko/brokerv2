using System.Text.Json;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

namespace PriceCheck.Collection;

public sealed partial class CollectionModule
{
    // Native fsyncs each complete shop line before moving on. Replay closes the
    // power-loss gap between that event and the application SQLite transaction.
    private void RecoverLocalCaptureFiles(CollectorProfile profile,LocalCycleStore store)
    {
        var folder=Path.Combine(CycleQueue.Root,profile.Id.ToString("N"),"runs");
        if(!Directory.Exists(folder))return;
        var recovered=0;
        foreach(var file in Directory.EnumerateFiles(folder,"route-*.shops.jsonl"))
        {
            var prefix=file[..^".shops.jsonl".Length];var input=prefix+".input.json";
            if(!File.Exists(input))continue;
            using var plan=JsonDocument.Parse(File.ReadAllText(input));
            if(!plan.RootElement.TryGetProperty("market",out var market)||market.GetString()!=profile.Name)continue;
            var assigned=plan.RootElement.GetProperty("targets").EnumerateArray().ToDictionary(t=>t.GetProperty("traderKey").GetString()!);
            using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            using var reader=new StreamReader(stream);
            while(reader.ReadLine() is {} line)
            {
                try
                {
                    using var document=JsonDocument.Parse(line);var shop=document.RootElement;
                    if(shop.GetProperty("type").GetString()!="shop")continue;
                    var target=ProvisionalCycleTarget(shop);if(target is null)continue;
                    var capture=ExactCycleCapture(shop,target,prefix);if(capture is null)continue;
                    var token=shop.TryGetProperty("verification_revision",out var frozen)&&frozen.ValueKind==JsonValueKind.String?frozen.GetString():null;
                    if(token is null)continue;
                    var revision=assigned.TryGetValue(target.TraderKey,out var planned)&&planned.TryGetProperty("local_revision",out var local)?local.GetInt64():0;
                    var current=store.Target(target.TraderKey);
                    if(revision==0 && current?.VerificationRevision==token)revision=current.Revision;
                    if(store.Commit(target with {Revision=revision,VerificationRevision=token},capture))recovered++;
                }
                catch(JsonException){break;} // Only a complete fsynced event is recoverable.
                catch(InvalidDataException e){Log($"WARNING {profile.Name}: unrecoverable capture event in {file} · {e.Message}");}
            }
        }
        if(recovered>0)Log($"INFO {profile.Name}: recovered {recovered} individual shop results after interrupted operations");
    }
}
