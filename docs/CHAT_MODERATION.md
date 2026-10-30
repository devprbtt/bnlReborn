# Timed chat restrictions and legacy impersonation prevention

Player-originated global, room, private and clan messages pass the same moderation gate. Rich-text brackets, legacy alert-marker braces, newlines and Unicode direction/format controls are neutralized before relay. This prevents the legacy `player_alert_mark` path from suppressing the authenticated sender prefix. Internal server service notifications retain their existing presentation. Formatting attempts are recorded with the authenticated account ID and JSON-escaped payload.

Operators may atomically replace `Configs/chat_bans.json` with an array of records:

```json
[{"PlayerId":123,"UntilUtc":"2030-01-01T12:00:00Z","Reason":"Impersonating another player in chat."}]
```

The restriction uses the stable account ID, survives restarts, reloads on file changes, and expires at the UTC instant. It blocks sending chat only; login and gameplay remain available. Invalid updates retain the last valid restrictions. An empty array removes restrictions. Account-targeted native warning popups are queued at login and sent on blocked chat attempts (at most once per ten seconds). Existing control-panel player notification endpoints can deliver an immediate warning to an already-online player.

No automatic ban is inferred from text: operators must verify the sender from protocol/log evidence. Production account IDs and live moderation state belong in private operational configuration, not in this document or tests.

Validation: `dotnet run -c Release --project tests/BNLReloadedServer.ChatModerationFixture` (19 checks), plus `tests/BNLReloadedServer.ClanFightFixture` (217 checks). The moderation fixture covers the observed marker attack, normal text, multiline/bidi controls, stable identity, exact expiry, restart persistence, malformed/null config entries, and all four chat ingress routes.
