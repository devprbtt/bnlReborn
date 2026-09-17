using System.Linq.Expressions;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
var updates = new List<(UnitUpdate Data, bool Immediate)>();
var ctor = typeof(UnitUpdater).GetConstructors().Single();
var callbacks = ctor.GetParameters().Select(p => {
    if (p.ParameterType == typeof(OnUnitUpdate)) return (object)(OnUnitUpdate)((_, u, immediate) => updates.Add((u, immediate)));
    var invoke = p.ParameterType.GetMethod("Invoke")!;
    var parameters = invoke.GetParameters().Select(a => Expression.Parameter(a.ParameterType, a.Name)).ToArray();
    return (object)Expression.Lambda(p.ParameterType, Expression.Default(invoke.ReturnType), parameters).Compile();
}).ToArray();
var updater = (UnitUpdater)ctor.Invoke(callbacks);
var catalogue = (ServerCatalogue)Databases.Catalogue;
using var json = JsonDocument.Parse(File.ReadAllText(args[0]));
var ids = new HashSet<string> { "gear_boxer_blitz_stance", "gear_boxer_graviton_stance", "effect_hero_boxer_reactive_ammo" };
var hero = new CardUnit { Id = "fixture_ss_ammo", Data = new UnitDataPlayer() };
catalogue.Replicate(json.RootElement.EnumerateArray().Where(c => ids.Contains(c.GetProperty("_id").GetString()!))
    .Select(c => JsonSerializer.Deserialize<Card>(c.GetRawText(), JsonHelper.DefaultSerializerSettings)!).Append(hero).ToList());
var blitz = new Key("gear_boxer_blitz_stance");
var graviton = new Key("gear_boxer_graviton_stance");
var reactive = (ConstEffectOnDamageTaken)catalogue.GetCard<CardEffect>(new Key("effect_hero_boxer_reactive_ammo"))!.Effect!;
var amount = ((InstEffectAddAmmo)reactive.Effect!).Amount;
var unit = new Unit(1, new UnitInit { Key = hero.Key, PlayerId = 1, Team = TeamType.Team1, Gears = [blitz, graviton] }, updater);
void State(float mag, float pool) {
    unit.UpdateData(new UnitUpdate { CurrentGear = blitz, Ammo = new() {
        [blitz] = [new Ammo { Index=0, Mag=mag, Pool=pool }],
        [graviton] = [new Ammo { Index=0, Mag=0, Pool=3 }]
    }}); updates.Clear();
}

State(0, 4);
unit.AddAmmo(amount);
Check(updates.Single().Data.Ammo![blitz].Single().Mag == null, "reactive reserve update never carries a stale magazine");
Check(unit.GetGearByKey(blitz)!.Ammo[0].Mag == 0 && unit.GetGearByKey(blitz)!.Ammo[0].Pool == 4.25f, "reactive reserve gain retained");
unit.ReloadAmmo();
Check(updates.All(u => !u.Immediate), "reload and reactive updates share ordered stream");
float clientMag=0, clientPool=4;
foreach(var update in updates) {
 var a=update.Data.Ammo![blitz].Single();
 if(a.Mag.HasValue)clientMag=a.Mag.Value;
 if(a.Pool.HasValue)clientPool=a.Pool.Value;
}
Check(clientMag==2 && clientPool==2.25f && unit.CurrentGear!.Ammo[0].Mag==clientMag, "reactive-before-reload ends synchronized");
updates.Clear(); unit.AddAmmo(amount);
Check(updates.Single().Data.Ammo![blitz].Single().Mag==null, "reactive-after-reload cannot overwrite loaded charges");
updates.Clear(); unit.AddAmmoPercent(.1f);
Check(updates.Single().Data.Ammo![blitz].Single().Mag==null, "percentage reserve refill cannot overwrite loaded charges");
State(0,4); unit.ReloadAmmo(); unit.SetGear(graviton); unit.ReloadAmmo(); unit.SetGear(blitz);
Check(unit.GetGearByKey(blitz)!.Ammo[0].Mag==2 && unit.GetGearByKey(graviton)!.Ammo[0].Mag==1, "reload/swap preserves both magazines");
var now=DateTimeOffset.UtcNow;
State(2,4);
Check(!unit.DashCharge.Finish(unit,1,now,out _), "end-charge without accepted start rejected");
Check(!unit.DashCharge.Consume(unit,1,now,out _), "cast without accepted charge rejected");
Check(unit.DashCharge.Start(unit,1,now), "full magazine starts dash");
Check(unit.DashCharge.Finish(unit,1,now.AddSeconds(1),out var max) && max, "two charges permit full dash");
Check(unit.DashCharge.Consume(unit,1,now.AddSeconds(1.1),out max) && max, "accepted full dash can cast");
Check(!unit.DashCharge.Consume(unit,1,now.AddSeconds(1.2),out _), "duplicate cast rejected");
State(1,4); unit.DashCharge.Start(unit,1,now);
Check(unit.DashCharge.Finish(unit,1,now.AddSeconds(1),out max) && !max, "one charge cannot authorize two-charge dash");
Check(unit.DashCharge.Consume(unit,1,now.AddSeconds(1.1),out max) && !max, "one-charge short dash allowed");
State(0,4); Check(!unit.DashCharge.Start(unit,1,now), "empty magazine rejects start");
State(2,4); unit.DashCharge.Start(unit,1,now); unit.DashCharge.Finish(unit,1,now.AddSeconds(1),out _);
unit.CurrentGear!.Ammo[0].Mag=1;
Check(!unit.DashCharge.Consume(unit,1,now.AddSeconds(1.1),out _), "full dash rejected when ammo drains after charge approval");
State(2,4); unit.DashCharge.Start(unit,1,now); unit.SetGear(graviton); unit.SetGear(blitz);
Check(!unit.DashCharge.Finish(unit,1,now.AddSeconds(1),out _), "switch away and back invalidates charge");
State(2,4); unit.DashCharge.Start(unit,1,now); unit.DashCharge.Finish(unit,1,now.AddSeconds(1),out _);
Check(!unit.DashCharge.Consume(unit,1,now.AddSeconds(7),out _), "expired cast rejected");
State(2,4); unit.DashCharge.Start(unit,1,now);
Check(!unit.DashCharge.Finish(unit,0,now.AddSeconds(1),out _), "different tool cannot finish charge");
Check(!unit.DashCharge.Start(unit,255,now), "out-of-range tool safely rejected");
Console.WriteLine($"SS ammo repair: {checks} checks passed");
