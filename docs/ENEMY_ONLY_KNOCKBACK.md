# Enemy-only weapon knockback

Prepared 2026-11-25 on top of production/main `2078632ab72368dd45b1ff71bdb42e2825bd9514`.

Weapon knockback now admits opponents and the caster only when the existing
`AffectCaster` flag permits self-knockback. The owner explicitly requested that
rocket jumping remain available. Same-team players cannot be knocked back even
in custom games with team killing enabled.

`GameZone.CanKnockback` uses authoritative source teams and the existing
waiting-arena per-player combat relationships. Owned projectile/device sources
identify self through `CombatOwnerPlayerId`. Block sources retain their own team
when no owning unit is available. Eligibility is applied before recall interrupts
and impact recipient lists. The direct admin repulse path also uses the guard.
Forces, ranges, falloff, original targeting restrictions, root/knockback immunity,
damage, and other movement maneuvers retain their existing rules. No catalogue,
protocol, or client update is required.

Validation:

```powershell
dotnet build BNLReloadedServer/BNLReloadedServer.csproj -c Release -warnaserror
dotnet run --project tests/BNLReloadedServer.KnockbackFixture -- <public-catalogue.json>
dotnet run --project tests/BNLReloadedServer.CustomTeamCombatFixture -c Release
```

The focused fixture executes the real effect dispatcher and captures maneuver,
recall-cancel and impact packets. The reference public catalogue contains 41
knockback definitions across 34 cards, including saucer, rocket and Kreepy variants.
It also verifies self flags, owned projectiles, unowned blocks, team changes,
friendly-fire settings, native targeting restrictions, immunities and waiting-arena
relationships. The original server fails the teammate regression check.
Admin repulse is covered by shared eligibility and a dispatch-site audit, rather
than a live admin-weapon exercise. Live multiplayer acceptance and deployment
remain pending.

Player-facing note for the next release: "Weapons can no longer knock back
teammates, including saucers, rockets and Kreepy's weapons. Self-knockback and
rocket jumping still work."
