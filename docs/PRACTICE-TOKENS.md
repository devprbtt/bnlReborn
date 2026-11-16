# Practice token persistence

Discord GUI ISSUES 3.0 reported that Practice Grounds tokens did not persist.
Accepted tutorial-map pickups now save a unique account/map/unit-position identity in
`TutorialTokenCollections`, update `Users.tutorial_tokens`, refresh the active player
cache and send the normal `PlayerUpdate.TutorialTokens` notification. Runtime unit IDs
are deliberately excluded from the identity because they change between sessions.

The migration creates the collection table automatically. Existing progress is retained;
repeating a token cannot increase the saved count. The count is bounded by the actual
tutorial map's token total. Other pickups and map-editor sessions do not write progress.
Database writes and notifications are serialized so rapid pickups cannot publish an older count.

The shipped map has 23 loose pickups and 12 destructible targets. When a target drops
its pickup, the pickup inherits the authored target's identity. Its new runtime ID and
falling position do not count as a new collectible. The cap stays at the initial 35
authored checkpoints rather than growing when loot is spawned.

The previously empty `FinishTutorial` handler now uses the existing exit path for an
authenticated player in a tutorial game mode. It ignores other modes and unauthenticated calls.

Validation: Release build with warnings treated as errors; the 12-check TutorialTokenFixture
covers persistence after reopening SQLite, duplicate/account isolation, the 35-token cap
and the completion protocol guard. The existing 11-check NineTopicsFixture also passes.
Run with `dotnet run --project tests/BNLReloadedServer.TutorialTokenFixture -c Release`.
The authenticated local PTR also reached 35/35 through actual pickup notifications,
including all 12 target drops, and returned to MainMenu through FinishTutorial.
The local-only fixture used the existing admin debug command to destroy the targets;
the account's original role was restored afterward. SQLite contains 35 unique token
rows and a saved total of 35. This is protocol integration coverage, not a manual
playthrough of the whole tutorial.
Production deployment is separate from this source change.
