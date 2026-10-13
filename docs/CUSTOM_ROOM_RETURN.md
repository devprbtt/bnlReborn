# Returning from a custom match

ZoneLeave and ExitMatch detach the player from the active match but retain custom-room membership if GetFullCustomGameUpdate still resolves the room after detachment. The existing LeaveCustomGame command still exits the room. A missing/deleted room clears stale membership. Spectator-only membership cleanup remains in GameInstance.PlayerLeftInstance.

The client must open Play/custom setup on MainMenu startup when its replicated custom roster still contains the local player. Without that companion UI change, the room is retained but the player initially lands on Home.

Validation:
- `dotnet run --project tests/BNLReloadedServer.CustomRoomReturnFixture -c Release`: 23 checks for both quit messages, surviving/deleted rooms, deletion during exit, public matches, spectator cleanup and explicit Leave Room.
- `dotnet run --project tests/BNLReloadedServer.BackfillReentryFixture -c Release`: existing public-match reentry rules pass.

These are in-process service tests, not a deployed multi-client match test. Deployment is separate.
