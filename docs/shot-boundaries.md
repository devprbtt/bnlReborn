# Metal and forcegate shot boundaries

Solid metal (`block_metal`) stops projectiles and occludes splash against covered player body cells. Explosion contact samples inside metal are discarded; the existing outside-face sample seeds the blast instead, so exposed players still take damage. Both flood and raycast explosion modes use this rule.

Shots may enter normal/disco forcegates and reach targets inside. Exiting a contiguous gate volume stops the shot, including shots fired inside. The shared `ShotBoundaryTrace` voxel walker matches the Unity client implementation. Server ranged-hit correction removes targets beyond a boundary. Moving projectile checks use successive reported positions and clear their position state on drop. The updated client supplies matching collision and visual behavior.

Validation: `dotnet run --project tests/BNLReloadedServer.ShotBoundaryFixture`. Synthetic map coverage includes six exit directions, connected gate variants, inside/outside shots, metal tunneling, server hit correction, and exposed/covered splash targets in both algorithms. Build and fixture are local checks, not a live-match capture. Deployment is pending.
