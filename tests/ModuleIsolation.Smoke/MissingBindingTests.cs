using System.Reflection;
using System.Collections;
using System.Text.Json;
using PriceCheck.Collection;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

internal static class MissingBindingTests
{
    public static async Task Run()
    {
        var folder=Path.Combine(Path.GetTempPath(),"PriceCheck-missing-binding-"+Guid.NewGuid().ToString("N"));
        var module=new CollectionModule(new FakeRadar(),_=>true,new PendingWorker(),()=>{},
            new ServerUploadOutbox(()=>{},folder),new FakeMarketTasks());
        var runtime=new ProfileRuntime {Profile=new(){Name="Synthetic",City="Giran"}};
        var logs=new List<string>();module.Message+=logs.Add;
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        typeof(CollectionModule).GetMethod("StartCycle",flags)!.Invoke(module,[runtime]);
        var cycles=(IDictionary)typeof(CollectionModule).GetField("_cycles",flags)!.GetValue(module)!;
        var cycle=cycles[runtime.Profile.Id]!;var type=cycle.GetType();
        type.GetProperty("BrokerBatch")!.SetValue(cycle,"fixture");
        var remaining=(HashSet<string>)type.GetProperty("RemainingVisitKeys")!.GetValue(cycle)!;
        remaining.Add("SHOP");
        var claim=typeof(CollectionModule).GetMethod("ClaimCycleTargetsAsync",flags)!;
        var result=await (Task<IReadOnlyList<CycleTarget>?>)claim.Invoke(module,[cycle,new RadarSnapshot()])!;
        if(result!.Count!=0 || remaining.Count!=0) throw new Exception("Missing binding must skip only this pass.");
        using var release=JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(folder,"*.ready").Single()));
        if(release.RootElement.GetProperty("body").GetProperty("jobs")[0].GetProperty("error").ValueKind!=JsonValueKind.Null)
            throw new Exception("Missing runtime binding must not increment server attempts or defer a job.");
        if(!logs.Any(s=>s.Contains("Shop") && s.Contains("No runtime object") && s.Contains("remains ready")))
            throw new Exception("Binding skip must explain its actual cause.");
        Console.WriteLine("MISSING_BINDING_OK ready_release bounded_pass diagnostic");
    }
}
