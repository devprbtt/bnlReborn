using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;
using Timer = System.Timers.Timer;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
void Set(object obj, string field, object? value, Type? type = null) =>
    (type ?? obj.GetType()).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
object? Get(object obj, string field) => obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj);
void Fire(GameZone zone, Timer timer) => typeof(GameZone).GetMethod("OnSupplyTimerElapsed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(zone, [timer, null]);
var resource = new CardUnit { Id = "fixture_resource", Data = new UnitDataCommon() };
var bb = new CardUnit { Id = "fixture_bb", Data = new UnitDataCommon() };
var marker = new CardUnit { Id = "fixture_marker", Labels = [UnitLabel.DropPointBlockbuster, UnitLabel.DropPointResource], Data = new UnitDataCommon() };
var match = new CardMatch { Id = "fixture_supply_match", SupplyLogic = new() { SpawnHeight = 100 } };
((ServerCatalogue)Databases.Catalogue).Replicate([resource, bb, marker, match]);
SupplySequenceItem Item(CardUnit card) => new() { SupplyUnitKey = card.Key, DropPointLabel = card == bb ? UnitLabel.DropPointBlockbuster : UnitLabel.DropPointResource, Seconds = 150 };
match.SupplyLogic.Sequence = [Item(resource), Item(resource), Item(bb)];
match.SupplyLogic.RepeatSequence = [Item(bb), Item(resource)];
var spawned = new List<UnitInit>();
var updater = (UnitUpdater)RuntimeHelpers.GetUninitializedObject(typeof(UnitUpdater));
void SetDelegate(string name, object value) => typeof(UnitUpdater).GetProperty(name)!.SetValue(updater, value);
SetDelegate("OnUnitInit", (OnUnitInit)((u, init) => spawned.Add(init)));
SetDelegate("OnUnitUpdate", (OnUnitUpdate)((u, update, unbuffered) => { }));
SetDelegate("GetTeamEffects", (GetTeamEffects)(_ => []));
var announcements = new List<ZoneUpdate>();
var data = new ZoneData(new ZoneUpdater(announcements.Add)) { MatchKey = match.Key, GameModeKey = Key.None, MapData = null!, BlocksData = null! };
var zone = (GameZone)RuntimeHelpers.GetUninitializedObject(typeof(GameZone));
var queue = Channel.CreateUnbounded<Action>();
Set(zone, "_updateActions", queue, typeof(Updater));
Set(zone, "_queuedActionSources", new ConcurrentDictionary<MethodInfo, int>());
Set(zone, "_zoneData", data);
Set(zone, "_defaultUnitUpdater", updater);
Set(zone, "_gameInitiator", DispatchProxy.Create<IGameInitiator, Initiator>());
var units = new Dictionary<uint, Unit>();
units[1] = new Unit(1, new UnitInit { Key = marker.Key, Transform = new ZoneTransform { Position = new Vector3(134, 14, 13) } }, updater);
spawned.Clear();
using var timer = new Timer(150000) { AutoReset = false };
Set(zone, "_supplyTimer", timer);
data.UpdateSupplyTime(match.SupplyLogic.Sequence[0], new Vector3(134, 14, 13));
// Deliberately leave _units unset until the queue runs: no timer-thread state access is allowed.
Fire(zone, timer);
Check(announcements.Count == 1 && spawned.Count == 0 && (int)Get(zone, "_supplyTimes")! == 0, "timer callback only queues work");
Set(zone, "_units", units);
void Drain(bool expectRearm = true) { Check(queue.Reader.TryRead(out var action), "supply action queued"); action!(); if (expectRearm && Get(zone, "_supplyTimer") is Timer active) { Check(active.Enabled, "next drop timer rearmed"); active.Stop(); } }
Drain();
Check(spawned.Count == 1 && spawned[0].Key == resource.Key && spawned[0].Transform!.Position.Y == 114, "resource drop created at configured height");
Check(announcements.Count == 2 && data.SupplyInfo.NextSupplyDrop == resource.Key, "next resource announced");
foreach (var expected in new[] { bb.Key, bb.Key, resource.Key, bb.Key })
{
    Fire(zone, timer); Drain();
    Check(data.SupplyInfo.NextSupplyDrop == expected, "sequence advances through BBs into repeating drops");
}
Check(spawned.Count == 5 && spawned.Count(s => s.Key == bb.Key) == 2, "all due units actually spawn");
// A missing map marker skips that drop without killing the remaining schedule.
data.SupplyInfo.Position = null;
var before = spawned.Count;
Fire(zone, timer); Drain();
Check(spawned.Count == before && announcements.Count == 7, "missing marker does not stall schedule");
// A queued callback must not act on a replacement timer.
using var stale = new Timer(150000);
Fire(zone, stale); Drain(false);
Check(spawned.Count == before && announcements.Count == 7, "stale timer ignored");
// Match completion may occur while the callback waits on the queue.
Fire(zone, timer);
data.MatchEnded = true;
Drain();
Check(spawned.Count == before && Get(zone, "_supplyTimer") is null, "no drops or rearming after match end");
// No repeat sequence: final drop stops the timer cleanly.
data.MatchEnded = false;
match.SupplyLogic.RepeatSequence = null;
Set(zone, "_supplyTimes", 2);
using var last = new Timer(150000);
Set(zone, "_supplyTimer", last);
data.UpdateSupplyTime(Item(bb), new Vector3(134, 14, 13));
Fire(zone, last); Drain();
Check(Get(zone, "_supplyTimer") is null && spawned.Count == before + 1, "finite sequence ends after final drop");
// Repeat-only configurations start at repeat[0], then advance and wrap.
match.SupplyLogic.Sequence = null;
match.SupplyLogic.RepeatSequence = [Item(resource), Item(bb)];
Set(zone, "_supplyTimes", 0);
using var repeatOnly = new Timer(150000);
Set(zone, "_supplyTimer", repeatOnly);
data.UpdateSupplyTime(Item(resource), new Vector3(134, 14, 13));
Fire(zone, repeatOnly); Drain();
Check(data.SupplyInfo.NextSupplyDrop == bb.Key, "repeat-only schedule advances");
Fire(zone, repeatOnly); Drain();
Check(data.SupplyInfo.NextSupplyDrop == resource.Key, "repeat-only schedule wraps");
// BB history belongs to the marker location, across tiers and intervening resources.
var choose = typeof(GameZone).GetMethod("GetSupplyPosition", BindingFlags.Instance | BindingFlags.NonPublic)!;
Vector3? Choose(SupplySequenceItem item) => (Vector3?)choose.Invoke(zone, [item]);
var a = new Vector3(134, 14, 13);
var b = new Vector3(160, 14, 13);
var c = new Vector3(190, 14, 13);
Unit Marker(uint id, Vector3 position) => new(id, new UnitInit { Key = marker.Key, Transform = new ZoneTransform { Position = position } }, updater);
Check(Choose(Item(bb)) == a && Choose(Item(bb)) == a, "single-location maps keep delivering BBs");
units[2] = Marker(2, b);
Set(zone, "_lastBlockbusterDropPoint", a);
var alternating = true;
for (int i = 0; i < 40; i++) alternating &= Choose(Item(bb)) == (i % 2 == 0 ? b : a);
Check(alternating, "two distinct BB locations always alternate");
Set(zone, "_lastBlockbusterDropPoint", a);
Choose(Item(resource));
Check(Choose(Item(bb)) == b, "resource drops do not reset the previous BB location");
units[3] = Marker(3, b);
Set(zone, "_lastBlockbusterDropPoint", b);
Check(Choose(Item(bb)) == a, "duplicate markers at the previous spot cannot repeat that spot");
units[4] = Marker(4, c);
var distinct = true;
var previous = (Vector3)Get(zone, "_lastBlockbusterDropPoint")!;
for (int i = 0; i < 500; i++)
{
    var next = Choose(Item(bb));
    distinct &= next.HasValue && next != previous && (next == a || next == b || next == c);
    previous = next!.Value;
}
Check(distinct, "three-location BB draws never repeat the previous spot");
match.SupplyLogic.RandomPosOffset = 2;
var offsetSafe = true;
for (int i = 0; i < 100; i++)
{
    var previousAnchor = (Vector3)Get(zone, "_lastBlockbusterDropPoint")!;
    var landed = Choose(Item(bb))!.Value;
    var anchor = (Vector3)Get(zone, "_lastBlockbusterDropPoint")!;
    offsetSafe &= anchor != previousAnchor && Math.Abs(landed.X - anchor.X) <= 2 &&
                  Math.Abs(landed.Z - anchor.Z) <= 2 && landed.Y == anchor.Y;
}
Check(offsetSafe, "random landing offsets preserve BB marker exclusion and bounds");
units.Clear();
var remembered = Get(zone, "_lastBlockbusterDropPoint");
Check(Choose(Item(bb)) == null && Equals(remembered, Get(zone, "_lastBlockbusterDropPoint")), "missing markers preserve BB history without inventing a drop");
units[1] = Marker(1, a); units[2] = Marker(2, b);
Set(zone, "_lastBlockbusterDropPoint", c);
Check(Choose(Item(bb)).HasValue, "removed previous marker does not prevent selecting remaining locations");
queue.Writer.Complete();
using var closed = new Timer(150000);
Fire(zone, closed);
Check(!queue.Reader.TryRead(out _), "closed zone rejects callback");
Console.WriteLine($"SUPPLY_SCHEDULE PASS {checks} checks");
public class Initiator : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.ReturnType == typeof(bool) ? false : null;
}
