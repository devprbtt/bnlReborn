# Custom random heroes

The custom-room owner can toggle Random heroes while preparing. A new match chooses five distinct heroes from the playable catalogue, including Brawn, Skills and Brains, and independently shuffles the same five heroes for each team. Smaller teams consume a subset of that roster. Each player gets their own saved loadout for the assigned hero.

The roster and assignments belong to the match, survive reconnects, and cannot be rerolled by switching heroes or teams. Perks, devices and skins remain editable. Hero switching is disabled for this mode without discarding the room's ordinary hero-switch preference. New players cannot join after the randomized match starts; existing members can reconnect and spectators are unaffected. A new match creates a new roster.

Protocol: ServiceMatchmaker request 30 carries one boolean. The server resolves the authenticated sender's current room and checks ownership and Preparing status. CustomGameUpdate uses the previously unused eighth bit to indicate an optional trailing boolean; the existing seven flags remain in the same byte. Full room snapshots include the setting. The legacy public-browser record is unchanged.

Validation: `dotnet run --project tests/BNLReloadedServer.RandomHeroesFixture -- <catalogue.json>` covers 1,000 seeds, uniqueness, class coverage, mirrored team sets, reconnect stability, server rejection of another hero, room ownership/status checks, setting broadcasts and protocol round trips. The optional catalogue argument also validates the real playable hero pool. LobbyJoinOrderFixture remains green.

This branch is source only; no live deployment was performed.
