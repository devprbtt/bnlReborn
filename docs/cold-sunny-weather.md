# Fixed Cold Sunny maps

All map loads use DaytimeCold (winter terrain suffix retained); all catalogue map cards advertise DynamicWeather=false, including hot updates. Weather epochs are absent for all joins/reconnects/phases. Legacy Paradise keeps its ID and geometry but loses the weather label.

Requires the matching client ZoneBuild/DynamicMapWeather change: released clients hardcode the old Paradise exception. Do not claim server-only activation removes every cycle. Prepared, not deployed.

Validation: StaticWeatherFixture 50 checks; server Release build with warnings as errors, zero warnings/errors. Client Unity validation passed 18 checks in the companion client repository.
