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
