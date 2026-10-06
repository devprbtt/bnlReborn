# Paradise dynamic weather

Separate map ID `map_sr2_paradise_weather_test` (stable ID retained from the private test); public title **Paradise - Dynamic Weather**. Adds to Casual and Custom; preserves every existing rotation entry and Ranked.

Requires client beta.52 or later before activation. Optional ZoneUpdate bit 11 carries the first-assault epoch. Daytime is held before combat, full night at 20 minutes, daylight again at 25 minutes, repeating. All five recovered weather presets blend smoothly; reconnects retain the same clock.

The map payload is an exact geometry clone of recovered Paradise. Live production had no Paradise card or payload when audited on 2026-10-06. See provenance.json for source hashes.

Thumbnail: `paradise-v1.jpg` is unmodified frame 00000 from the textured in-engine Paradise capture. SHA-256 bb86fe563e94c4116149dcaf8e58a2db8279af79a9604e1bbfcfb10112cb5f6e. Hosted at https://blocknload.cc/images/maps/paradise-v1.jpg. The recovered Gyazo URL returns HTTP 503. `tools/fix_paradise_thumbnail.py --apply` updates only image/large_image on the two live cards with a rollback snapshot; no client release or restart.
