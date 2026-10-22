# Bowl Arena admission

Date: 2026-10-23. Implementation starts from `ba016ba`. This adds `arena_bowl`
as an optional ArenaMapPort location; it does not distribute or change the map.

## Behavior

- All five current modes, including protected PFT Tactical and PFT Zombies, can
  select Bowl when the client plugin and enabled server location are present.
- DM/Gun Game and independent Team DM recommend eight total participants, with
  the existing generic count choices (2..24 solo, even 4..24 TDM). This is menu
  capacity, not a performance or collision guarantee for maximum populations.
- The picker shows **BOWL** in gold, preserving MapPort's native placement,
  thumbnail and time/weather selection. Closing Arena restores native labels.
- Pickups use only the existing Bot/BotPmc coordinates in live DM/TDM, with the
  usual exact-raid, floor, NavMesh, spacing and occupancy checks. Installed data
  yields 16 separated candidate positions; no positions are invented. Custom
  Pickups.json still replaces the built-in layout. Phase lighting includes Bowl.
- Both PFT coverless adapters accept the exact Bowl ID. Their authenticated
  roster, exact world/game/body/controller, loading/live, real-core and empty
  cover checks are unchanged. Normal raids never acquire these fallbacks.

## Migration and isolation

Optional-map revision 4 extends the exact former AirPit default once. Earlier
defaults retain their original revision cutoffs. Custom lists, earlier optional
map removals, disabled Bowl entries and existing per-map tuning remain owned by
the user. Removing Bowl after this upgrade persists. Malformed/future server
revision markers do not re-enable removed maps. Client binding uses the same
revision policy, without replacing unrelated BepInEx settings.

The server discovers Bowl from its live database on each request. Missing,
disabled, wrong-ID or unavailable locations are omitted only from the detached
client-settings response; no source configuration or location row is changed.
Start-route checks retain the snapshot/idempotence contract. Native PMC/Scav
selection continues through the ordinary path.

## Spawn evidence and original-box boundary

The installed MapPort 0.3.0 data supplies four Player and seventeen Bot/BotPmc
markers in `BotZone_Main`, with nineteen real AI core points and no baked cover
points. `D:/ArenaMapPort/tools/maps/bowl.bots.json` excludes the core in the west
pocket and the east gate-line core from bot starts. MapPort's default
`OpenSpawnBarriers=true`, `SpawnGateHold=false` parks all four moving parts of
DOORS_SPAWN_A/B open at raid startup when no owner holds them.

This change deliberately does **not** add Bowl to `ArenaTeamBoxCatalog`. It uses
the existing generic spawns, acquires no gate hold, adds no private team-start
markers and grants no box-specific bot spawn shield. Original-box integration
needs separate work: the current MapPort Release/IsHeld contract reports a hold
released before Bowl's opening animation finishes (up to 13.5 seconds), without
a public opening/ready property. Treating that as immediate navigation readiness
would reuse an invalid assumption from the instantaneous gate maps.

Map-side source/previous native evidence is in
`D:/ArenaMapPort/docs/progress/bowl-release.md` and `bowl-source-060.md`.
It is not new Arena gameplay acceptance.

## Verification

Run `pwsh -NoProfile -File tools/BowlChecks/Verify.ps1`. It uses the installed Bowl
data, no-deploy target overrides and an absent deployment sentinel, then runs
server admission/migration, source-linked menu/presentation, actual BepInEx
config, participant, PFT, lighting, spawn-shield and pickup checks. It reuses the
AirPit negative-control runner, now also requiring rejection when either Bowl
PFT adapter is removed from the compiled production source.

Current offline results:

| Check | Result |
| --- | --- |
| Full server/unit suite, including final Bowl start-route cases | 2,222 passed, 1 existing installed-APBS test skipped, 0 failed |
| Native-menu source harness | 842 assertions |
| Real BepInEx config bind/save/reload | 291 assertions |
| Map presentation / ordinary-screen restoration | 4,468 assertions |
| Participant menu | 4,731 assertions |
| PFT startup / coverless combat | 288 / 204 assertions |
| Lighting / pickup runtime | 602 / 506 assertions |
| Non-box spawn-shield exclusions, including Bowl | Passed |
| Compiled missing PFT-adapter controls | All four rejected at intended assertions; production reruns passed |
| Full no-deploy Release solution build | 0 errors; 4 existing client nullable warnings |

Evidence: `artifacts/bowl-integration-20261023/bowl-unit-final.trx`, `build.log`,
and `artifacts/bowl-checks/9e87c11f15cf402797c94ae25a07a091/`. The latter's filtered
75-test run preceded the extra route cases; the subsequent full suite includes
them. Independent read-only review found no blocking issue in the production
scope or migration. No GPU raid, native PFT activation, max-population or visual
pickup acceptance is claimed.

After deployment restart both EFT and the SPT server so the client/server
migrations run. Suggested first native check: select Bowl, verify both times,
start an eight-participant DM, confirm opponents, respawn and pickups, then exit
and verify ordinary PMC selection. Test generic TDM and PFT formats separately.
