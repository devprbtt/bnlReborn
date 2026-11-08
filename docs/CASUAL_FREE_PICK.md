# Casual simultaneous selection

Prepared 2026-11-07; not deployed.

Casual matchmaking (`game_mode_friendly`) uses the existing FreePick lobby protocol. Everyone selects together during a shared 60-second window, with their last-played hero and loadout available immediately. All players locking in ends selection early; otherwise the timer advances to the five-second prestart countdown. Map voting remains available. Duplicate heroes remain allowed; draft turns, bans and forced draft class coverage do not apply. Ranked retains its draft and restrictions.

Registration normalizes both full catalogue loads and individual card updates. GameLobby creates casual draft state only for an explicitly draft-configured casual card, so the free-pick mode sends no draft extension/UI state. Backfill retains a separate 60-second selection window. Existing clients already understand FreePick; no client update or CDB write is needed.

Validation commands (from server repository root):

```powershell
dotnet build BNLReloadedServer/BNLReloadedServer.csproj -c Release -warnaserror
dotnet run --project tests/BNLReloadedServer.CasualDraftFixture -c Release
dotnet run --project tests/BNLReloadedServer.PublicQueueRulesFixture -c Release
dotnet run --project tests/BNLReloadedServer.BackfillReentryFixture -c Release
dotnet run --project tests/BNLReloadedServer.RandomHeroesFixture -c Release
dotnet run --project tests/BNLReloadedServer.MapVoteFixture -c Release -- <catalogue-json-export>
```

Deployment remains separate: drain active matches, deploy the prepared server build, restart the service, then verify a fresh casual lobby has simultaneous selection and no draft overlay; verify ranked still drafts. No live multiplayer acceptance test has been performed. Existing connections need to reconnect and receive the new server catalogue. Rollback restores the preceding server release and restarts; no database migration is involved.

Player-facing note for deployment: “Casual now lets everyone choose their hero together. Pick and lock in during a shared 60-second selection window, with no draft turns or forced class coverage. Ranked is unchanged.”
