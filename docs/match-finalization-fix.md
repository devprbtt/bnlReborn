# Duplicate match completion

2026-10-19. Source fix; not deployed.

The public archive for Wilson's Bay Mid Edit, round
`173293ef-4ca0-4e49-8aa5-61099b776332:1792349940734`, reports player 3's
K/D/A as 30/24/46. The supplied profile screenshot has two results with
15/12/23, one Victory and one Left Match, one second apart. The archive
ends at 1792352348074; the player's MatchEnded presence closes at
1792352347202 (872 ms earlier). This is consistent with repeated EndGame
execution. Spectating itself is not established as the trigger.

BeginEndOfGame previously allowed a second completion while waiting or
after completion. Surrender lacked an ending-state check, and three
rejection paths continued into ballot creation. EndGame added cumulative
stats and persisted another profile result on every call. The first call
marks players inactive, explaining the later Left Match result. The client
loads both persisted rows and resolves both to the same archive, explaining
the repeated displayed MMR; that display alone does not prove two rating awards.

The fix claims ending before queuing or awaiting, guards result side effects
independently, rejects late surrender requests/votes, and returns immediately
for rejected ballots. Completed participation ignores late result, join and
leave updates. Reconnect segments before completion remain additive.

Validation:
- DeathmatchSurrenderFixture: 15 checks, including duplicate requests,
  competing end callbacks, late votes and suppressed duplicate result output.
- TeamFeaturesFixture: 142 assertions, including two legitimate play segments
  and immutable completed results after disconnect/rejoin/repeated completion.
- ReconnectFixture: 10 checks, including spectator ownership isolation.
- AccountingFixture: 19 assertions.

Run each with `dotnet run --project tests/BNLReloadedServer.<name>`.

Historical data has not been modified. A repair must use a backed-up master
database and reconcile each affected round's history, archive and hero totals.
Do not deduplicate by game instance alone: an instance can host multiple rounds.
Do not divide every archive counter by two: reconnect segments and win counters
have different accumulation semantics (this player's raw WinSkills is 3).
The public API evidence is sufficient to confirm the K/D/A inflation but not
to reconstruct every historical reward, rating and progression adjustment.
