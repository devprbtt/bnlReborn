# Trondson allied F AOE block damage

Run `dotnet run --project tests/BNLReloadedServer.TrondsonAoeFixture -c Release -p:TreatWarningsAsErrors=true`.

Fixtures derive from read-only live CDB cards captured 2026-10-18, transformed using
`tools/add_trondson_aoe_block_buff.py`; revisions and editor metadata removed.
Original capture SHA256: c77f15360ed31b105458cbaa37d2390881e8d887e50227d598576ced4b34eb11.

The legacy server-only `splash_block_damage_bonus` CardEffect property multiplies
outgoing non-mining splash BlockDamage after existing world buffs. Other damage
channels and radius are unchanged. The existing allied buff icon carries 0.5;
both 12-second auras retain their 10m/16m radii and exclude the caster. Existing
effect membership/removal governs range, expiry, purge and overlapping sources.
No new buff enum or binary field is sent to clients. Devices do not inherit the
player bonus; explosions use the authoritative source's active effects at impact.

Deploy the matching server first, then run the CDB tool read-only to review and
with `--apply` to apply. Applying to an old server has no gameplay effect.
Migration backs up current cards, checks dependencies, uses revision-guarded PUTs,
verifies readback and rolls back only its own revisions on failure. No live change
has been made during preparation. No live multiplayer validation performed.

The fixture also verifies the registered `effect.buffs.splash_block_damage_bonus` entry, JSON parsing, legacy client filtering, new-field precedence, explicit zero, and removal of unused card-level JSON fields.
