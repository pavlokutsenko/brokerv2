using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using PriceCheck.Collection;
using PriceCheck.Contracts;
using PriceCheck.Collector.Models;

internal static class LocalCenterArrivalTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static async Task Run()
    {
        var now=DateTimeOffset.UtcNow;var center=new MarketZone(0,0,500);
        RadarSnapshot Frame(int pid=510,bool live=true,bool inside=true,double x=0,int age=0)=>new(){ProcessId=pid,
            WorldCharacterDataAvailable=true,LivePlayerPositionAvailable=live,CenterZoneConfigured=true,IsInsideCenterZone=inside,
            PlayerX=x,PlayerY=0,CapturedAtUtc=now.AddSeconds(-age)};
        var accept=typeof(CollectionModule).GetMethod("TryAcceptCenterEndpoint",BindingFlags.Static|BindingFlags.NonPublic)!;
        bool Accept(string reason,string position,RadarSnapshot? radar)
        {
            using var result=JsonDocument.Parse("{\"position\":"+position+"}");
            return (bool)accept.Invoke(null,[result.RootElement,reason,center,radar,510,now,Array.Empty<double>()])!;
        }
        foreach(var reason in new[]{"recovery_unreachable","stalled","time_limit"})
            Check(Accept(reason,"[162,0,0]",Frame()),"Safe route endpoint inside saved center is accepted after fresh current confirmation.");
        foreach(var reason in new[]{"cancelled","position_jump","target_changed_externally","keyboard_interrupt","unknown"})
            Check(!Accept(reason,"[162,0,0]",Frame()),"Cancellation/external jump cannot become successful center arrival.");
        foreach(var bad in new[]{"[501,0]","[1e309,0]","[\"NaN\",0]","[null,0]","[]"})
            Check(!Accept("stalled",bad,Frame()),"Outside or malformed actual endpoint cannot authorize broker.");
        foreach(var radar in new RadarSnapshot?[]{null,Frame(pid:511),Frame(live:false),Frame(inside:false),Frame(x:501),Frame(age:6)})
            Check(!Accept("stalled","[162,0]",radar),"Missing/stale/foreign/outside current radar cannot confirm center.");
        await Scenario("recovery_unreachable",162,false);
        await Scenario("stalled",501,false);
        await Scenario("time_limit",162,true);
        await Scenario("cancelled",162,false);
        Console.WriteLine("LOCAL_CENTER_ARRIVAL_OK safe_inside_endpoint immediate_broker no_delay no_geometry_change reject_cancel_stale_foreign_missing_outside unsafe_command outside_three_failure_limit");
    }
    private static async Task Scenario(string reason,double x,bool unsafeFailure)
    {
        using var fixture=new LocalTestFixture();var runtime=LocalTestFixture.Runtime(510);
        var radar=new FakeRadar{CurrentSnapshot=LocalTestFixture.Radar(510)};
        var worker=new CenterWorker(reason,x,unsafeFailure);
        var module=new CollectionModule(radar,_=>true,worker,()=>{},new(()=>{},Path.Combine(fixture.Root,"status")),new NeverMarketTasks());
        fixture.Install(module,runtime.Profile);var logs=new List<string>();module.Message+=logs.Add;
        await module.AttachAsync(runtime,CancellationToken.None);await module.RefreshAsync(runtime);await module.SetCollectionAsync(runtime,true);
        await LocalTestFixture.Step(module,runtime,radar.CurrentSnapshot);
        var cycle=LocalTestFixture.Cycle(module,runtime);
        if(unsafeFailure)Check(runtime.ClientFault is not null&&LocalTestFixture.Phase(cycle)=="Return to center","Inside endpoint cannot override unsafe native cleanup.");
        else if(reason=="cancelled")Check(!runtime.IsCollectionEnabled&&LocalTestFixture.Phase(cycle)=="Stopped","Cancelled center route stays stopped.");
        else if(x<=500)
        {
            var next=(DateTimeOffset)cycle.GetType().GetProperty("Next")!.GetValue(cycle)!;
            Check(LocalTestFixture.Phase(cycle)=="Broker inventory"&&next==DateTimeOffset.MinValue,"Safe actual center arrival starts broker without thirty-second retry.");
            Check(logs.Any(l=>l.Contains("WARNING")&&l.Contains(reason))&&logs.Any(l=>l.Contains("arrived in saved center")),"Original route reason remains a warning while arrival is explicit.");
            Check(((double[])cycle.GetType().GetProperty("PreviousDestination")!.GetValue(cycle)!)[0]==162,"Accepted actual endpoint is retained rather than unreachable planned destination.");
        }
        else
        {
            Check(LocalTestFixture.Phase(cycle)=="Return to center","Outside endpoint cannot start broker.");
            await LocalTestFixture.Step(module,runtime,radar.CurrentSnapshot);await LocalTestFixture.Step(module,runtime,radar.CurrentSnapshot);
            Check(!runtime.IsCollectionEnabled&&LocalTestFixture.Phase(cycle)=="Stopped","Outside-center three-failure stop remains unchanged.");
        }
        await module.DetachAsync(runtime);
    }
    private sealed class CenterWorker(string reason,double x,bool fail):ICollectionWorker
    {
        public Task RunAsync(ClientSession session,string mode,string output,Action<ProcessStartInfo> configure)
        {
            var start=new ProcessStartInfo();configure(start);
            File.WriteAllText(output,JsonSerializer.Serialize(new{reason,position=new[]{x,0,0},destination=new[]{450,0}}));
            return fail?Task.FromException(new InvalidOperationException("ProcessEvent command bridge is busy: trigger=1 status=0")):Task.CompletedTask;
        }
    }
}
