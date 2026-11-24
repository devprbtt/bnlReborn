# Replay rewind recovery — 2026-11-23

Backward seeks now use the existing scene-reload reconstruction even when the viewer advertises capability 12. Removing and recreating units in the same scene does not reset native client camera, animation, audio, HUD and monotonic timestamp caches. Current client investigation confirmed retained future look samples after rewind, with older batches ignored and fixed look direction returned from the future buffer.

The compatibility argument to `EnableControls` remains accepted, while in-place backward rebuilding is disabled. Forward catch-up, presentation acknowledgements, requested position, pause intent, and older-client behavior stay supported. Backward seeking can take longer because it reloads the scene. Re-enable only with a complete tested client timeline reset, rather than relying on packet consumption alone.

Folded `PlayerInfo` updates replace membership rather than merging it, matching current client `ZoneData.UpdateData`. A departing player must not return as an unselectable roster row after seek. Per-player statistics continue to merge. The recording oracle now models membership replacement too.

Validation: replay controls 70 checks; recorder 48 assertions; Release warnings-as-errors build zero warnings/errors. Tests cover a capability-12 viewer waiting for scene readiness, replaying the keyframe and target history, keeping earlier block changes, avoiding later destruction, retaining pause, forward seek after reload and rewind from the final frame. This is prepared source; live deployment and signed-client acceptance remain separate.
