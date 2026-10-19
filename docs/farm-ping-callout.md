# Farm marker callout fix

Farm wheel selection previously sent a farm player command followed by a normal team ping, which generated an additional Enemy spotted notice/VO.

Capability 9 adds client-to-server ServiceZone message 112: position (three floats), surface normal (three floats). The server uses the associated player identity and the same validated team-ping admission, rate limit, team visibility, normal sanitization, and emote cancellation as ordinary pings. It relays the existing message 100 marker and exactly one native note_command_farm command. No new receiver packet is needed. Ordinary message 99 pings retain Enemy spotted.

Requires the matching client update. Clients connected to older servers fall back to a farm callout without the marker; deploy the server before distributing the client. Older clients sending the original two requests retain their old behavior until updated. No deployment performed.

Validation: TeamFeaturesFixture passed 170 assertions, including exact marker coordinates/normal and one native callout for farm versus ordinary pings, capability negotiation, and unauthenticated request rejection. No live multiplayer capture yet.
