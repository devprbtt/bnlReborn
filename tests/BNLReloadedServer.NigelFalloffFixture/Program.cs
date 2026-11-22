using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;

int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
void Near(float value,float expected,string label)=>Check(MathF.Abs(value-expected)<.0002f,label);
var before=JsonNode.Parse(File.ReadAllText(args[0]))!.AsArray().ToDictionary(c=>c!["_id"]!.GetValue<string>(),c=>c!);
var after=JsonNode.Parse(File.ReadAllText(args[1]))!.AsArray().ToDictionary(c=>c!["_id"]!.GetValue<string>(),c=>c!);
IEnumerable<JsonObject> Nodes(JsonNode? node)
{
    if(node is JsonObject obj){yield return obj;foreach(var p in obj)foreach(var c in Nodes(p.Value))yield return c;}
    if(node is JsonArray arr)foreach(var item in arr)foreach(var c in Nodes(item))yield return c;
}
((ServerCatalogue)Databases.Catalogue).Replicate(File.ReadAllText(args[1]).Deserialize<List<Card>>()!);
var zone=(GameZone)RuntimeHelpers.GetUninitializedObject(typeof(GameZone));
const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
typeof(GameZone).GetField("_gameInitiator",hidden)!.SetValue(zone,new WaitingArenaInitiator(new CardGameMode()));
var convert=typeof(GameZone).GetMethod("ConvertToDamageData",hidden)!;
float At(InstEffectDamage effect,float distance,bool crit=false)=>((DamageData)convert.Invoke(zone,
    [effect.Damage,new Vector3(distance,0,0),Vector3.Zero,null,false,crit,effect.CritModifier,effect.Falloff])!).EnemyDamage;
var csv=new List<string>{"gear,distance,before_body,after_body,before_critical,after_critical"};
foreach(var id in new[]{"gear_hunter_rifle","gear_hunter_rifle_spot_shot"})
{
    InstEffectDamage Damage(JsonNode card)=>Nodes(card["tools"]![0]).Single(n=>n["type"]?.GetValue<string>()=="damage").ToJsonString().Deserialize<InstEffectDamage>()!;
    var old=Damage(before[id]);var next=Damage(after[id]);
    Near(next.Damage!.PlayerDamage,40,"close body damage remains40");
    Near(next.CritModifier,1.625f,"headshot multiplier preserved");
    Near(At(next,39.99f),At(old,39.99f),"close damage retained below new boundary");
    Check(At(next,40.01f)<At(old,40.01f),"falloff actually starts immediately after40m");
    foreach(var (distance,expected) in new[]{(40f,40f),(50f,37f),(60f,34f),(70f,31f),(80f,28f),(90f,25f),(100f,22f)})
    {
        Near(At(next,distance),expected,"native body damage at"+distance);
        Near(At(next,distance,true),expected*1.625f,"native headshot damage at"+distance);
        Check(At(next,distance)<=At(old,distance)+.0001f,"range tuning cannot increase damage");
        csv.Add(FormattableString.Invariant($"{id},{distance},{At(old,distance)},{At(next,distance)},{At(old,distance,true)},{At(next,distance,true)}"));
    }
    Near(At(next,101),0,"maximum range remains100m");
}
Check(after.Keys.SequenceEqual(before.Keys),"no catalogue cards added or removed");
var changed=after.Keys.Where(id=>!JsonNode.DeepEquals(before[id],after[id])).ToHashSet();
Check(changed.SetEquals(["gear_hunter_rifle","gear_hunter_rifle_spot_shot"]),"only the two rifle variants change");
foreach(var id in changed)
{
    var normalized=after[id].DeepClone();
    Nodes(normalized["tools"]![0]).Single(n=>n["type"]?.GetValue<string>()=="damage")["falloff"]!["max_damage_range"]=50;
    normalized["description"]!["text"]=normalized["description"]!["text"]!.GetValue<string>().Replace("Max Damage Range - 40","Max Damage Range - 50");
    normalized["reticles"]![0]!["preview_damage_range"]=50;
    Check(JsonNode.DeepEquals(before[id],normalized),"all other weapon fields remain unchanged");
}
File.WriteAllLines(args[2],csv);
Console.WriteLine("NIGEL_FALLOFF PASS "+checks+" checks; actual engine damage saved");
