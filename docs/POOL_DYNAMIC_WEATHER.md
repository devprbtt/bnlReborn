# Dynamic weather on existing maps

Map cards accept `dynamic_weather: true`. Enablement is independent of the map ID and does not change terrain, map files, gameplay mode, or rotation lists. `tools/enable_pool_dynamic_weather.py` defaults to a dry run and targets the distinct union of current Casual and Ranked map cards. `--apply` saves a host-only card backup, uses CouchDB revision checks, and verifies that only the flag changed and the pool document stayed identical.

The flag occupies the previously unused eighth bit in the one-byte CardMap header. It has no additional payload; disabled cards retain their old bytes and old clients ignore the bit. Updated clients use it to install the weather controller before assault. Updated servers replicate the existing first-assault epoch on configured maps, preserving late-join synchronization. Both builds are needed for the full rollout; setting CDB alone does not update shipped client behavior. The existing Paradise weather map remains supported for compatibility.

Paradise fallback registration must not add maps back to Casual; matchmaking pools remain operator-owned. No new map IDs or map payloads are created.

Validation: `dotnet run --project tests/BNLReloadedServer.WeatherFixture -c Release` from the repository root checks configured/ordinary clocks, JSON, card header layout, legacy defaults, phase/late-join epochs and pool preservation.
