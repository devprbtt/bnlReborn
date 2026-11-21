# Practice Grounds quit compatibility

The shipped client pause-menu confirmation for tutorials calls `FinishTutorial`
(zone message 6), as does the tutorial results exit. The server previously ignored
that message, leaving players in Practice Grounds after confirmation.

Route message 6 through the existing ExitMatch handler: leave the associated
instance with MatchQuit, detach streams, perform normal region/scene cleanup and
preserve surviving custom-room membership as usual. No client update or CDB
change is required. This request does not award tutorial completion, currency or
pickup credit; authenticated server gameplay remains responsible for progress.

Validation: `dotnet run --project tests/BNLReloadedServer.CustomRoomReturnFixture
-c Release`, covering real message dispatch for exit IDs 2/5/6, correct player and
reason, room/ownership/team preservation, closed rooms, spectators,
unauthenticated requests and coalesced transport bytes.

Player-facing note: Fixed quitting Practice Grounds from the pause menu or
tutorial results screen leaving you stuck in the session.
