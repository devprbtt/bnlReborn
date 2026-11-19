# Experimental admin funny mode

Off by default, scoped to one living player in one match. Admin and Core accounts can enable it in any match; both teams are valid targets. Client capability 15 is required for the admin and target. No production deployment accompanies this change.

The corresponding client uses Ctrl+Shift+G to toggle. Flight is available with every gear: movement follows camera yaw, Shift rises, Ctrl descends, and total speed is capped at 20 m/s. Native CharacterController collisions remain active. Enabling sets current and maximum health to 2000; disabling reconstructs ordinary buffs and clamps current health to the normal maximum. Death is never undone.

With a mining/dig tool equipped, LMB selects a visible player without an arbitrary short range limit (the map diagonal bounds distances). The first selecting click does not charge. A fresh LMB press starts server-timed charging; release throws. Wheel changes hold distance, RMB drops. Charge caps at two seconds: forward velocity grows from 15 to 120 m/s, with upward lift from 8 to 30 m/s. The server issues the impulse; the target's normal movement physics then handles the flight and collisions.

Transport: zone capability 15; message 115 has a fixed 21-byte request body (byte action, uint target, float distance, float3 direction), message 116 returns admin/target/enabled/action/position/velocity/charge. Old clients receive no extension packets. The debug `admin_funny` command also accepts the same six textual fields for diagnostics. Both transports check account roles; the zone checks again after queuing. Positions and throw charge are derived by the server, not accepted from the administrator. Non-finite/invalid vectors and distances are rejected. Selection requires ray proximity and clear terrain; a swept standing capsule stops dragging at terrain or map boundaries.

The server rejects held players' movement and active build/channel/ability/charge/dash/ground-slam requests. Their current action/channel/build is cancelled. One target per admin, no competing holds or chains between enabled admins. Server movement timestamps increase on the synchronized millisecond clock. Death, disconnect/removal, tool switch, role revocation, disable, or a 1.5-second heartbeat lapse releases the target. Flight and health perks also end on disable/death/disconnect/lease expiry. The client drops on lost focus or open menus, expires stale hold state, and uses ordinary movement send cadence and terrain position updates.

Model replacement is deferred. The client exposes ModeChanged for the later presentation hook.

Validation: Release build, 45 new server fixture checks, TeleportPlacement fixture, DashMiss 330794 checks, Auth 4 assertions. The companion Unity validation checks request/state encoding and capability gating, default-off state, flight directions/speed/hover, and native CharacterController wall/vertical movement. A two-human interactive match smoke remains a rollout check.

Reproduce: `dotnet run --project tests/BNLReloadedServer.AdminFunnyFixture -c Release`.


## Overhead name visibility

While enabled, the unit publishes AdminHideNameplate=1 (buff wire value 60) alongside its ordinary health/buff updates and full snapshots. This is presentation state owned by the authorized mode; normal catalogue effects cannot set it. Existing update delivery covers all match observers, including spectators and late joins. Normal effect recomputation preserves it while enabled, and existing mode cleanup removes it on disable/role loss/death/disconnect/lease expiry.

Updated clients suppress overhead name text and title alpha, including crosshair-triggered display, while retaining native health bar fill/visibility. The title's prior enabled state returns on exit. No account name or scoreboard entry is rewritten. Existing clients accept unknown numeric buff keys but require the new client implementation to hide the title; deploy the matching client/server together. Server fixture now passes 52 checks.

## Transformation storm

Every new mode activation starts a shared match-wide storm on updated clients: heavy fog and dark overcast lighting arrive over three seconds, with continuous roof-aware rain, cloud flashes and recorded thunder. The storm lasts while any living admin remains transformed, then fades over four seconds and restores map settings. Normal mode cleanup handles death, disconnect, role loss and lease expiry as well as explicit disable.

The zone assigns a fresh Unix millisecond activation epoch and publishes it on all active admins. Buff keys AdminStormEpochHigh=61 and AdminStormEpochLow=62 encode `epoch / 1048576` and `epoch % 1048576` as exact integer floats. Ordinary unit updates and full snapshots deliver them to observers; catalogue effects cannot inject them. Duplicate enable requests retain the epoch. If the newest admin exits, surviving admins retain the latest epoch so weather does not restart or roll backward. A new activation restarts the thunder timeline without clearing ongoing heavy fog.

Clients use the synchronized server clock to seek thunder audio and avoid replaying past flashes for late joins. Weather ownership does not require the observer to be an administrator or to enable the mode. Existing clients parse the unknown buff keys but need the updated client renderer to show weather. No extension packet change or deployment is included. The Release build passes with warnings as errors, and the server fixture passes 59 checks including epoch precision, multi-admin activation/removal, duplicate enable, catalogue rejection and cleanup. Companion Unity evidence is in the client repository at reports/admin-storm (40 graphics-enabled Play Mode checks).
