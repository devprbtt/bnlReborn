using System.Linq.Expressions;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;

void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
var ctor = typeof(UnitUpdater).GetConstructors().Single(c => c.GetParameters().Length == 20);
var callbacks = ctor.GetParameters().Select(p => {
 var invoke = p.ParameterType.GetMethod("Invoke")!;
 var parameters = invoke.GetParameters().Select(a => Expression.Parameter(a.ParameterType, a.Name)).ToArray();
 return (object)Expression.Lambda(p.ParameterType, Expression.Default(invoke.ReturnType), parameters).Compile();
}).ToArray();
var messages = new List<ManeuverPull>();
callbacks[Array.FindIndex(ctor.GetParameters(), p => p.ParameterType == typeof(OnPull))] = new OnPull((unit, pull) => messages.Add(pull));
var updater = (UnitUpdater)ctor.Invoke(callbacks);
var db = (ServerCatalogue)Databases.Catalogue;
db.Replicate(System.Text.Json.JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]), BNLReloadedServer.ProtocolHelpers.JsonHelper.DefaultSerializerSettings)!);
var heroCard = new CardUnit { Id="fixture_hero", Data=new UnitDataPlayer() };
db.UpdateCard(heroCard);
Unit Hero(uint id, TeamType team) => new(id, new UnitInit { Key=heroCard.Key, Team=team, PlayerId=id }, updater);

var well = Hero(1, TeamType.Team1);
var stance = Hero(2, TeamType.Team1);
var target = Hero(3, TeamType.Team2);
ConstEffectInfo Pull(string id, float force) {
 var card = new CardEffect { Id=id, Effect=new ConstEffectPull { Force=force, BindToUnit=true } };
 db.UpdateCard(card); return new ConstEffectInfo(card.Key);
}
var gravity = Pull("fixture_gravity", 6);
var strong = Pull("fixture_stance", 20);
var weak = Pull("fixture_weak", 5);
void Add(ConstEffectInfo effect, Unit source) => target.AddEffect(effect, source.Team, source.GetSelfSource());
void Remove(ConstEffectInfo effect, Unit source) => target.RemoveEffect(effect, source.Team, source.GetSelfSource());
void Expect(uint? source, float force, string description) {
 var last=messages.Last();
 Check(source.HasValue ? last.Enabled && last.OriginUnitId==source && last.Force==force : !last.Enabled,description);
}
Add(gravity,well); Expect(well.Id,6,"well starts pulling");
Add(strong,stance); Expect(stance.Id,20,"stronger stance takes priority");
messages.Clear(); Remove(strong,stance);
Expect(well.Id,6,"well resumes immediately when stance ends");
Check(messages.Count==1 && messages.All(m=>m.Enabled),"handoff does not send a stop or wait three seconds");
Add(weak,stance); messages.Clear(); Remove(weak,stance);
Check(messages.Count==0,"removing a weaker pull does not cancel the well");
Remove(gravity,well); Expect(null,0,"last pull removal stops pulling");
Add(weak,stance); Expect(stance.Id,5,"new weaker pull is not blocked by stale force history");
Remove(weak,stance);
Add(gravity,well); Add(gravity,stance); messages.Clear(); Remove(gravity,well);
Expect(stance.Id,6,"same effect key transfers to its remaining source");
Check(messages.Count==1,"same-key transfer sends exactly one update");
Remove(gravity,stance); Expect(null,0,"same-key final source stops");
Add(gravity,well); Add(strong,stance); messages.Clear();
target.RemoveEffects([gravity,strong],TeamType.Team1,null,true);
Check(messages.Count==1 && !messages[0].Enabled,"batch removal stops once without transient handoff");
Add(gravity,well); Add(strong,stance); messages.Clear(); Remove(gravity,well);
Check(messages.Count==0,"removing inactive well preserves stronger stance");
Remove(strong,stance); Expect(null,0,"stance stops when no well remains");
Add(gravity,well); target.AddEffects([gravity],stance.Team,stance.GetSelfSource()); messages.Clear();
target.RemoveEffects([gravity],well.Team,well.GetSelfSource());
Expect(stance.Id,6,"batched same-key source changes reconcile");
Remove(gravity,stance);
var timedCard=new CardEffect { Id="fixture_timed_stance", Duration=1, Effect=new ConstEffectPull { Force=20, BindToUnit=true } };
db.UpdateCard(timedCard);
Add(gravity,well); Add(new ConstEffectInfo(timedCard.Key),stance); messages.Clear();
target.PurgeEffects(false,true);
Expect(well.Id,6,"purging timed stance restores persistent well");
Check(messages.Count==1 && messages[0].Enabled,"purge handoff has no disabled update");
Remove(gravity,well);
Add(new ConstEffectInfo(new Key("effect_device_fan")),well);
Expect(well.Id,-6,"negative-force fan still works");
Remove(new ConstEffectInfo(new Key("effect_device_fan")),well);
var realWell=new ConstEffectInfo(new Key("effect_device_gravity_well"));
var realStance=new ConstEffectInfo(new Key("effect_hero_boxer_graviton_pull"), (ulong?)1);
Add(realWell,well); Add(realStance,stance); messages.Clear();
typeof(Unit).GetMethod("RemoveExpiredEffects", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(target,null);
Expect(well.Id,6,"real catalogue stance expiry restores real gravity well");
Check(messages.Count==1 && messages[0].Enabled,"real expiry sends direct well handoff");
Console.WriteLine("Pull overlap regression suite passed.");
