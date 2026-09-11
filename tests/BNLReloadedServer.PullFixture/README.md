# Overlapping pull effects

Gravity well applies force 6 continuously; Sweet Science's Graviton Stance
applies force 20 for one second. Previously the stance's removal sent a disabled
pull maneuver without considering the well's still-active effect. Existing
weaker effects were never reapplied, and stale force history could suppress them.

Reconcile all active pull effects and their sources when effects/sources change.
Select the strongest force, keep the current source on ties, hand off directly
to a surviving pull, and send disabled only when none remains. Preserve fan
negative-force behavior. Source-only changes on a shared effect key also
reconcile; unchanged expiry scans do not rescan pulls or send extra maneuvers.

Validation:

```
dotnet run --project tests/BNLReloadedServer.PullFixture -- <catalogue.json>
dotnet run --project tests/BNLReloadedServer.IgnoreCasterFixture -- <catalogue.json>
```

19 pull assertions pass, including actual catalogue gravity/stance expiration,
shared sources, inactive removal, batch clear, purge and negative-force fan.
The new regression fails on pre-fix commit 3db72db at the first stance-to-well
handoff. The 12 ignore-caster assertions also pass. No client/protocol or balance
card changes are required. Production deployment and multiplayer verification
remain separate.

Production deployment: runtime commit dec6001fd885e2d72baaf47b702b25005bd0076e
activated on 2026-09-11 at 04:45:36 UTC. Release build completed with zero
warnings/errors; uploaded DLL/PDB hashes verified remotely. All three listeners,
signed game-ticket authentication and startup checks passed. Follow-up service
state active/running, NRestarts=0, no error-priority journal entries.
Previous runtime a02d26245a11 retained for rollback; snapshot
/root/config-backups/bnl-server-hotfix-20260911T044535Z.
Live multiplayer ability-interaction confirmation remains to be done.
