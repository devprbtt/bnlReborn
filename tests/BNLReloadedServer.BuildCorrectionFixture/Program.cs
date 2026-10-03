using System.Reflection;
using System.Runtime.CompilerServices;
using BNLReloadedServer.BaseTypes;
using BNLReloadedServer.ProtocolHelpers;
using BNLReloadedServer.ServerTypes;

void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
var size = new Vector3s(4, 4, 4);
var map = new MapBinary(3, new byte[4 * 4 * 4 * 6].Zip(0).ToArray(), size, -1,
    new MapUpdater((_, _) => { }, (_, _) => { }, _ => { }, _ => true));
var zone = (GameZone)RuntimeHelpers.GetUninitializedObject(typeof(GameZone));
var data = (ZoneData)RuntimeHelpers.GetUninitializedObject(typeof(ZoneData));
data.BlocksData = map;
typeof(GameZone).GetField("_zoneData", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(zone, data);
var unit = (Unit)RuntimeHelpers.GetUninitializedObject(typeof(Unit));
var inside = new Vector3s(1, 1, 1); var predicted = new Vector3s(1, 2, 1); var actual = new Vector3s(2, 2, 1);
unit.CurrentBuildInfo = new BuildInfo { BuildInsidePosition = inside, BuildOutsidePosition = predicted };
var method = typeof(GameZone).GetMethod("AddRequestedBuildPositionCorrection", BindingFlags.Instance | BindingFlags.NonPublic)!;
var updates = new Dictionary<Vector3s, BlockUpdate> { [actual] = new BlockUpdate { Id = 17 } };
method.Invoke(zone, new object[] { updates, unit, actual });
Check(updates.ContainsKey(predicted) && updates[predicted].Id == 0, "redirected placement clears the client's predicted destination");
Check(updates.ContainsKey(inside) && updates[actual].Id == 17, "support is corrected without overwriting the successful placement");
updates = new Dictionary<Vector3s, BlockUpdate> { [predicted] = new BlockUpdate { Id = 58 } };
method.Invoke(zone, new object[] { updates, unit, predicted });
Check(updates[predicted].Id == 58, "matching successful placement is preserved");
unit.CurrentBuildInfo.BuildOutsidePosition = new Vector3s(-1, -1, -1);
updates.Clear(); method.Invoke(zone, new object[] { updates, unit, actual });
Check(updates.Count == 1 && updates.ContainsKey(inside), "out-of-bounds prediction cannot be read from the map");
