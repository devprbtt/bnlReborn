using System.Text.Json;
namespace BNLReloadedServer.ServerTypes;
public partial class GameZone
{
    private string? _lastBlockOwners;
    private string? BlockOwnerSnapshot() => _gameInitiator is WaitingArenaInitiator
        ? JsonSerializer.Serialize(new { blocks = MapBinary.OwnedBlocks.Select(pair => new {
            x = pair.Key.x, y = pair.Key.y, z = pair.Key.z,
            owner = pair.Value.PlayerId ?? pair.Value.OwnerPlayerId ?? 0
        }).OrderBy(b => b.x).ThenBy(b => b.y).ThenBy(b => b.z).ToArray() }) : null;
}
