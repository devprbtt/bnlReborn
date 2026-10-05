# Rejoining while the previous session still exists

A VPN route change or fast client restart can reconnect before the server tears down the old match socket. `SendLoadZone` previously rebound the existing hero's service but omitted its create/update snapshot. A living hero therefore stayed on the server while the replacement client had no controlled unit and displayed the origin/dead-player view.

The snapshot now sends the existing living hero with recipient-local ownership and its current state. It retains the unit ID and position, does not spawn a duplicate, and leaves dead heroes to the existing authoritative respawn path. Remote players remain uncontrolled and dead remote units are omitted.

Rebinding installs a new connection record, removes old subscriptions, and disconnects the old match socket. Queued lobby/load/disconnect actions check their captured connection identity before affecting the replacement. Removal also checks the expected session ID.

Validation: `dotnet run --project tests/BNLReloadedServer.ReconnectFixture` (10 checks), CustomHeroSwitchFixture, BackfillReentryFixture, and server `dotnet build -warnaserror`. ReconnectFixture inspects actual hero snapshot calls and the queued-work identity guard. It does not simulate a live VPN or full Unity match. No wire format changes. Deployment is separate.
