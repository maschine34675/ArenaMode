# Factory day/night team-spawn exclusions

## Verified 2026-10-08

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

- `LocalRaidStartedPatch` passes `settings.location` to
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
