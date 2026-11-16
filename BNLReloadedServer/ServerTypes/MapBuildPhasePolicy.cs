namespace BNLReloadedServer.ServerTypes;

// Initial build preparation follows the selected map's cubes per side.
// Core-only/non-objective modes keep their existing timing; custom games use the host's value.
public static class MapBuildPhasePolicy
{
    public static int? Seconds(int cubesPerSide) => cubesPerSide switch {1 => 180, 2 => 240, >= 3 => 360, _ => null};
}
