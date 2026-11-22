using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;

if(args.Length!=3)throw new ArgumentException("<before JSON> <candidate JSON> <damage CSV>");
int checks=0;
void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
void Close(float actual,float expected,string text)=>Check(MathF.Abs(actual-expected)<.0002f,text);
var before=JsonNode.Parse(File.ReadAllText(args[0]))!.AsArray().ToDictionary(c=>c!["_id"]!.GetValue<string>(),c=>c!);
var after=JsonNode.Parse(File.ReadAllText(args[1]))!.AsArray().ToDictionary(c=>c!["_id"]!.GetValue<string>(),c=>c!);
IEnumerable<JsonObject> Nodes(JsonNode? node)
{
    if(node is JsonObject obj){yield return obj;foreach(var p in obj)foreach(var c in Nodes(p.Value))yield return c;}
    if(node is JsonArray arr)foreach(var item in arr)foreach(var c in Nodes(item))yield return c;
}
var catalogue=(ServerCatalogue)Databases.Catalogue;
catalogue.Replicate(File.ReadAllText(args[0]).Deserialize<List<Card>>()!);
var oldRules=ActionScoreRules.Load();
catalogue.Replicate(File.ReadAllText(args[1]).Deserialize<List<Card>>()!);
var newRules=ActionScoreRules.Load();
Check(oldRules.RolyGoo==10 && newRules.RolyGoo==8,"Roly Goo award reduced by20 percent");
foreach(var property in typeof(ActionScoreRules).GetProperties().Where(p=>p.Name!=nameof(ActionScoreRules.RolyGoo)))
    Check(Equals(property.GetValue(oldRules),property.GetValue(newRules)),"other scoring unchanged: "+property.Name);
Check(GameZone.DeviceScoreReason("unit_device_roly_toxic_waste")=="Goo affected" && GameZone.DeviceScoreReason("block_toxic_waste_rolydevice")=="Goo affected","goo paths map to tuned score reason");

var zone=(GameZone)RuntimeHelpers.GetUninitializedObject(typeof(GameZone));
const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
typeof(GameZone).GetField("_gameInitiator",hidden)!.SetValue(zone,new WaitingArenaInitiator(new CardGameMode()));
var convert=typeof(GameZone).GetMethod("ConvertToDamageData",hidden)!;
var csv=new List<string>{"gear,distance,before_body,after_body,before_critical,after_critical"};
foreach(var id in new[]{"gear_trondson_sonic_staff","gear_trondson_sonic_staff_echo_location","gear_hunter_rifle","gear_hunter_rifle_spot_shot"})
{
    InstEffectDamage Damage(JsonNode card)=>Nodes(card["tools"]![0]!["hit_effect"]).Single(n=>n["type"]?.GetValue<string>()=="damage").ToJsonString().Deserialize<InstEffectDamage>()!;
    var old=Damage(before[id]);var next=Damage(after[id]);
    Check(old.Damage!.PlayerDamage==next.Damage!.PlayerDamage && old.CritModifier==next.CritModifier,"close damage and critical multiplier unchanged "+id);
    float At(InstEffectDamage effect,float distance,bool crit)=>( (DamageData)convert.Invoke(zone,[effect.Damage,new Vector3(distance,0,0),Vector3.Zero,null,false,crit,effect.CritModifier,effect.Falloff])!).EnemyDamage;
    float previous=float.PositiveInfinity;
    for(int distance=0;distance<=100;distance++)
    {
        var oldD=At(old,distance,false);var newD=At(next,distance,false);
        Check(float.IsFinite(newD) && newD>=0 && newD<=oldD+.0001f,"never increases ranged damage "+id+" at "+distance);
        Check(newD<=previous+.0001f,"monotonic range curve "+id);previous=newD;
        if(distance<=next.Falloff!.MaxDamageRange)Close(newD,oldD,"close damage retained");
        if(distance>next.Falloff.MaxDamageRange && distance<=old.Falloff!.MaxRange)Check(newD<oldD,"requested long range nerf "+id);
        if(distance%5==0)csv.Add(FormattableString.Invariant($"{id},{distance},{oldD},{newD},{At(old,distance,true)},{At(next,distance,true)}"));
    }
}
File.WriteAllLines(args[2],csv);
var updater=(UnitUpdater)RuntimeHelpers.GetUninitializedObject(typeof(UnitUpdater));
updater=updater with {OnUnitInit=(_,_)=>{},OnUnitUpdate=(_,_,_)=>{}};
var ally=new Unit(2,new UnitInit {Key=new Key("unit_hero_engineer"),PlayerId=2,Team=TeamType.Team1,Transform=new ZoneTransform()},updater);
var enemy=new Unit(3,new UnitInit {Key=new Key("unit_hero_engineer"),PlayerId=3,Team=TeamType.Team2,Transform=new ZoneTransform()},updater);
var healer=new Unit(1,new UnitInit {Key=new Key("unit_hero_doc_eliza"),PlayerId=1,Team=TeamType.Team1,Transform=new ZoneTransform()},updater);
foreach(var (id,factor) in new[]{("gear_doc_eliza_bubble_gun",.8f),("gear_doc_eliza_bubble_gun_beautiful_bubbles",.8f),("gear_magnus_static_gloves",1.125f)})
{
    var oldHeals=Nodes(before[id]["tools"]).Where(n=>n["type"]?.GetValue<string>()=="heal" && n["player_heal"]?.GetValue<float>()>0).ToArray();
    var newHeals=Nodes(after[id]["tools"]).Where(n=>n["type"]?.GetValue<string>()=="heal" && n["player_heal"]?.GetValue<float>()>0).ToArray();
    Check(oldHeals.Length==newHeals.Length && newHeals.Length>0,"all heal branches covered "+id);
    for(int i=0;i<newHeals.Length;i++)
    {
        var heal=newHeals[i].ToJsonString().Deserialize<InstEffectHeal>()!;
        Close(heal.PlayerHeal,oldHeals[i]["player_heal"]!.GetValue<float>()*factor,"correct heal tuning "+id);
        Check(ally.DoesEffectApply(heal.Targeting!,healer) && !enemy.DoesEffectApply(heal.Targeting!,healer) && heal.Targeting!.IgnoreCaster,"ally-only non-self healing unchanged");
        ally.UpdateData(new UnitUpdate{Health=10});
        float gained=ally.AddHealth(heal.PlayerHeal,out _,new UnitSource(healer));
        Close(gained,heal.PlayerHeal,"native effective HP gain matches tuned heal");
    }
}
var shield=((ConstEffectBuff)new Key("effect_status_shielded_kinetic_shield").GetCard<CardEffect>()!.Effect!).Buffs![BuffType.Shield];
Close(shield,.3f,"shared teal shield tier is30 percent");
foreach(var card in catalogue.All.OfType<CardEffect>())
{
    if(card.Effect is not ConstEffectBuff {Buffs:{} buffs} || !buffs.TryGetValue(BuffType.Shield,out var value))continue;
    Check(MathF.Abs(value-.375f)>.0001f,"no gameplay shield retains the retired tier: "+card.Id);
    if(card.Id is not ("effect_status_shielded_kinetic_shield" or "effect_hero_roly_emplacement_mode_rolling_wild"))
        Check(JsonNode.DeepEquals(before[card.Id!],after[card.Id!]),"other shield sources preserved: "+card.Id);
}
typeof(Unit).GetField("_buffs",hidden)!.SetValue(ally,new Dictionary<BuffType,float>{{BuffType.Shield,shield}});
Close(ally.DamageTaken(100),70f,"native direct shield mitigation");
Close(ally.SplashDamageTaken(100),70f,"native splash shield mitigation");
var rolling=(ConstEffectBuff)new Key("effect_hero_roly_emplacement_mode_rolling_wild").GetCard<CardEffect>()!.Effect!;
Close(rolling.Buffs![BuffType.Shield],shield,"Rolling Wild matches Sweet Science shield tier");
typeof(Unit).GetField("_buffs",hidden)!.SetValue(ally,rolling.Buffs);
Close(ally.DamageTaken(100),70f,"Rolling Wild native direct damage mitigation");
Close(ally.SplashDamageTaken(100),70f,"Rolling Wild native splash mitigation");
Close(rolling.Buffs[BuffType.RunSpeed],-.4f,"Rolling Wild retains sixty percent movement speed");
Check(JsonNode.DeepEquals(before["effect_hero_roly_emplacement_mode"],after["effect_hero_roly_emplacement_mode"]),"ordinary Roly shield unchanged");
Check(after["ability_roly_emplacement_mode_rolling_wild"]["charges"]!["charge_cooldown"]!.GetValue<int>()==45,
    "shield downside replaces seven-second cooldown penalty");
