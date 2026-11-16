# Spectator results after loading and reconnecting

An admitted spectator could finish loading after the one-time final result broadcast and remain on an empty result screen. GetLastScene also explicitly removed an admitted spectator whose match had ended, causing reconnect/load recovery to return to the menu.

Retain the read-only EndMatchData in the zone and resend the winner and final rows after the initial observer snapshot. While the result delay is running, send the winner only; the final result broadcast remains authoritative. GetLastScene preserves existing seats; fresh admission to ended matches is still rejected. No reward, profile or progression update is invoked by observer state delivery.

Validation: MatchChatFixture 27 checks, LiveSpectateFixture 13 checks; Release build with warnings as errors. Deployment remains separate.
