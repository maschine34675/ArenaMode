# Pickups on every supported Arena map

The built-in layout now covers Factory day/night, Labs, Labyrinth, the three
selected Customs areas, Equator, Bay5 and AirPit. This replaces the Factory-only
scope described in the original prototype review. Mode admission is unchanged:
live Deathmatch and independent Team Deathmatch only. Gun Game, Tactical,
Zombies, protected PitFireTeam teams and ordinary raids remain excluded.

Factory retains its eight accepted candidate positions and six pickup identities
exactly. `Pickups.json` still replaces all built-in layouts; custom positions,
alternatives, kit selections, spacing and freeze-at-match-start behavior are not
rewritten or supplemented.

For the other maps `ArenaPickupMapLayout` reads the current raid's existing
`LocalGame.Location.SpawnPointParams`. It copies coordinates only: no spawn list,
native marker, BotZone or profile is changed and no spawn system is created.
The same exact clone contains the surveyed Customs Yard's admitted extra rows.
Whole Customs, obsolete/unknown contours and unsupported maps do not gain a
fallback layout. The current contour is checked before seeds enter the pool.

Only native Bot/BotPmc seed categories are accepted. This avoids Labyrinth's
trap-lined player entrances. Known sealed Labs extraction zones, sniper zones,
excluded point IDs and original ported-map team boxes are rejected. A 2.5m box
margin also excludes candidates whose bounded 2m floor repair could enter a box.
Every pool is deduplicated, spatially spread, limited to sixteen positions and
frozen for the match. All six existing pickup types share that pool. Existing
round/60-second/cooldown rotation, ownership and leave/re-enter rules are retained.

Installed-data candidate counts on 2026-10-17:

| Map / selected area | Candidates | Simultaneous pickup entries |
|---|---:|---:|
| Factory day/night | 8 each | 6 |
| Labs | 16 | 6 |
| Labyrinth | 9 | 6 |
| Equator | 9 | 6 |
| Bay5 | 10 | 6 |
| AirPit | 13 | 6 |
| Customs Factory Center | 9 | 6 |
| Customs Factory Side | 12 | 6 |
| Customs Yard | 16 | 6 |

These counts are candidate capacity, not native placement proof. Every actual
appearance still passes nearby NavMesh sampling, supported level floor,
pedestal clearance, complete native path, selected contour and path containment,
and separation from other pickups. Existing bounded retries and the 15-gameplay-
second placement deadline remain. Unsafe or unreachable candidates are skipped;
an exhausted pool never places unchecked pickups or aborts the match. Native
GPU tests must validate placement and collection on the new maps.

The visible beam maximum increases from 4m to 6m, exactly 50%. Ceiling clipping,
occlusion, widths, opacity, geometry ownership, collision and collection range
are unchanged. Low ceilings therefore still produce shorter beams.

## Verification

Use PowerShell 7 (`pwsh`) for the fault runners; Windows PowerShell 5 treats the
intentional failed-mutant stderr as a terminating native-command exception.

```powershell
pwsh -NoProfile -File tools/ArenaPickupChecks/Verify.ps1 -SptPath D:\SPT41
pwsh -NoProfile -File tools/PickupObjectiveChecks/Verify.ps1 -SptRoot D:\SPT41
```

The source-linked checks include the production map adapter, real shared map/
contour/exclusion/box catalogs, installed vanilla/MapPort JSON, and controlled
native physics and world boundaries. They verify all maps, all three Customs
polygons, six kinds, unchanged Factory pools, native-record immutability,
deterministic spread, custom overrides, frozen layouts, excluded boxes and Labs
gates, live navigation refusal, six-metre beam cap and existing ceiling behavior.
Added compiled faults remove new-map wiring, sealed-gate exclusion, box exclusion
and the beam extension. Existing lifecycle, rotation, transfer, audio and bot
objective faults remain. Offline checks do not establish live geometry or visuals.

Completed verification: pickup suite **462 assertions / 37 compiled faults**,
plus 30 wiring contracts; objective suite **597 assertions / 12 compiled faults**.
The final F12 enable-setting description was corrected after those fault manifests;
it is a text-only change, followed by fresh production baseline runs. Evidence:
`artifacts/pickup-checks/16e2dfcee99b4596a706a53999d3eaf1/manifest.json` and
`artifacts/pickup-objective-checks/98768a937c894ac5bd8656a93f0ea2f9/manifest.json`.

Native members were checked against local decompilation:
`EFT.Game.Spawning/SpawnPointParams.cs`, `ESpawnCategoryMask.cs`, `ClassVector3.cs`.
No optional MapPort assembly API is required; absent ported maps are never offered
by the existing map selection and therefore cannot start this pickup runtime.
