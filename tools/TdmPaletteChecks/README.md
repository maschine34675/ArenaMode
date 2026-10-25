# TDM faction palette checks

`dotnet run --project tools/TdmPaletteChecks/TdmPaletteChecks.csproj -c Release`

Links the production palette and HDR colors against minimal config/session and
Unity value doubles. Exercises both selected factions, monitor/UI RGB agreement,
custom non-TDM colors, inactive/stale selection, exact game/world replacement,
missing singletons, clearing and absent config.
This does not execute Unity rendering; native checks must inspect the score HUD,
markers, killfeed, tracer/laser colors and map monitors after a side switch.

Expected token: `TDMPALETTECHECKS_PASSED`.
