# Flexible player search

Profile/friend nickname search now trims the query and matches any part of the stored nickname using OrdinalIgnoreCase, including Unicode letters. Exact matches rank first, then prefixes, then other substrings; ties sort by nickname and player ID. Return at most 50 accounts; blank queries return nothing. Punctuation is literal. Only id, steam_id and username are read; no schema or protocol change. Clan invitation exact/ambiguity behavior remains separate.

Validation: `dotnet run --project tests/BNLReloadedServer.PlayerSearchFixture -c Release` exercises the production database search helper against a temporary real SQLite Users table. No production data is used.

Deployment is separate. Existing clients can show the expanded server results, but the Unity client companion fix is needed for case-insensitive Enter/search-button submission and stale-result clearing.
