# Tony F scores the caster — 2026-11-23

Tony earns 5 base action-score points for each other allied player who actually receives the timed F building buff at activation. Tony's self-buff awards no points. Recipients receive the buff but no receipt points. For example, Tony plus three affected teammates pays Tony 15 base points; Tony alone pays zero. Existing general objective bonuses remain separate.

Previously each affected player, including Tony himself, received 5 receipt points. This change supersedes that ownership rule. Award through the caster's Unit so score, award events and hero attribution belong to Tony, with the affected unit as target and its position as score location. The breakdown reason is `Build boost applied`. Retain the legacy `TonyBuildBuffReceived` configuration key (default 5) and old reason mapping for compatibility.

Keep the per-cast shared recipient set: one payout per other player per accepted activation, including cloned impacts. Buff ticks, delayed/repeated hit reports, rejected recasts and self-only casts add nothing. Both base and Wider variants use the same behavior. Native buff application, duration/ranges, turret bonus and expiry remain unchanged; no client, CDB or protocol changes are required.

Validation and source commit are recorded in the client workspace's `reports/tony-f-caster-points/`. No deployment is included.
