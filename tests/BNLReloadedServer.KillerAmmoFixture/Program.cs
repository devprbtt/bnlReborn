using System.Linq.Expressions;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;

void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
}

var updates = new List<(UnitUpdate Data, bool Unbuffered)>();
var constructor = typeof(UnitUpdater).GetConstructors().Single(c => c.GetParameters().Length == 20);
var callbacks = constructor.GetParameters().Select(parameter =>
{
    if (parameter.ParameterType == typeof(OnUnitUpdate))
    {
        return (object)(OnUnitUpdate)((_, update, unbuffered) => updates.Add((update, unbuffered)));
    }

    var invoke = parameter.ParameterType.GetMethod("Invoke")!;
    var parameters = invoke.GetParameters()
        .Select(argument => Expression.Parameter(argument.ParameterType, argument.Name)).ToArray();
    return (object)Expression.Lambda(parameter.ParameterType, Expression.Default(invoke.ReturnType), parameters).Compile();
}).ToArray();
var updater = (UnitUpdater)constructor.Invoke(callbacks);

var catalogue = (ServerCatalogue)Databases.Catalogue;
var gearCard = new CardGear
{
    Id = "fixture_killer_ammo_weapon",
    Ammo =
    [
        new AmmoData
        {
            MagSize = 10,
            Pool = new AmmoPool { PoolSize = 100, BaseRegen = 1 }
        }
    ],
    Tools =
    [
        new ToolShot { Ammo = new ToolAmmo { AmmoIndex = 0, Rate = 1 } }
    ]
};
var heroCard = new CardUnit { Id = "fixture_killer_ammo_hero", Data = new UnitDataPlayer() };
catalogue.Replicate([gearCard, heroCard]);

var player = new Unit(1, new UnitInit
{
    Key = heroCard.Key,
    Team = TeamType.Team1,
    PlayerId = 1,
    Gears = [gearCard.Key]
}, updater);
player.UpdateData(new UnitUpdate
{
    CurrentGear = gearCard.Key,
    Ammo = new Dictionary<Key, List<Ammo>>
    {
        [gearCard.Key] = [new Ammo { Index = 0, Mag = 0, Pool = 50 }]
    }
});
updates.Clear();

player.ReloadAmmo(true, 0.5f);

Check(updates.Count == 1, "killer reload emits one authoritative ammo update");
Check(!updates[0].Unbuffered, "killer reload stays ordered on the buffered shot stream");
Check(updates[0].Data.Ammo != null, "killer reload includes ammo data");
var ammo = updates[0].Data.Ammo![gearCard.Key].Single();
Check(ammo.Mag == 5, "last-bullet kill refills half of the clip");
Check(ammo.Pool == 45, "clip refill consumes the expected reserve ammo");
Check(player.CurrentGear!.Ammo[0].Mag == 5, "server ammo state is updated immediately");

Console.WriteLine("Killer Ammo ordering suite passed.");
