# Combat healing feedback - 2026-10-10

Dropped health pickups retain their actual healing but do not send a collector-as-healer portrait attribution or award healing-given score to the collector. This is applied by source type, including FFA pickups temporarily assigned to a recipient. Hero, projectile and station attribution remains intact.

Lifesteal records the health returned by AddHealth in HealPlayerByHero/HealedByHero. Max-health clamping and zero-gain heals are respected. Existing match archives aggregate these counters; no schema change or retrospective invention of old totals.

Validation: Release LifeStealFixture passes 20 checks, HealAttributionFixture passes 17 checks, including real GameZone.ApplyInstEffect attribution dispatch. No live deployment or server restart performed. Client spectator/hover/Jinx target-cycle changes are maintained separately in the Unity client repository.
