# All-map phase lighting and one-second deploy countdown

## Behavior

Arena's existing additive phase light now supports every currently admitted
Arena location: Labs, Factory day/night, Labyrinth, the selected Customs area,
Equator, Bay5 and AirPit. The previous whitelist covered only the first four
location identifiers. Unknown identifiers remain rejected; map admission and
Customs contour validation remain owned by the normal Arena selection contract.

The director is map-independent: one temporary directional light belongs to the
exact active `GameWorld`, with shadows and bounce disabled. It does not modify
existing lights, scene materials, lightmaps, global lighting settings or map
bundles. Existing configured friendly/opponent colors, intensity, timing,
Blackout/Going Dark suppression and teardown behavior are unchanged. Actual
visibility can differ with a map's brightness and materials.

With FastDeploy enabled, the native local pre-raid countdown now lasts one
second instead of two. This does not shorten mode-specific roster assembly,
Tactical buying or combat-start countdowns.

## Native countdown review

The installed decompilation's `EFT/LocalGame.cs`, `vmethod_2`, has the single read
of `GlobalConfiguration.TimeBeforeDeployLocal`. It snapshots that duration for
the final-countdown screen, audio fade, delayed menu-music stop and
`WaitForSeconds`. The patch keeps that coroutine intact and retains a positive
duration for the native fade.

The existing narrow mutation/restoration contract is unchanged: armed Arena
loading plus enabled FastDeploy are required; native `vmethod_6` and Arena
teardown restore the saved value. The next countdown heals an interrupted
previous override before checking arm or setting state, so an aborted Arena
load cannot shorten a later ordinary raid. Existing zero/one-second values are
not increased.

## Verification

`tools/ArenaLightingChecks` links the real policy, director and deploy patches.
It runs owned-light lifecycle/cleanup and ordinary-raid isolation checks on all
eight supported location identifiers, retains effect/color/external-owner
coverage, rejects unknown map variants, and checks the one-second override,
completion/abort restoration, disabled/ordinary isolation and shorter defaults.

Command: `dotnet run --project tools/ArenaLightingChecks/ArenaLightingChecks.csproj -c Release`.
Result: `ARENA_LIGHTING_CHECKS_PASSED (498 assertions)`; exit code 0.

Offline checks do not certify rendered brightness, spectator presentation or
audio fade behavior in the native game. Build/deployment and GPU acceptance
are recorded separately by the integrating session.
