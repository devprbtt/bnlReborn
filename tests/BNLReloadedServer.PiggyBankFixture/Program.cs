using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using System.Linq.Expressions;
int checks=0;
void Check(bool value,string label) { if(!value)throw new Exception(label); checks++; }
Check(PiggyBankBalance.Calculate(100,50,10)==100,"existing payout rate");
Check(PiggyBankBalance.Calculate(0,50,10)==0,"new piggy empty");
Check(PiggyBankBalance.Calculate(900,50,10)==900,"late join uses full lifetime");
Check(PiggyBankBalance.Calculate(10.75,50,10)==10.75f,"fractional payout retained");
foreach(var age in new[]{-1d,double.NaN,double.PositiveInfinity})Check(PiggyBankBalance.Calculate(age,50,10)==0,"invalid age");
foreach(var interval in new[]{0f,-1f,float.NaN,float.PositiveInfinity})Check(PiggyBankBalance.Calculate(100,50,interval)==0,"invalid interval");
foreach(var rate in new[]{-1f,float.NaN,float.PositiveInfinity})Check(PiggyBankBalance.Calculate(100,rate,10)==0,"invalid rate");
for(int age=1;age<1000;age+=13)Check(Math.Abs(PiggyBankBalance.Calculate(age,17,3)-(float)(age*17d/(3*5)))<.001f,"payout parity");
var ctor=typeof(UnitUpdater).GetConstructors().Single(c=>c.GetParameters().Length==20);
var callbacks=ctor.GetParameters().Select(p=> {
 var invoke=p.ParameterType.GetMethod("Invoke")!;
 return (object)Expression.Lambda(p.ParameterType,Expression.Default(invoke.ReturnType),invoke.GetParameters().Select(a=>Expression.Parameter(a.ParameterType,a.Name))).Compile();
}).ToArray();
var updater=(UnitUpdater)ctor.Invoke(callbacks);
var card=new CardUnit {Id="fixture_piggy",Data=new UnitDataPiggyBank {GenerationInterval=10,ResourcePerInterval=50}};
((ServerCatalogue)Databases.Catalogue).Replicate([card]);
var unit=new Unit(100,new UnitInit {Key=card.Key,Team=TeamType.Team1,OwnerId=42},updater);
unit.CreationTime=DateTimeOffset.Now.AddSeconds(-900);unit.Resource=0;
var snapshot=unit.GetUpdateData();
Check(snapshot.Resource is >=900 and <902,"late-join snapshot computes full lifetime instead of stale cached resources");
using(var wire=new MemoryStream()) {
 snapshot.Write(new BinaryWriter(wire));wire.Position=0;
 var decoded=UnitUpdate.ReadRecord(new BinaryReader(wire));
 Check(decoded.Resource==snapshot.Resource,"balance survives existing UnitUpdate wire protocol");
}
unit.CreationTime=DateTimeOffset.Now.AddSeconds(1);
Check(unit.GetUpdateData().Resource==0,"empty balance explicitly included in snapshot");
Console.WriteLine($"PASS {checks} Piggy Bank balance checks.");
