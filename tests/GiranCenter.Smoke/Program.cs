using System.Security.Cryptography;
using System.Text.Json;
using PriceCheck.Collection;
using PriceCheck.Collector.Models;
using PriceCheck.Collector.Services;

const double savedX = 82413.61851503256, savedY = 148116.9785946493;
void Require(bool value, string reason) { if (!value) throw new Exception(reason); }
CollectorProfile[] FourProfiles() => [
    new() { Name="Gamma", CenterZonesByCity=new() {["Giran"]=new(){X=savedX,Y=savedY}} },
    new() { Name="Black" }, new() { Name="White" }, new() { Name="Carmine" }];
void CheckPoint(CollectorProfile profile)
{
    var point=CollectionModule.GetCenterZone(profile);
    Require(point is { } value && value.X==savedX && value.Y==savedY && value.Radius==200,"Exact saved Giran center was not reused");
    Require(new ProfileRuntime {Profile=profile}.CenterZoneLabel!="Center not set","UI and collection centers differ");
}

var profiles=FourProfiles();
var before=JsonSerializer.Serialize(profiles);
var dictionaries=profiles.Select(p=>p.CenterZonesByCity).ToArray();
var result=SavedGiranCenter.AssignSharedFallback(profiles);
Require(result==new GiranCenterAssignment(1,3,3),"Four-profile assignment failed");
foreach(var profile in profiles) CheckPoint(profile);
Require(profiles.Select((p,i)=>ReferenceEquals(p.CenterZonesByCity,dictionaries[i])).All(x=>x),"Persisted dictionaries replaced");
Require(profiles.Skip(1).All(p=>p.CenterZonesByCity.Count==0),"Fallback persisted into profiles");
Require(before==JsonSerializer.Serialize(profiles),"Runtime fallback changed serialized configuration");

var starting=FourProfiles().Take(3).ToArray();
SavedGiranCenter.AssignSharedFallback(starting);
var added=new CollectorProfile {Name="Carmine"};
SavedGiranCenter.AssignSharedFallback(starting.Append(added));
CheckPoint(added);Require(added.CenterZonesByCity.Count==0,"New profile created city settings");

profiles[1].CenterZonesByCity["Giran"]=new(){X=savedX+1,Y=savedY};
var own=profiles[1].CenterZonesByCity["Giran"];
result=SavedGiranCenter.AssignSharedFallback(profiles);
Require(result.DistinctSavedCenters==2&&result.ReusedProfiles==0,"Conflicting centers picked an automatic fallback");
Require(ReferenceEquals(SavedGiranCenter.Resolve(profiles[1]),own),"Own valid center overwritten");
Require(CollectionModule.GetCenterZone(profiles[2]) is null&&CollectionModule.GetCenterZone(profiles[3]) is null,"Conflict retained a stale shared center");
profiles[1].CenterZonesByCity.Clear();SavedGiranCenter.AssignSharedFallback(profiles);
foreach(var profile in profiles) CheckPoint(profile);

profiles[0].CenterZonesByCity["Giran"]=new(){X=double.NaN,Y=savedY};
result=SavedGiranCenter.AssignSharedFallback(profiles);
Require(result.DistinctSavedCenters==0&&profiles.All(p=>CollectionModule.GetCenterZone(p) is null),"Nonfinite source accepted");
profiles[1].CenterZonesByCity["Giran"]=new(){X=savedX,Y=savedY};
SavedGiranCenter.AssignSharedFallback(profiles);CheckPoint(profiles[0]);
Require(double.IsNaN(profiles[0].CenterZonesByCity["Giran"].X),"Invalid stored value silently overwritten");
var otherCity=new CollectorProfile{CenterZonesByCity=new(){["Aden"]=new(){X=savedX,Y=savedY}}};
SavedGiranCenter.AssignSharedFallback([otherCity]);
Require(CollectionModule.GetCenterZone(otherCity) is null,"Other-city geometry became Giran center");

var directory=Path.Combine(Path.GetTempPath(),"PriceCheck-giran-center-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var path=Path.Combine(directory,"profiles.json");
    File.WriteAllText(path,JsonSerializer.Serialize(FourProfiles()));
    var hash=SHA256.HashData(File.ReadAllBytes(path));
    var store=new ProfileStore(directory);
    var loaded=await store.LoadAsync();foreach(var profile in loaded)CheckPoint(profile);
    Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),"Loading shared geometry rewrote configuration");
    await store.SaveAsync(loaded);
    using var json=JsonDocument.Parse(File.ReadAllText(path));
    Require(json.RootElement.EnumerateArray().Skip(1).All(p=>!p.TryGetProperty("SharedGiranCenter",out _)&&
        !p.GetProperty("CenterZonesByCity").EnumerateObject().Any()),"Save persisted fallback/new city settings");
}
finally
{
    Require(Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase),"Unsafe test cleanup");
    Directory.Delete(directory,true);
}
Console.WriteLine("GIRAN_CENTER_SMOKE_OK four_profiles new_profile own_center conflict nonfinite Giran_only JSON_unchanged");
