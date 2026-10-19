using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;

int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
void Set(object o,string name,object value)=>o.GetType().GetField(name,flags)!.SetValue(o,value);
var cards=JsonSerializer.Deserialize<List<CardEffect>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"cards.json")),JsonHelper.DefaultSerializerSettings)!;
foreach(var card in cards) card.Key=new Key(card.Id!);
var icon=cards.Single(c=>c.Id=="effect_hero_trondson_heigh_ho_buff_icon");
Check(icon.SplashBlockDamageBonus==.5f,"CDB bonus parses as 50 percent");
foreach(var c in cards.Where(c=>c.Effect is ConstEffectAura)){
    var aura=(ConstEffectAura)c.Effect!;
    Check(aura.Targeting!.IgnoreCaster && aura.Targeting.AffectedTeam==RelativeTeamType.Friendly && aura.ConstantEffects!.Contains(icon.Key),"both aura variants grant the bonus only to nearby allies");
}
Check(!((ConstEffectSelf)cards.Single(c=>c.Id!.EndsWith("buff_self")).Effect!).ConstantEffects!.Contains(icon.Key),"self buff excludes bonus");
using(var stream=new MemoryStream()){
    icon.Write(new BinaryWriter(stream));stream.Position=0;
    var read=CardEffect.ReadRecord(new BinaryReader(stream));
    Check(stream.Position==stream.Length && read.SplashBlockDamageBonus==0 && read.GuiInfo!=null,"legacy card wire layout remains unchanged");
}
var match=new CardMatch{Id="fixture_match"};
var actor=new CardUnit{Id="fixture_actor",Data=new UnitDataCommon()};
((ServerCatalogue)Databases.Catalogue).Replicate(cards.Cast<Card>().Concat(new Card[]{match,actor}).ToList());
var ctor=typeof(UnitUpdater).GetConstructors().Single(c=>c.GetParameters().Length==20);
var callbacks=ctor.GetParameters().Select(p=>{
    var invoke=p.ParameterType.GetMethod("Invoke")!;
    return (object)Expression.Lambda(p.ParameterType,Expression.Default(invoke.ReturnType),invoke.GetParameters().Select(a=>Expression.Parameter(a.ParameterType,a.Name))).Compile();
}).ToArray();
var unit=new Unit(1,new UnitInit{Key=actor.Key,Team=TeamType.Team1},(UnitUpdater)ctor.Invoke(callbacks));
var zone=(GameZone)RuntimeHelpers.GetUninitializedObject(typeof(GameZone));
var data=(ZoneData)RuntimeHelpers.GetUninitializedObject(typeof(ZoneData));data.MatchKey=match.Key;Set(zone,"_zoneData",data);
var convert=typeof(GameZone).GetMethod("ConvertToDamageData",flags)!;
DamageData Hit(bool splash,bool mining=false,Unit? source=null,DamageFalloff? falloff=null)=>(DamageData)convert.Invoke(zone,new object?[]{new Damage{WorldDamage=100,PlayerDamage=80,ObjectiveDamage=60,Mining=mining},new Vector3(5,0,0),Vector3.Zero,source??unit,splash,false,1f,falloff})!;
var baseline=Hit(true);
Check(baseline.BlockDamage==100,"unbuffed splash baseline");
unit.ActiveEffects=unit.ActiveEffects.Add(new ConstEffectInfo(icon.Key));
var buffed=Hit(true);
Check(buffed.BlockDamage==150,"allied splash does 50 percent more block damage");
Check(buffed with {BlockDamage=100}==baseline,"players, devices and objectives unchanged");
Check(Hit(false).BlockDamage==100 && Hit(true,true).BlockDamage==100,"direct hits and mining excluded");
var falloff=new DamageFalloff{MaxDamageRange=0,MinDamageRange=10,MaxRange=20,ReductionCoeff=0};
Check(Hit(true,falloff:falloff).BlockDamage==75,"existing splash falloff retained");
Set(unit,"_buffs",new Dictionary<BuffType,float>{{BuffType.WorldDamage,.2f}});
Check(Math.Abs(Hit(true).BlockDamage-180)<.001f,"50 percent multiplies existing world damage bonuses");
unit.ActiveEffects=unit.ActiveEffects.Add(new ConstEffectInfo(icon.Key));
Check(Math.Abs(Hit(true).BlockDamage-180)<.001f,"duplicate aura card does not stack bonus");
unit.ActiveEffects=unit.ActiveEffects.Clear();
Check(Hit(true).BlockDamage==100,"leaving aura removes bonus");
Console.WriteLine($"Trondson AOE: {checks} checks passed.");