Check(after["perk_hero_roly_rolling_wild"]["description"]!["text"]!.GetValue<string>().Contains("30%") &&
    !after["perk_hero_roly_rolling_wild"]["description"]!["text"]!.GetValue<string>().Contains("7 seconds"),"perk explains new downside");
var cannon=after["gear_roly_tank_ball_cannon"];
var cannonHit=cannon["tools"]![1]!["hit_effect"]!.ToJsonString().Deserialize<InstEffectSplashDamage>()!;
var shellDamage=(DamageData)convert.Invoke(zone,[cannonHit.Damage,Vector3.Zero,Vector3.Zero,null,false,false,1f,null])!;
Close(shellDamage.EnemyDamage*cannon["tools"]![1]!["bullet"]!.AsArray().Count,32,"native Roly RMB twin-shell damage totals32");
Check(JsonNode.DeepEquals(before["gear_roly_tank_ball_cannon"]["tools"]![0],cannon["tools"]![0]),"Roly primary fire unchanged");
Close(cannonHit.Damage!.WorldDamage,18,"Roly RMB terrain damage per shell unchanged");
var newCards=after.Keys.Except(before.Keys).ToHashSet();
Check(newCards.Count==0 || newCards.SetEquals([Unit.TonyBuildCastEffectId,"effect_hero_engineer_bfs_turret_cast"]),
    "combined rollout adds only the two reviewed Tony wrappers");
Check(after.Keys.Where(k=>!newCards.Contains(k)).SequenceEqual(before.Keys),"existing catalogue cards preserved");
Check(new Key("unit_time_assault_original_device_eliza_acid_trap").GetCard<CardUnit>()!.LandmineUnitDataForTest()==2,"original Time Assault traps remain isolated");
Console.WriteLine($"SIX_HERO_BALANCE PASS {checks} checks; actual engine damage curves saved");

static class Helpers { public static float LandmineUnitDataForTest(this CardUnit card)=>((UnitDataLandmine)card.Data!).TriggerRadius; }
