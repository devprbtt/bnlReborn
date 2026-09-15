using BNLReloadedServer.BaseTypes;
namespace BNLReloadedServer.ServerTypes;
internal static class DeathmatchFollowup
{
    internal static Dictionary<PlayerMatchStatType, int> AddDestruction(Dictionary<PlayerMatchStatType,int>? stats, Dictionary<ScoreType,float>? raw)
    {
        stats ??= new();
        stats[PlayerMatchStatType.Destruction] = (int)Math.Max(0, new[] { ScoreType.WorldDestroyedResource,
            ScoreType.BlocksDestroyedResource, ScoreType.DevicesDestroyedResource, ScoreType.HeroBlocksDestroyedResource }
            .Sum(key => raw?.GetValueOrDefault(key) ?? 0));
        return stats;
    }
    internal static void RemoveMapForcefields(MapBinary map)
    {
        // Built-in spawn forcefields only; player force-gate devices remain available.
        for (int x=0;x<map.Size.x;x++) for(int y=0;y<map.Size.y;y++) for(int z=0;z<map.Size.z;z++)
        {
            var block=map[new Vector3s(x,y,z)];
            if (block.Id != 44) continue;
            block.Id=0; block.Damage=0; block.VData=0; block.LData=0;
        }
    }
}
