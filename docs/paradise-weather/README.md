# Paradise - Dynamic Weather Test

Custom-only map ID: `map_sr2_paradise_weather_test`. The payload is a separate clone
of recovered Paradise, with identical MapData (terrain, colors, spawns, units,
water and rules). The input paths and hashes are in `provenance.json`.

The matching Unity client blends warm daylight (0:00), cold daylight (5:00),
overcast (10:00), sunset (15:00), night (20:00), and warm daylight (25:00), then
repeats. Transitions use smooth interpolation throughout each five-minute leg.
The build phase stays in daylight. The clock starts at the first assault and
continues across later phases. The 25-minute duration and five-minute return
were explicitly selected by the owner.

`ZoneUpdate` optional bit 11 carries `WeatherStartTime`, a signed 64-bit Unix
millisecond timestamp, after BlockOwnersJson. It is emitted only for this map
after combat starts, in phase updates and initial/join/reconnect snapshots.
Ordinary maps retain the previous wire layout and behavior. This experiment
requires the matching client; do not offer it to unupdated clients.

Registration adds the separate map only to custom lists when its payload exists.
It leaves the original map and public/ranked matchmaking lists unchanged.
This code does not change match duration, objectives or victory conditions: a
match may end before the 20-minute night milestone.

From the repository root:

```powershell
dotnet run --project tests/BNLReloadedServer.WeatherFixture -c Release
dotnet build BNLReloadedServer/BNLReloadedServer.csproj -c Release -warnaserror
```

The client installer and render validation are in the Unity upgrade repository:
`tools/install_paradise_weather.py` and `ParadiseWeatherValidation.Run`.
The latter checks sky endpoints against the original materials, cycle boundaries,
client wire decoding and a neutral-material occupancy preview of the actual map.
This preview is not an in-game visibility or performance test.

For a hosted test, deploy the matching server and this separate Maps payload,
then use the private weather client and select this map in Custom Game. No live
deployment or public client release is part of this change. Server deployment
requires separate owner authorization. Multiplayer, water/material reflection,
respawn/spectator and 25-minute wall-clock playtests remain to be performed.
