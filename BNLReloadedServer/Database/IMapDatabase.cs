using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.Database;

public interface IMapDatabase
{
    public List<string> GetMapIds();
    public bool HasMap(Key key);
    public MapData? LoadMapData(Key key);
    public int GetCubeCount(Key key) => CasualMapPool.CountCubes(LoadMapData(key), k => k.GetCard<CardUnit>());
    public void SaveMap(string key, MapData mapData);
}
