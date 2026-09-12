# Astarella ground-slam cancellation

The server treats a validated ground-slam cast as a one-shot entitlement bound
to the player, current unit, and ground-slam tool slot. A matching hit consumes
that entitlement.

When the same player's controlled unit emits `ZoneEventDoubleJump`, the server
cancels the entitlement before rebroadcasting the event. A later stale
`GroundSlamHit` is therefore ignored. Casting ground slam again creates a fresh
entitlement, preserving Space then CTRL behavior.

Run the focused regression fixture with:

```powershell
dotnet run --project tests/BNLReloadedServer.GroundSlamFixture/BNLReloadedServer.GroundSlamFixture.csproj
```
