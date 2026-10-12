# Clan applications and ranked snake draft

## Clan applications

Player-initiated join requests are separate from officer invitations. Requests persist in the additive `ClanApplications` table. Only the target clan's leader or officers may approve or decline. Approval rechecks membership and the 20-member cap under the same store gate as invitation acceptance. Joining or founding any clan removes all of that player's outstanding applications; disbanding removes the clan's applications. Applicants can cancel, with limits of five requests per player and fifty per clan.

Clan capability 3 opts into application requests 18–21 (apply, cancel, approve, decline) and server message 8 (application snapshot). Existing state version 1 and competition messages remain unchanged. Version 1/2 clients receive no application extension. The client decoder is cross-checked by `ClanFixture` using `BnlClientProtocol`.

## Ranked draft

Accepted match rosters and matchmaking MMR are snapshotted before clients load. Lower average MMR gets first pick; the higher-average team gets the following double pick. This is a balancing policy, not a measured claim about first-pick advantage. Equal averages use one coin toss per match. Players within each team pick by descending MMR, with player ID breaking individual ties.

Each pick is sequential. Five-a-side order is A–BB–AA–BB–AA–B; four-a-side is A–BB–AA–BB–A. Taken heroes are restricted across the entire match, while existing class limits and clan bans still apply. The current player has a legal default hero; timeout locks it. Block In and timer callbacks run on the lobby updater, and stale timer callbacks cannot skip turns. Disconnects retain draft seats and picks. Reconnects restore the same pick; ranked hero changes remain prohibited.

Friendly mode cards with legacy draft settings are normalized to free pick during catalogue replication/update. Ranked retains draft. No production deployment is included in this branch.

## Validation

- Clan fixture: 94 checks, including actual client decoder, persistence, officer authorization, concurrent approvals, cancellation, stale requests, and capacity recheck after an invitation takes the last slot.
- Public queue rules fixture: 64 checks, including full ten-turn lobby transitions, mixed timeout/manual picks, reconnect preservation, exact 4v4/5v5 order, match-wide uniqueness, and stale timer rejection.
- Clan fight: 216 checks; lobby join order: 8; reconnect: 10; backfill reentry: 13; map vote: 19 with the local catalogue export.
- Matching Unity client: charge display validation and native menu captures use offline fixture data; they do not represent a live multi-client server playtest.
