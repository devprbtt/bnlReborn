# Earlier Nigel rifle falloff — 2026-11-22

Prepared; **not applied to the live CDB**. The owner clarified that this follow-up concerns Nigel's rifle only. Choose an earlier full-damage threshold of **50m instead of 70m**, including Spot Shot. Preserve 55% damage at 100m, the 100m maximum range, 40 base damage and 1.625 critical multiplier. Trondson and the revolver are unchanged.

| Distance | Current body damage | Prepared body damage |
| --- | ---: | ---: |
| 50m | 40 | 40 |
| 60m | 40 | 36.4 |
| 70m | 40 | 32.8 |
| 80m | 34 | 29.2 |
| 90m | 28 | 25.6 |
| 100m | 22 | 22 |

These values come from the actual server ConvertToDamageData path without shields or damage buffs. Damage drops continuously immediately after50m. Native critical multiplication remains in place (for example, at70m headshot damage65 to53.3).

The two-card/six-field patch changes each primary damage node's `max_damage_range`, English stat text and reticle `preview_damage_range`. Full-card scope comparison confirms that every other field and every other document is unchanged, preserving Spot Shot reveal logic, projectile/range/ammo/timing, all other weapon modes and prior server balance changes.

Files: `tools/prepare_nigel_earlier_falloff.py`, `catalogue/nigel-earlier-falloff-20261122/nigel-earlier-falloff.patch.json` and `tests/BNLReloadedServer.NigelFalloffFixture/`. Fresh read-only public input and candidate are in client `extracted/work/nigel-earlier-falloff-20261122/`; logs, source hashes and table are in `reports/nigel-earlier-falloff/`.

Validation: **50 actual-engine checks** across both variants, the onset boundary, direct/headshot damage at six distances, unchanged max range, complete catalogue scope and all nonchanged fields. Six fake-Couch scenarios pass for the new plan and six for the existing13-card default plan. Release build with warnings as errors: zero warnings/errors. Actual host preflight is read-only: **2 pending, 0 applied**. No CDB writes, deployment, service restart or public client/feed release performed. No live gameplay acceptance is claimed.

```powershell
python tools/prepare_nigel_earlier_falloff.py <fresh-public-catalogue.json> <output>
dotnet run --project tests/BNLReloadedServer.NigelFalloffFixture -c Release -- <before.json> <candidate.json> <damage-curves.csv>
python tests/test_six_hero_balance_apply.py <before.json> catalogue/nigel-earlier-falloff-20261122/nigel-earlier-falloff.patch.json
dotnet build BNLReloadedServer -c Release -warnaserror
```

The existing revision/field-guarded writer now accepts an explicit expected-card count while keeping its13-card default. It also rejects duplicate ids. After separate deployment authorization, preview on the host with:

```sh
sudo python3 apply_six_hero_balance.py nigel-earlier-falloff.patch.json --expected-card-count 2
```

Use `--apply` only for the authorized write. The writer preserves other fields, backs up privately, checks revisions, reads back and rolls back failures without overwriting concurrent authors. Refresh/review the patch on source drift. Existing server/clients support the data; no new binary is needed. Reconnect clients for updated cached stats/reticle. Keep this follow-up separate from already-applied balance plans and the pending Roly F death-cooldown branch.

Player-facing note: **Nigel's rifle damage falloff now starts at50m instead of70m, including Spot Shot. Damage at70m changes40 to32.8; minimum damage remains22 at100m.**
