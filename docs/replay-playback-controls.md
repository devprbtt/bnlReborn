# Replay playback controls

Source implementation, 2026-10-19. Not deployed.

Zone capability 8 adds viewer-only controls to existing version-1 recordings:
- Service 6, message 110 (client to server): byte command, int argument.
  Commands: 0 state query, 1 pause, 2 resume, 3 relative seek in milliseconds,
  4 skip the current build phase. Relative requests are limited to +/-60 seconds;
  the client buttons use +/-10 seconds. Targets clamp within the recording.
- Service 6, message 111 (server to client): long recorded origin, long position,
  long duration, bool paused, bool seeking, long next combat position (-1 if absent).
- Only a negotiated viewer controlling their own ReplayInstance can act.
  Live matches ignore this message. Legacy clients receive no new state messages.

ReplayTimeline indexes phase changes from existing framed ZoneUpdate packets.
Skip Build selects the next Assault/Assault2/SuddenDeath transition and is disabled
while already in combat. The timeline stores boundaries, not packet payloads.

Pause stops delivery and the replay clock. Seeking cancels the old stream, requests
a fresh spectator zone scene, reloads the original keyframe, then replays intervening
packets with ordered clock updates. This restores block and unit changes instead of
discarding packets or merely changing the clock. Catch-up yields every 64 KiB;
seeking late in a long recording requires reading and rebuilding its earlier state.
Pause intent survives seeking. Completed playback remains open for rewind.
Only the viewer's scene is restarted; no live match state or player stats change.

Client work is maintained in blocknload-unity-upgrade: ReplayControlProtocol,
ReplayPlaybackControls, ServerTime, UnitMotor, HUD and scene transition hooks.
The client restores timing/audio on exit, preserves archived scoreboard loadouts
across seek reloads, and ignores late playback state during menu transitions.

Validation commands:
- `dotnet run --project tests/BNLReloadedServer.ReplayControlsFixture` (23 checks)
- `dotnet run --project tests/BNLReloadedServer.ReplayRecorderFixture` (48 assertions)
- `dotnet run --project tests/BNLReloadedServer.TeamFeaturesFixture` (142 assertions)

Fixtures cover pause, no delivery while paused, ownership isolation, both build
phases, world-packet catch-up, backward/forward bounds, end rewind, cancellation,
and legacy clients. A deployed client/server replay session still needs an
end-to-end visual/audio check; automated state tests do not establish rendering fidelity.
