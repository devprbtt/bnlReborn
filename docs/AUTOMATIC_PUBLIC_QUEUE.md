# Automatic public queue

Casual and Ranked queue requests share one regional player pool.

- Fewer than 8 players: keep waiting.
- 8 or 9 players: start a 30-second grace period.
- 10 or more players during the grace period: offer a 5v5 Ranked match immediately.
- Grace period expires with 8 or 9 players: offer a 4v4 Casual match. A ninth player stays in the queue.
- Queue population falls below 8: cancel the grace period. A later eighth player starts a fresh period.

Squads remain grouped by the existing balancer. If it cannot form equal teams without splitting a squad, the queue keeps waiting and retries. Casual backfill remains available from the shared pool, while a ready 10-player Ranked match takes precedence over backfill.

The match confirmation packet carries the selected mode, so the client renders eight acceptance slots for Casual and ten for Ranked. Declines and timeouts return the remaining players to the shared queue and begin a fresh decision cycle.

Set `BNL_PUBLIC_QUEUE_GRACE_SECONDS` to an integer from 1 through 300 to override the default 30-second grace period at process startup.

Validation:

```powershell
dotnet build BNLReloaded.sln -c Release --no-restore -warnaserror
dotnet run --project tests/BNLReloadedServer.AutomaticPublicQueueFixture/BNLReloadedServer.AutomaticPublicQueueFixture.csproj -c Release --no-restore
```
