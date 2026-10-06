# Paradise dynamic weather

Separate map ID `map_sr2_paradise_weather_test` (stable ID retained from the private test); public title **Paradise - Dynamic Weather**. Adds to Casual and Custom; preserves every existing rotation entry and Ranked.

Requires client beta.52 or later before activation. Optional ZoneUpdate bit 11 carries the first-assault epoch. Daytime is held before combat, full night at 20 minutes, daylight again at 25 minutes, repeating. All five recovered weather presets blend smoothly; reconnects retain the same clock.

The map payload is an exact geometry clone of recovered Paradise. Live production had no Paradise card or payload when audited on 2026-10-06. See provenance.json for source hashes.
