# Discord bot reconnect fix provenance

Derived from `extracted/work/discordbot-audit-20260831/source/src`, which was
extracted from read-only `G:/Downloads/src.zip` as recorded in
`extracted/migration-inputs/discord-bot-20260831/SOURCE_MANIFEST.md`.

Original main.rs SHA256: 4D3A9BF1774217EAC0DEC002201B2BCAF63A33170BD5C7BCE4248C7E9EB322E3
Unchanged Cargo.lock SHA256: 990DAEB05FAE7A0E8AD95187AE3AEDA3C34E0CD7C4FF0380E84B8819B6D5E84E

No environment files or tokens copied. READY and RESUMED replace the polling
task/context, invalidating its presence cache. Unchanged presence is reasserted
at most once per minute; gateway stage transitions are logged without secrets.
