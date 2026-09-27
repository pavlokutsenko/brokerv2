using System.Collections;
using System.Reflection;
using System.Text.Json;
using PriceCheck.Collection;
using PriceCheck.Collector.Models;

internal static class RadarFrameSharingTests
{
    public static void Run()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var module=new CollectionModule(new FakeRadar(),_=>true,new PendingWorker(),()=>{},new(()=>{},Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"))),new FakeMarketTasks());
        var runtime=new ProfileRuntime {Profile=new(){Name="SyntheticSharing-"+Guid.NewGuid().ToString("N"),ServerUrl="http://127.0.0.1:1",City="Giran"}};
        using var fixture=new LocalTestFixture();fixture.Install(module,runtime.Profile);
        typeof(CollectionModule).GetMethod("StartCycle",flags)!.Invoke(module,[runtime]);
        var cycle=((IDictionary)typeof(CollectionModule).GetField("_cycles",flags)!.GetValue(module)!)[runtime.Profile.Id]!;
        var type=cycle.GetType();
        var path=Path.Combine(Path.GetTempPath(),"PriceCheck-radar-frame-"+Guid.NewGuid().ToString("N")+".json");
        File.WriteAllText(path,"{}");type.GetProperty("RadarFile")!.SetValue(cycle,path);
        var write=typeof(CollectionModule).GetMethod("WriteCycleRadarTargets",BindingFlags.Static|BindingFlags.NonPublic)!;
        using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            write.Invoke(null,[cycle,new RadarSnapshot {ProcessId=42}]);
        type.GetProperty("NextRadarWrite")!.SetValue(cycle,DateTimeOffset.MinValue);
        write.Invoke(null,[cycle,new RadarSnapshot {ProcessId=42}]);
        using var frame=JsonDocument.Parse(File.ReadAllText(path));
        if(frame.RootElement.GetProperty("pid").GetInt32()!=42) throw new Exception("Locked radar frame must retry after reader releases it.");
        Console.WriteLine("RADAR_FRAME_SHARING_OK locked_destination_retry no_reader_failure current_pid");
    }
}
