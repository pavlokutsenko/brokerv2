using PriceCheck.Collector.Models;
using PriceCheck.Collector.Runtime.Radar;

static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
var start=DateTimeOffset.UtcNow;
var store=new RadarEntityStore();
store.SetObservationZone(new(100,100,500),new(800,100,0),false);
store.Apply(new CharacterPacket(7," Shop ","",1,123,456,0),start);
Check(store.Snapshot(42,null,new(800,100,0)).Traders.Count==1,"Reader accumulates before collection starts.");
store.Apply(new CharacterPacket(7,"Shop","",0,123,456,0),start.AddSeconds(1),800,100);
store.Apply(new MovePacket(7,124,457,0),start.AddSeconds(2));
store.SetObservationZone(new(100,100,500),new(100,100,0),true);
var closed=store.Snapshot(42,null,new(100,100,0)).ClosedTraders.Single();
Check(closed.StateObservedAtUtc==start.AddSeconds(1) && closed.StateObservedPlayerX==800,
    "Moving into center or receiving Move cannot refresh an outside closure.");
store.Apply(new CharacterPacket(7,"Shop","",1,124,457,0),start.AddSeconds(3));
var reopened=store.Snapshot(42,null,new(100,100,0)).Traders.Single();
Check(reopened.LastClosedAtUtc==start.AddSeconds(1) && reopened.LastReopenedAtUtc==start.AddSeconds(3),
    "Fast close/reopen remains detectable even when UI never saw the closed snapshot.");
store.Apply(new CharacterPacket(7,"Shop","",1,124,457,0),start.AddSeconds(4));
Check(store.Snapshot(42,null,null).Traders.Single().TradeRevision==reopened.TradeRevision,
    "Repeated open packets do not create a new verification generation.");
store.Apply(new DeletePacket(7),start.AddSeconds(5));
var hidden=store.Snapshot(42,null,null);
Check(hidden.ClosedTraders.Count==0 && hidden.Traders.Single().Name=="Shop" && !hidden.Traders.Single().IsVisible,
    "Knownlist loss retains identity and coordinates, never closes a shop.");
store.Apply(new CharacterPacket(8,"Shop","",1,124,457,0),start.AddSeconds(6));
Check(store.Snapshot(42,null,null).Traders.Single().LastReopenedAtUtc==reopened.LastReopenedAtUtc,
    "ObjectID rebinding itself cannot create a reopening.");
store.Apply(new CharacterPacket(8,"Shop","",0,124,457,0),start.AddSeconds(7),100,100);
closed=store.Snapshot(42,null,new(100,100,0)).ClosedTraders.Single();
Check(closed.StateObservedPlayerX==100 && closed.StateObservedAtUtc==start.AddSeconds(7),
    "A new explicit center observation carries fresh removal evidence.");
store.Apply(new DeletePacket(8),start.AddSeconds(8));
closed=store.Snapshot(42,null,new(100,100,0)).ClosedTraders.Single();
Check(!closed.IsVisible && closed.StateObservedAtUtc==start.AddSeconds(7),
    "Delete after an explicit close retains that close without refreshing its evidence.");
var fresh=new RadarEntityStore();
foreach(var type in new[]{1,3,8}) fresh.Apply(new CharacterPacket(type,$"Shop{type}","",type,123,456,0),start);
Check(fresh.Snapshot(43,null,null).Traders.All(t=>t.LastReopenedAtUtc is null),
    "A new client accepts all three shop types without treating first sight as reopening.");
Check(!store.Snapshot(42,null,null).IsInsideCenterZone,"A stale player position cannot authorize center confirmation.");
store.SetObservationZone(new(100,100,200),new(301,100,0),true);
Check(!store.Snapshot(42,null,new(301,100,0)).IsInsideCenterZone,"Reduced center radius excludes observer at 201.");
Check(store.Snapshot(42,null,new(300,100,0)).IsInsideCenterZone,"Reduced center radius includes observer at 200.");
var history=new RadarEntityStore();
history.RememberTraderKeys([" FormerShop "]);
Check(history.NeedsClosurePosition("FormerShop"),"Historical nickname requests a current observer position.");
history.Apply(new CharacterPacket(99,"FormerShop","",0,123,456,0),start,100,100);
history.Apply(new CharacterPacket(100,"Stranger","",0,123,456,0),start,100,100);
var historicalClose=history.Snapshot(44,null,new(100,100,0)).ClosedTraders.Single();
Check(historicalClose.Name=="FormerShop" && historicalClose.ObjectId==99 && historicalClose.StateObservedPlayerX==100,
    "First explicit zero state after restart closes known history without carrying an old ObjectID or admitting strangers.");
Console.WriteLine("RADAR_LIFECYCLE_OK early_reader rapid_reopen stable_open no_visibility_close observer_position current_client");

namespace PriceCheck.Collector.Runtime.Radar
{
    internal readonly record struct PlayerPosition(double X,double Y,double Z);
}
