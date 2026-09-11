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
var updater = (UnitUpdater)ctor.Invoke(callbacks);
var db = (ServerCatalogue)Databases.Catalogue;
db.Replicate(System.Text.Json.JsonSerializer.Deserialize<List<Card>>(File.ReadAllText(args[0]), BNLReloadedServer.ProtocolHelpers.JsonHelper.DefaultSerializerSettings)!);
var heroCard = new CardUnit { Id="fixture_hero", Data=new UnitDataPlayer() };
db.UpdateCard(heroCard);
Unit Hero(uint id, TeamType team) => new(id, new UnitInit { Key=heroCard.Key, Team=team, PlayerId=id }, updater);
var tony=Hero(1,TeamType.Team1); var ally=Hero(2,TeamType.Team1); var enemy=Hero(3,TeamType.Team2);
var targeting = new EffectTargeting { IgnoreCaster=true, AffectedTeam=RelativeTeamType.Friendly, AffectedUnits=[UnitType.Player], AffectedLabels=[] };
var buff=new CardEffect { Id="fixture_buff", Positive=true, Effect=new ConstEffectBuff { Targeting=targeting, Buffs=new() { [BuffType.BuildSpeed]=1 } } };
db.UpdateCard(buff);
var info=new ConstEffectInfo(buff.Key);
var aura=new ConstEffectAura { Targeting=targeting, OuterRadius=5, ConstantEffects=[buff.Key] };
Check(!tony.DoesAuraTargetApply(aura,tony),"aura excludes caster");
Check(ally.DoesAuraTargetApply(aura,tony),"aura includes friendly player with empty labels");
Check(!enemy.DoesAuraTargetApply(aura,tony),"aura excludes opponent");
tony.AddEffect(info,tony.Team,tony.GetSelfSource());
Check(tony.ActiveEffects.Count==0,"single buff excludes self");
tony.AddEffects([info],tony.Team,tony.GetSelfSource());
Check(tony.ActiveEffects.Count==0,"nested/batched buff excludes self");
ally.AddEffects([info],tony.Team,tony.GetSelfSource());
Check(ally.ActiveEffects.Contains(info),"nested buff reaches ally");
ally.RemoveEffects([info],tony.Team,tony.GetSelfSource());
Check(!ally.ActiveEffects.Contains(info),"aura exit removes buff");
tony.AddEffect(info,ally.Team,ally.GetSelfSource());
Check(tony.ActiveEffects.Contains(info),"another caster can buff Tony");
tony.AddEffects([info],tony.Team,tony.GetSelfSource());
tony.RemoveEffects([info],ally.Team,ally.GetSelfSource());
Check(!tony.ActiveEffects.Contains(info),"rejected self application leaves no source keeping buff alive");
var auraCard=new CardEffect { Id="fixture_aura", Effect=aura };
db.UpdateCard(auraCard);
tony.AddEffect(new ConstEffectInfo(auraCard.Key),tony.Team,tony.GetSelfSource());
Check(tony.AuraEffects.ContainsKey(aura),"ignore_caster aura remains active on emitter");
targeting.IgnoreCaster=false;
Check(tony.DoesAuraTargetApply(aura,tony),"self allowed when ignore_caster disabled");
tony.AddEffect(info,tony.Team,tony.GetSelfSource());
Check(tony.ActiveEffects.Contains(info),"self buff allowed when flag disabled");

