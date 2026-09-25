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

In the control panel, open a player (Players → Edit). The admin-only **Inventory** section shows:

- counts of everything the account owns, from the same list the game sends at login;
- every private item, with who granted it, when and why, and a Grant or Revoke button;
- any leftover grants for items that are no longer private or no longer in the catalogue, so they can be
  cleaned up.

The optional reason is stored with the grant. Visitors who are not logged in never receive this section.

The same actions are available as an API. Both routes require a logged-in panel session. JSON field
names are snake_case, like the rest of the panel API:

```
GET  /api/players/{playerId}/inventory
     -> { player: {id, nickname}, owned_counts: {Skin: n, ...},
          private_items: [{id, name, category, hero, owned, grant: {granted_at, granted_by, note} | null}],
          stale_grants: [{item, granted_at, granted_by, note}] }

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
3. Grant it to players from their Inventory section in the control panel (or the API).

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
(39 checks), then the real control panel over HTTP: login, the viewer data, grant and revoke attributed to the
signed-in admin, and admin-only serving (53 checks in total). Removing any one of those behaviours makes it fail.

## Making released skins available to everyone

`tools/publish_public_skin_access.py` changes only `scope` to `public` for Nigel Arctic Wolf and
Sweet Science Darklord (the catalogue name is Demon). It checks their hero lists and preserves
IDs, names and asset routes. Run on the VPS without arguments to review, then with `--apply`.
It stores revision-checked card snapshots and archives the two skins' redundant InventoryGrants
rows under a private `/root/config-backups/public-custom-skins-*` directory before removing those
rows. This avoids the current startup validator rejecting grants for public cards; the completed
legacy import marker remains untouched. Other items and grants are unchanged.

The catalogue watcher applies the change without a restart. Public scope covers existing and
future accounts; already-connected clients should reconnect for their inventory to refresh.
