# Factory day/night team-spawn exclusions

## Courtyard follow-up 2026-10-09

The user's marked screenshot (`../Screenshot 2026-10-09 111002.png`) identifies
the enclosed courtyard beside Main Office / Woods transit. There are **five**
Player markers there, not one: the previously excluded `c671337f...` and four
more at the following coordinates. All five records are identical between the
installed day and night databases, Player-only, with an empty bot-zone name.

| Courtyard marker | X | Y | Z | Horizontal distance from user's X13.10/Z59.34 |
| --- | ---: | ---: | ---: | ---: |
| `4755c538-4ee5-4125-8cd3-885787da7ad5` | 12.6300011 | 1.797 | 60.15 | 0.94 m |
| `c671337f-e58f-4de6-a387-307d877954a3` (already excluded) | 14.018 | 1.18999994 | 58.119 | 1.53 m |
| `6d97a439-de10-42c5-99ef-6cf92e243aca` | 10.2300014 | 1.14 | 58.7600021 | 2.93 m |
| `e60d8dce-ee35-4b30-9c3a-afddf4c5445e` | 13.76 | 1.1 | 54.64 | 4.75 m |
| `bcae7a9f-8b2f-4a4c-9cb0-589d4a227c70` | 12.5600014 | 1.21 | 52.73 | 6.63 m |

The screenshot is a rotated map, not a world X/Y plane. Installed DynamicMaps'
`Factory_TarkovDev.jsonc` specifies `CoordinateRotation: 90`; its reference
`MathUtils.ConvertToMapPosition` maps Unity `(x,y,z)` to map `(x,z,y)`, and
`CursorPositionText` reads the unrotated map-local cursor coordinates. Thus the
user's two numbers correspond to world **X/Z**, and the screenshot's ground
floor rectangle includes all five markers. Its edges are only visual evidence,
not an exact surveyed volume or a new runtime rectangle rule. The nearby Bot
marker `ff9c3127-2acd-48fe-a635-b1ab99d0bfa7` at `(2.1,1.1,63.06)` lies below
the marked courtyard in this view and remains available, as do all other
non-excluded markers.

The four newly identified exact IDs join the existing six-entry policy. The
same policy now rejects ten IDs on both variants, retaining the other 135 of
145 installed markers. No coordinates, categories or installed data are changed.

A read-only live Factory scene inventory on GPU2 also found exactly these five
markers in the broad inspection window X7..36 / Z43..67. Their scene names are
`PMCspawn_exp_Area_01_C (1)` through `(5)`, all Player-only / All sides / Factory
infiltration, without a bot zone. No additional runtime spawn marker appeared
there. This inventory used the previously deployed `ecc22d5` client and proves
the scene identities, not gameplay acceptance of this new filter. A positive
NavMesh/room dry-run at the old marker proves local placement only, not an exit
path from the pocket.

There was also a separate Zombie-team gap: `ArenaTeamSpawnPointsPatch` admitted
only Tactical. Protected Zombie teams use the same PFT leader placement, so
the existing confirmed-roster guard now supports both Tactical and Zombie.
Solo Zombie / solo Tactical / independent TDM player spawn views stay native;
ordinary raids and foreign/stale worlds retain their exact original collection.
This protects initial and newly selected respawn markers. PFT followers rebuilt
at a living leader who manually entered the courtyard can still appear there;
this change does not turn the screenshot into a movement barrier.

Zombie infected use `ZombieSpawnService.TryRequest` ->
`BurstSpawnService.SpawnInfected` -> `SpawnPair` ->
`BurstSpawnPointSelector.PickPoints`, with the same final bot dispatch filter
used by Tactical/TDM. PFT companions instead call the native PFT
`SpawnGroupBots`, which captures `player.Position` and passes it to
`ActivateBotAtPosition` (reviewed 0.10.3 reference lines 909 and 1135). Hence
filtering only bot-zone markers could never protect the followers' courtyard
entry. No whole BotZone or unrelated patrol route is excluded.

Production-linked checks passed **1105 assertions** with both installed
databases required. They exercise Tactical/Zombie leader views during loading
and active play, all ten IDs at AI selection and owned Tactical/TDM/Zombie
dispatch, and retention of every other installed marker and original object.
The Factory regression script rejected **14 compiled mutants**, including omission
of each new courtyard ID, omission of Zombie leader protection, and leakage into
unprotected solo play, alongside the earlier eight controls. Native raid acceptance must
be recorded separately; these stand-in checks do not prove navigation.

## Verified 2026-10-08

Historical verification below predates the marked courtyard follow-up above.

The suspected missing Factory night exclusions are already present. The installed
day and night location databases both contain the same six unsafe marker IDs at
the same physical coordinates. `ArenaBotSpawnPolicy.HasPointExclusions` includes
both maps and `IsExcludedPoint` applies the same exact-ID set. No production
change, new exclusion, database edit or widened spatial rule is needed.

