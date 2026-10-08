# Paradise - Winter

Custom Games appearance variant of Paradise, requiring client beta.56 or newer. The map payload changes only `map.properties.render` from `DaytimeWarm` to `DaytimeWarm_winter`; terrain geometry, colors, units, objectives, water, collision and gameplay rules are unchanged. The client strips the suffix when resolving lighting and applies its shipped winter textures and independent Snowfall graphics option.

`tools/prepare_paradise_winter.py` stages and hashes the original before producing the variant. `docs/paradise-winter-provenance.json` records input/output hashes. `tests/BNLReloadedServer.WinterMapFixture` validates the native loader and wire roundtrip against both map payloads. The thumbnail is an unmodified native winter capture from the client project (`reports/beta-winter/02-winter-snowfall.png`).

`tools/activate_paradise_winter.py PAYLOAD` performs a dry run on the production host. `--apply` requires an idle server, a published beta.56-or-newer feed, and the hosted thumbnail. It backs up the catalogue pool, original card and minimum-version setting under `/root/config-backups/paradise-winter-TIMESTAMP`, stops the server, installs the map, clones the Paradise card, appends only to the Custom pool, raises the minimum client to beta.56, and starts and validates the server. Failures restore the prior pool/minimum and remove only this variant. Original, Casual and Ranked maps remain intact.

For a later rollback, wait for an idle server, stop it, restore `map_list.before.json` with the current CouchDB revision, delete only the winter card with its current revision, remove the hash-matching winter map, restore `minimum_client_version.before.txt`, and restart. Preserve any unrelated catalogue changes made after activation.
