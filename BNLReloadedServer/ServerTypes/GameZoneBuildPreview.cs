using System.Numerics;
using BNLReloadedServer.BaseTypes;

namespace BNLReloadedServer.ServerTypes;

public partial class GameZone
{
    private sealed class PreviewState(BuildInfo build,uint generation)
    {
        public readonly BuildInfo Build=build;
        public readonly uint Generation=generation;
        public long LastUpdate;
    }
    private readonly Dictionary<uint,PreviewState> _buildPreviews=new();
    private uint _buildPreviewGeneration;

    public bool TryBeginBuildPreview(uint playerId,BuildInfo info,out uint unitId,out uint generation)
    {
        generation=0;
        if(!_playerIdToUnitId.TryGetValue(playerId,out unitId) || !_playerUnits.TryGetValue(unitId,out var player)
            || !ReferenceEquals(player.CurrentBuildInfo,info)) return false;
        generation=++_buildPreviewGeneration;
        _buildPreviews[playerId]=new PreviewState(info,generation);
        return true;
    }

    public bool TryAcceptBuildPreview(uint playerId,uint generation,BuildInfo info,out uint unitId)
    {
        unitId=0;
        if(!_buildPreviews.TryGetValue(playerId,out var state) || generation!=state.Generation
            || !_playerIdToUnitId.TryGetValue(playerId,out unitId) || !_playerUnits.TryGetValue(unitId,out var player)
            || player.IsDead || !player.IsActive || !ReferenceEquals(player.CurrentBuildInfo,state.Build)
            || !state.Build.ShowGhost || info.DeviceKey!=state.Build.DeviceKey || info.ToolIndex!=state.Build.ToolIndex
            || !Enum.IsDefined(info.Direction)) return false;
        long now=Environment.TickCount64;
        if(now-state.LastUpdate<75) return false;
        state.LastUpdate=now;
        if(info.ShowGhost)
        {
            if(player.CurrentGear==null || info.ToolIndex>=player.CurrentGear.Tools.Count
                || player.CurrentGear.Tools[info.ToolIndex].Tool is not ToolBuild tool) return false;
            var inside=info.BuildInsidePosition; var outside=info.BuildOutsidePosition;
            if(Math.Abs((int)inside.x-outside.x)+Math.Abs((int)inside.y-outside.y)+Math.Abs((int)inside.z-outside.z)!=1
                || !MapBinary.ContainsBlock(inside) || !MapBinary.ContainsBlock(outside)
                || Vector3.DistanceSquared(player.Transform.Position,outside.ToVector3()+new Vector3(0.5f))>MathF.Pow(tool.Range+3f,2)) return false;
        }
        // Presentation only: never replace CurrentBuildInfo or alter build timers/costs.
        return true;
    }
}