The installed `SpawnPointParams` arrays are identical (145 records each). The
protected Tactical scene view keeps all 139 other markers, including neighboring
markers; every ordinary-raid marker remains available.

| Unsafe physical location | ID in both variants | X | Y | Z |
| --- | --- | ---: | ---: | ---: |
| Courtyard near Woods transit | `c671337f-e58f-4de6-a387-307d877954a3` | 14.018 | 1.18999994 | 58.119 |
| Underground group | `c2d01ce0-e0e4-41af-8311-44dc5cf38a93` | -6.47099972 | -2.598 | 15.79 |
| Underground group | `97998a75-baa4-4b03-ac1f-f86dfbf48d79` | -6.689 | -2.598 | 17.303 |
| Underground group | `ea010f6f-4860-4339-b576-09439f8f4c3a` | -5.58 | -2.598 | 14.757 |
| Underground group | `8128ea83-0d4f-4e64-b109-5acc9e477b33` | -4.302 | -2.598 | 14.318 |
| Underground group | `71c2ba1e-e226-47be-814f-d52ddd931603` | -2.81400013 | -2.598 | 18.476 |

All six installed rows are Player-only, without a bot-zone name. They therefore
cannot be addressed by banning a whole bot zone. The fixture pins each ID and
position separately; replacing a variant's ID or moving a target breaks the check.

## Consumers reviewed

- `ArenaLocalRaidStartedPatch` passes `settings.location` to
  `ArenaSession.MarkArmed`; the selected night ID is retained. Native
  `../_decompiled/EFT/BaseLocalGame.cs:525` sets the current world's location ID
  before `SpawnPointsCollection.CreateFromScene`, then uses that collection for
  the local player's selection. There is no day-only branch at this seam.
- `ArenaTeamSpawnPointsPatch` filters the returned collection for protected
  Tactical only. Its shared point guard checks the armed/active Arena state,
  exact world/map and, once active, exact game/world identity. The PFT follower
  path uses the leader's position directly (reviewed reference 0.10.3
  `client/Patches/BotsControllerPatch.cs:909,1135`); excluding the leader's
  initial marker is what protects those companions. Redeploy's
  `SpawnSystemCache.GetOrCreate` also calls `CreateFromScene`, so its cached
  respawn selection receives the same protected view.
- Tactical opponents call `BurstSpawnService.SpawnSquad`; both USEC and BEAR
  TDM refill requests call `SpawnFactionBot`. Full-map requests share
  `SpawnPair` and `BurstSpawnPointSelector.PickPoints`; capacity and selection
  both call `ArenaBotSpawnFilter.IsExcludedPoint`. The final
  `SpawnBotsInZoneOnPositions` prefix rejects an unsafe batch before native
  counters. TDM's dispatch ownership does not bypass the exclusion.
- Native `CreateFromScene` applies `FixSpawns` only when export dates permit,
  then returns the marker index
  (`../_decompiled/EFT.Game.Spawning/SpawnPointsCollection.cs:152`). Arena filters the final returned view, keeps
  the native marker objects, and never edits their categories, blocked flag or
  source dictionary. It does not rewrite the installed database.

The local human's ordinary full-map TDM selection is not a protected PFT leader
selection. This review preserves that existing scope; it does not broaden the
team-bot exclusion into a general player-spawn prohibition.

## Executed evidence

From the repository root:

```powershell
dotnet run --project tools/LabsBotSpawnChecks/LabsBotSpawnChecks.csproj -c Release -- --require-factory-database
pwsh -NoProfile -File tools/LabsBotSpawnChecks/Verify-FactoryRegression.ps1 -RequireInstalledDatabase
```

The updated production-linked harness passed 533 assertions. It loads both
installed databases, pins all six coordinates, exercises armed/loading and
active protected leader views, AI selection and owned TDM/Tactical dispatch,
and preserves all other actual markers and ordinary-raid choices. The required
database option fails instead of skipping if the database is absent.

Eight temporary source defects were rejected at their intended behavior
assertions, including removal of night classification and allowing just one
underground marker on night. That latter fault fails against the installed
night collection, while day still passes. Production source files are snapshotted
and hash-checked; mutants never overwrite them. The script ends by rerunning
the unmodified baseline.

Installed base-file SHA256 at review:

- Day: `9826CA3EFFD4F925E8DC123CA1BAFD8469B24BEDF3C0F2C2D41270550ABA430D`
- Night: `7FC5266543D23D8140CE6166C76BC0E119E1004BE210B7C8EAFBFD0AD50CAB60`

These checks execute linked production methods against explicit EFT/Unity
stand-ins and installed location records. They do not execute Harmony detours,
the actual Unity scene or AI navigation. No new build, deployment or native raid
acceptance is claimed. A newly reported stuck position would need its own
coordinate/marker evidence rather than expansion of the current exclusions.
