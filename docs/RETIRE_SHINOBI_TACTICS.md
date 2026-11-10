# Retire Ninja Shinobi Tactics (2026-11-09)

Player-facing note: Removed Juan Shinobi's Shinobi Tactics hero perk. Shrine Bomb uses its normal behavior; the perk's instant explosion and associated penalties no longer apply.

Server change only; prepared, not deployed. Remove the perk from global hero selection and shop, deny ownership/equip even with legacy grants, clean saved loadouts, and neutralize its ability replacement. Retain the card identity for history. Other Ninja perks and stock ability cards are unchanged. Full replication and incremental updates enforce the retirement.

Validation: RetiredHeroPerksFixture 92 checks; RetiredHeroSkinsFixture 269 checks; Release build with warnings as errors: zero warnings/errors. No live match validation.

Reproduce: `dotnet run --project tests/BNLReloadedServer.RetiredHeroPerksFixture -- <catalogue-json>`.
Validation input: existing local snapshot `J:/github/blocknload-unity-upgrade/extracted/work/beta92-server-inputs/server-live-cards.json`, SHA-256 03c9857c2e9bebc487fc1fc651c5ec7fcbac3331ee9c506d7188f708ca50be45. Snapshot remains outside Git.

Deployment requires authorization separately. Existing clients receive the catalogue after reconnect; no client rebuild required. Rollback by restoring previous server revision. Cleared saved perk selections are not automatically restored by rollback. No currency/inventory grant changes.


## Walk It Off follow-up (2026-11-09; not deployed)

Player-facing note: Removed the Walk It Off defensive perk from all heroes.

Retires `perk_def_walk_it_off` alongside Shinobi Tactics. The common helper is now named RetiredPerks and filters defensive/offensive lists as well as hero lists. Existing grants cannot restore availability; saved selections are removed; both positive and negative effect modifiers are cleared from the retained legacy card. Direct effect cards remain resolvable for history. No refunds, currency edits or history deletion.

Combined retirement fixture: 113 checks, covering both retired cards, shop/list removal, existing grants, equip rejection, saved-loadout cleanup, idempotency and incremental catalogue updates; all other perk modifiers preserved. Release warnings-as-errors build passed. Server deployment remains separate; no client rebuild required.
