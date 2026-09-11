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
values or client files changed.

Deployed runtime commit a02d26245a11efa01eaf33a968c485412d6255f0 on 2026-09-11
03:01 UTC. Verified DLL/PDB hashes, signed authentication, startup and all three
game listeners. Follow-up: active/running, NRestarts=0, no error-priority journal
entries. Previous release a50c85bfcb74 retained for rollback. No catalogue edits.
In-match behavior still needs a live check.

