# Empty custom room cleanup

2026-10-15. A room is occupied only while an online connection still belongs to it. Retained reconnect seats do not keep an otherwise empty room published. Spectators with current membership count as occupants. Disconnect and browser refresh sweep stale rooms; explicit room departure also closes a room whose remaining seats are offline. Creating or successfully joining another custom room first releases the previous membership.

Closure removes the directory entry before callbacks, clears matching memberships and subscriptions, and detaches matching game-instance participants. A stale roster cannot remove a player from a newer room. Reconnect seats remain available while teammates are online.

Validation: `dotnet build BNLReloadedServer/BNLReloadedServer.csproj`; `dotnet run --project tests/BNLReloadedServer.EmptyCustomRoomsFixture` (10 checks); `dotnet run --project tests/BNLReloadedServer.CustomRoomReturnFixture` (23 checks).

Deployment pending. Live browser/disconnect validation still required after deployment.
