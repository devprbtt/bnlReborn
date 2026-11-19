# Admin entity while spectating

Admin/Core spectators with capability 19 can use the existing `admin_funny on` action after their zone load finishes. Membership stays spectator throughout. The server spawns a transient neutral player body with normal entity powers and 2000 health, without inserting lobby, roster, statistics or match participation entries.

Owner-only zone message 120 contains one Boolean: true before body creation to switch the client to native player control, false after removal to restore the spectator camera. Capability 19 is required; the existing 115–119 request/observer payloads remain unchanged. Normal player activation and older compatible clients keep their current behavior.

Mode off, death, disconnect/leave, lost lease, revoked admin role and match completion remove the transient body and its mode effects. Spectator bodies have no normal respawn, action score or match reward/result row. Death of this body skips normal player kill/respawn processing. Spectators are also excluded from scoreboard network metadata. Existing spectators receive the body's single broadcast create; ownership uses UnitControl, avoiding a duplicate UnitCreate for an already subscribed connection.

Validation: AdminFunnyFixture 193 checks (including entry, 2000 health, roster/stat exclusion, retained spectator membership, repeated activation, off/death/leave/lease/role cleanup, capability and role rejection); LiveSpectateFixture 13; SpectatorRosterFixture 8 using the local catalogue; ReconnectFixture 10; ScoreboardMetadataFixture 20. Client control/camera checks live in the companion Unity repository. Public deployment remains separate.
