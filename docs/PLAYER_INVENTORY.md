# Player inventory

The catalogue (CouchDB) says which items exist. The server decides who owns them.

- A card with `"scope": "public"` (every card before this change) is owned by every player.
- A card with `"scope": "private"` is owned only by players with a row in the `InventoryGrants`
  table of `PlayerData/playerData.db`.

The client ignores `scope`, so a private card is still sent to every client. Other players need the
card to render whoever wears the skin. No shop offer is needed for either kind.

One rule covers every category: skins, heroes, devices, perks and badges. Ownership is checked in
four places: when inventory is built at login, when a skin is picked in the lobby, when a loadout is
saved, and when a saved loadout is loaded (`PlayerDataSanitizer`). A skin can only be worn on the hero
named by its own `hero_key`. A loadout whose skin the player no longer owns falls back to the first
skin of that hero they do own.

## Granting and revoking

Use the control panel API with a logged-in panel session. Both routes require authentication.

```
GET  /api/players/{playerId}/inventory
     -> { grants: [{item, grantedAt, grantedBy, note}], privateItems: [{id, category}] }

POST /api/players/{playerId}/inventory
     { "item": "skin_hunter_arctic_wolf_private", "action": "grant", "note": "optional reason" }
     { "item": "skin_hunter_arctic_wolf_private", "action": "revoke" }
     -> { result: "Granted" | "Revoked" | "Unchanged" }
        404 UnknownPlayer / UnknownItem, 400 PublicItem
```

A change takes effect immediately. If the player is online, the server sends their new inventory, and
the client replaces its inventory on that update, so no reconnect or restart is needed. Each grant
records the panel user who made it and the note. Revoking still works after a card has left the
catalogue.

## Adding a new exclusive item

1. Put the card in CouchDB with `"scope": "private"`. For a skin, also add it to its hero's
   `data.skins` list. `tools/publish_private_skin_cards.py` shows the pattern: read-only by default,
   `--apply` writes revision-checked documents and keeps a rollback snapshot under
   `/root/config-backups`.
2. Reload the catalogue (control panel "refresh CDB", or the change watcher).
3. Grant it to players through the API.

No server code change and no server deploy are needed. A new skin still needs its assets in the client.

## Migration from the grant file (one time)

Before this change the two private skins were added in code (`PrivateSkinRegistration`), and
ownership came from `Configs/private_skin_grants.json` plus a hardcoded owner (`PrivateSkinAccess`).
On its first start the new server copies that file and the old recovery owner (SteamID64
`76561197990315750`, both skins) into `InventoryGrants`. It then records `private_skin_grants_file_v1`
in `AppliedMigrations` so the import never runs again; otherwise a later revoke would come back on
the next restart. The file is left in place and ignored from then on. SteamIDs without an account are
listed in the start log.

### Deploy order

1. `sudo python3 tools/publish_private_skin_cards.py`, check the plan, then run it again with `--apply`.
   The running old server keeps working: its registration replaces the published cards with its own
   copies.
2. Deploy the server.

If the server starts before step 1, it logs
`Inventory grants name items that are not private cards in the catalogue` at journald error priority.
`deploy-code-hotfix.sh` treats that as a failed start and rolls back. Without that check, logins would
remove the skins from saved loadouts.

### Rolling back

The previous server still works against the published catalogue: its registration overrides the
cards and its grant file is untouched. Grants made through the API after the deploy are not in that
file, so they are missing while the old server runs. They stay in `InventoryGrants` and return when
the new server is redeployed.

## Validation

`dotnet run --project tests/BNLReloadedServer.PlayerInventoryFixture -c Release` runs the real
`MasterServerDatabase` on a temporary SQLite file: the one-time import, grant and revoke, restarts, the
live push to an online player, inventory for every category, loadout rules and the startup check
(39 checks). Removing any one of those behaviours makes it fail.
