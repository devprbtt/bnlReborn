using System.Linq.Expressions;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
void Check(bool ok,string name) { if(!ok) throw new Exception(name); Console.WriteLine("PASS "+name); }
var ctor=typeof(UnitUpdater).GetConstructors().Single(c=>c.GetParameters().Length==20);
var callbacks=ctor.GetParameters().Select(p=> {
    var invoke=p.ParameterType.GetMethod("Invoke")!;
    return (object)Expression.Lambda(p.ParameterType,Expression.Default(invoke.ReturnType),invoke.GetParameters().Select(a=>Expression.Parameter(a.ParameterType,a.Name))).Compile();
}).ToArray();
var updater=(UnitUpdater)ctor.Invoke(callbacks);
var db=(ServerCatalogue)Databases.Catalogue;
Unit Make(string id,UnitData data,float? lifetime,bool label=true) {
    var card=new CardUnit {Id=id,Data=data,Lifetime=lifetime,Labels=label?[UnitLabel.SupplyBlockbuster]:[]};
    db.Replicate(db.All.Append(card).ToList());
    return new Unit(1,new UnitInit {Key=card.Key,Team=TeamType.Neutral},updater);
}
var before=(ulong)DateTimeOffset.Now.ToUnixTimeMilliseconds();
var crate=Make("fixture_crate",new UnitDataCommon(),60);
var deadline=crate.BlockbusterBreakDeadline;
Check(deadline>=before+60000 && deadline<=(ulong)DateTimeOffset.Now.ToUnixTimeMilliseconds()+60000,"deadline follows CDB lifetime");
Check(crate.GetUpdateData().BombTimeoutEnd==deadline,"snapshot carries deadline");
Thread.Sleep(25);
Check(crate.GetUpdateData().BombTimeoutEnd==deadline,"late-join snapshot preserves original deadline");
using var stream=new MemoryStream();
crate.GetUpdateData().Write(new BinaryWriter(stream)); stream.Position=0;
var received=new UnitUpdate(); received.Read(new BinaryReader(stream));
Check(received.BombTimeoutEnd==deadline,"existing wire format round trips deadline");
Check(Make("fixture_unlimited",new UnitDataCommon(),null).BlockbusterBreakDeadline==null,"no timer without lifetime");
Check(Make("fixture_pickup",new UnitDataPickup(),60).BlockbusterBreakDeadline==null,"loose pickup excluded");
Check(Make("fixture_resource",new UnitDataCommon(),60,false).BlockbusterBreakDeadline==null,"resource drops excluded");
var bomb=Make("fixture_bomb",new UnitDataBomb(),60);
Check(bomb.BlockbusterBreakDeadline==null,"bomb fuse remains separate");
Unit Falling(string id,UnitData data) {
    var card=new CardUnit {Id=id,Data=data,Lifetime=60,Labels=[UnitLabel.SupplyBlockbuster],Movement=new UnitMovementFalling()};
    db.Replicate(db.All.Append(card).ToList());
    var unit=new Unit(2,new UnitInit {Key=card.Key,Team=TeamType.Neutral,Controlled=true},updater);
    unit.UpdateData(new UnitUpdate {MovementActive=true});
    return unit;
}
var dropping=Falling("fixture_dropping",new UnitDataCommon());
Check(dropping.GetUpdateData().MovementActive==null,"falling drop snapshot unchanged before landing");
dropping.IsFirstLand=false;
var landed=dropping.GetUpdateData();
Check(landed.MovementActive==false,"landed drop snapshot reports no movement");
using var landedStream=new MemoryStream();
landed.Write(new BinaryWriter(landedStream)); landedStream.Position=0;
var landedReceived=new UnitUpdate(); landedReceived.Read(new BinaryReader(landedStream));
Check(landedReceived.MovementActive==false,"landed movement state round trips");
Check(landedReceived.BombTimeoutEnd==landed.BombTimeoutEnd,"landed snapshot keeps break deadline");
var still=Make("fixture_static",new UnitDataCommon(),60); still.IsFirstLand=false;
Check(still.GetUpdateData().MovementActive==null,"units without falling movement unchanged");
Console.WriteLine("Blockbuster deadline and landing suite passed.");
