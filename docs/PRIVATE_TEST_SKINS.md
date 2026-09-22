# Private authoring skins

Owner: Prbtt, SteamID64 `76561197990315750` (resolved from the supplied Steam community profile).

- `skin_hunter_arctic_wolf_private`: Nigel Arctic Wolf, `character_longshot/Prefabs/Units/LongshotArcticWolfPrivate`, `fps_longshot_s1/Prefabs/Player/LongshotPlayerArcticWolfPrivate`.
- `skin_boxer_demon_private`: Sweet Science Demon, `character_sweetscience/Prefabs/Units/SweetScienceDarklordFinal`, `fps_sweetscience_s6/Prefabs/Player/SweetSciencePlayerS7`.

`PrivateSkinRegistration` appends rendering metadata and hero skin-list entries when their base skin/hero are present. Registration is idempotent and creates no shop offers. The cards must remain replicable to clients so other players can render the wearer. Ownership comes from the stable authenticated Steam identity, not a nickname or client request.

`PrivateSkinAccess` filters real/dummy inventories, lobby equip requests, saved loadouts and persistence sanitization. It rejects both non-owner requests and private skins equipped on the wrong hero. Existing public skin IDs are unchanged.

Validation: `dotnet run --project tests/BNLReloadedServer.PrivateSkinsFixture -c Release` passes 53 assertions covering registration, access, forged selection, wrong-hero selection and stored loadouts.

Deployment is pending. Deploy only with a matching client containing both generated skins, their portraits, and the new Nigel FPS/weapon assets. This branch does not publish a client feed or release a shop offer.
