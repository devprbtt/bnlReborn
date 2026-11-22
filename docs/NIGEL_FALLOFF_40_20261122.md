> **Deployed and verified:** both rifle variants now start falloff at40m.

# Nigel rifle falloff40m — 2026-11-22

Owner revised the just-deployed50m threshold to **40m**, authorizing continuation of the same live tuning task. Two rifle documents, including Spot Shot, change six fields: primary full-damage threshold, English weapon stats and reticle preview. Preserve40 base damage, critical multiplier1.625, 55% retained damage at100m and100m maximum range. Other cards/fields remain unchanged, including Trondson and the pending Roly death-cooldown fix.

Actual server damage conversion gives body damage40/37/34/31/28/25/22 at40/50/60/70/80/90/100m. The previous50m curve gave40/40/36.4/32.8/29.2/25.6/22 at the same points. Close damage is retained below40m; reduction begins immediately after40m.

Plan: `catalogue/nigel-falloff-40-20261122/nigel-earlier-falloff.patch.json`. Generator: `tools/prepare_nigel_earlier_falloff.py`, now guarded against the deployed50m source. Historical70-to50 plan is preserved. Reuse the revision-guarded writer with `--expected-card-count 2`, private backup and readback. No binary/client update or restart is needed. Evidence: client reports/nigel-falloff-40/ and extracted/work/nigel-falloff-40-20261122/.

Validation:56 actual-engine checks and all six writer scenarios pass. Source must be committed/pushed before applying the authorized two-card update. Final deployment result is recorded after live verification.

Patch note: **Nigel rifle and Spot Shot damage falloff now starts at40m instead of50m.**


Deployment result:2/2 documents applied; raw/runtime six fields verified. Backup `/root/config-backups/six-hero-balance-20261122T150228Z`. Source6368e1a; binary0fee0bc unchanged, no restart. Service/status/listeners and journal health pass. Reconnect for refreshed cached stats.
