# Entity activation transport fix

2026-11-20: the production client recognized Core role and capability 19, but
activation timed out twice. Read-only production inspection found release
`ef0aa51203ac` active.

`SessionReader` dispatches multiple TCP frames using one receive stream.
`ReceiveAdminFunny` incorrectly required the stream to contain exactly 21
remaining bytes. When movement or other messages followed an activation in the
same TCP receive, the handler silently returned without reading the request.
The client never received state 116 and displayed its private timeout notice.

The guard now requires at least 21 bytes and consumes exactly the fixed body,
matching the surrounding service parser's shared-stream convention. Role and
capability checks remain in place. No protocol or client update is required.

Validation: `dotnet run --project tests/BNLReloadedServer.AdminFunnyFixture -c Release`
passes 221 checks. New cases cover transport tails of 0/1/32/1024 bytes, all
short bodies, and real SessionReader combined frames with no split, a split
inside the request and a split at the frame boundary. The combined-buffer case
failed against the old guard before the fix.

This patch is prepared for deployment; it has not been applied to production.
