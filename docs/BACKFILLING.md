# Matchmaker backfilling

Friendly matchmaking keeps a disconnected player's slot for up to 45 seconds so
a brief connection loss can recover. Explicitly leaving the match releases the
slot immediately. Ranked, custom-game and pre-match reconnect timing retain the
configured grace period.

After a friendly slot is released, the matchmaker scans within five seconds.
Enabling **Allow Backfill** also triggers a scan immediately. Rating quality is
used to choose the best eligible queued player and team, but an open slot is not
blocked by the quality threshold used when forming a new match. A player who has
already participated in that match remains ineligible to backfill it again.

Validation: `BNLReloadedServer.BackfillReentryFixture` covers participant
history, repeat-entry rejection and reconnect-window scope. The cumulative
Release solution build must pass with warnings treated as errors.
