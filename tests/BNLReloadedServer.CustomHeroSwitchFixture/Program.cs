using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.Database;
using BNLReloadedServer.ServerTypes;

int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
object Raw(Type type) => RuntimeHelpers.GetUninitializedObject(type);
void Field(object obj, string name, object? value) => obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(obj, value);
// Inspect the actual instance authorization using a zone snapshot, without network/database side effects.
var instance = (GameInstance)Raw(typeof(GameInstance));
var zone = (GameZone)Raw(typeof(GameZone));
var data = (ZoneData)Raw(typeof(ZoneData));
Field(zone, "_zoneData", data);
Field(instance, "<Zone>k__BackingField", zone);
Field(instance, "<GameInitiator>k__BackingField", Raw(typeof(CustomGamePlayerGroup)));
Check(!instance.IsCustomHeroSwitchEnabled, "custom off by default");
Check(!instance.SendWaitingArenaUserToLobby(1), "disabled custom rejects swap");
data.CanSwitchHero = true;
Check(instance.IsCustomHeroSwitchEnabled, "custom uses the zone's enabled snapshot");
Check(!instance.SendWaitingArenaUserToLobby(1), "enabled custom without active lobby rejects swap");
Field(instance, "<Lobby>k__BackingField", Raw(typeof(GameLobby)));
Check(!instance.SendWaitingArenaUserToLobby(1), "custom before match start rejects swap");
Field(instance, "<IsStarted>k__BackingField", true);
data.MatchEnded = true;
Check(!instance.SendWaitingArenaUserToLobby(1), "ended custom rejects swap");
data.MatchEnded = false;
Field(instance, "<GameInitiator>k__BackingField", Raw(typeof(MatchmakerInitiator)));
Check(!instance.IsCustomHeroSwitchEnabled, "matchmaking cannot opt into custom swapping");
Check(!instance.SendWaitingArenaUserToLobby(1), "matchmaking rejects swap even with forged zone flag");
Field(instance, "<GameInitiator>k__BackingField", Raw(typeof(WaitingArenaInitiator)));
Check(!instance.IsCustomHeroSwitchEnabled, "deathmatch keeps separate arena authorization");
foreach (bool? enabled in new bool?[] { null, false, true })
{
    using var stream = new MemoryStream();
    new CustomGameSettings { HeroSwitch = enabled }.Write(new BinaryWriter(stream));
    stream.Position = 0;
    var decoded = CustomGameSettings.ReadRecord(new BinaryReader(stream));
    Check(decoded.HeroSwitch == enabled && stream.Position == stream.Length, "existing toggle wire roundtrip " + enabled);
}
var expected = new System.Collections.Concurrent.ConcurrentDictionary<uint, Guid>();
Field(instance, "_heroChangeDisconnects", expected);
var consume = typeof(GameInstance).GetMethod("ConsumeHeroChangeDisconnect", BindingFlags.NonPublic | BindingFlags.Instance)!;
bool Consume(uint id, Guid guid) => (bool)consume.Invoke(instance, new object[] { id, guid })!;
var oldSession = Guid.NewGuid();
var newSession = Guid.NewGuid();
Check(!Consume(1, oldSession), "ordinary disconnect still announced");
expected[1] = oldSession;
Check(!Consume(2, oldSession), "another player's disconnect still announced");
Check(!Consume(1, newSession), "different session disconnect still announced");
Check(Consume(1, oldSession), "expected hero-change disconnect suppressed");
Check(!Consume(1, oldSession), "hero-change suppression consumed only once");
Console.WriteLine($"{checks} checks passed.");
