# Aura and buff ignore_caster

Aura overlap recipients now honor the aura targeting before entry, exit and
interval processing. Constant buff application checks the UnitSource identity
in both AddEffect and AddEffects, before recording a source or publishing buffs.
An aura remains attached to its emitter: its targeting describes recipients.
Another emitter may still buff that hero. Disabling ignore_caster allows self.

Validation:
`dotnet run --project tests/BNLReloadedServer.IgnoreCasterFixture -c Release -- <catalogue.json>`

Twelve assertions passed for aura targeting, direct/batched buff application,
exit cleanup, other-caster application, rejected self-source bookkeeping,
aura attachment and flag-off behavior. Uses a complete valid catalogue fixture.

Scope: aura recipient filtering and constant buff self exclusion. No catalogue
values or client files changed. Not deployed to the live service yet.
