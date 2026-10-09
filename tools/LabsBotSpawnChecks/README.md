# Labs and Factory spawn checks

Run from the ArenaMode repository:

```powershell
dotnet run --project tools/LabsBotSpawnChecks/LabsBotSpawnChecks.csproj -c Release
pwsh -NoProfile -File tools/LabsBotSpawnChecks/Verify-Regression.ps1
pwsh -NoProfile -File tools/LabsBotSpawnChecks/Verify-FactoryRegression.ps1
```

For acceptance against an installed database, require its presence explicitly:

```powershell
dotnet run --project tools/LabsBotSpawnChecks/LabsBotSpawnChecks.csproj -c Release -- --require-factory-database
pwsh -NoProfile -File tools/LabsBotSpawnChecks/Verify-FactoryRegression.ps1 -RequireInstalledDatabase
```

`SPT_DATABASE` overrides `D:\SPT41\SPT_Runtime\SPT_Data\database`. Required mode
fails if that directory is absent; it cannot pass by skipping the installed
fixtures. `FACTORY_DAY_NIGHT_SPAWN_PARITY_PASSED` is emitted only after both run.

This console harness compiles the actual `ArenaBotSpawnFilter`,
`ArenaBotSpawnPatches`, `BurstSpawnPointSelector`, `ArenaTeamSpawnPointsPatch`, and
shared `ArenaBotSpawnPolicy` sources. It invokes the
private production Harmony prefix/postfix methods by reflection. Game, Unity,
Harmony and logging types are explicit stand-ins; no game, GPU, server, profile
or deployment is needed.

The checks cover:

- Registration and exact signatures of all four spawn hooks against stand-ins
  copied from the installed game's decompiled method declarations.
- Armed/loading and active Arena Labs, ordinary raids, stale map state with both
  Arena flags off, and other Arena maps.
- Removal of Gate1/Gate2 while keeping all three normal Labs zones. Outside the
  restricted session, the original enumerable and list objects pass unchanged.
- Direct AI selection of a gate, mixed selector results, and forced dispatch
  through the final bot-only boundary. Rejection keeps whole batches paired
  with their profile counts rather than shortening caller-owned lists.
- Unchanged point identity, category, blocked status, cooldown, core ID,
  position, and shared player collection. Player-tagged markers passed into a
  bot dispatch are still rejected at the bot boundary without editing them.
- Null results, safe fallback requests, and reuse of shared creation data after
  a rejected request.

`Verify-Regression.ps1` separately disables exclusions in a temporary copy of
the shared policy and disables the session guard in a temporary copy of the
client filter (with the real shared policy). Both must fail at the armed/loading
zone-filter boundary, before direct policy assertions. The script then reruns
the unchanged production checks. Source files are never overwritten;
temporary mutation/build evidence stays in the printed directory.

## Factory exact-point coverage

`FactoryChecks.cs` links the production methods instead of copying their logic.
The complete harness passed 1105 assertions on 2026-10-09, including these cases:

- Both Factory variants reject the ten exact IDs documented in
  `FACTORY-SPAWNS-REVIEW.md`, including a Player-only marker passed through bot
  dispatch. Neighboring, partial, null and
  unrelated-map IDs are retained. Shared `IsRestrictedMap` stays Labs-only because
  server callers use it for whole-zone handling; Factory uses `HasPointExclusions`.
- Whole mixed batches are denied before native accounting without changing the
  caller's list, profile data, point identity, category, cooldown or other fields.
  Factory zone enumerables/CSV preserve their original identity and formatting.
- Planning removes the unsafe point before position deduplication, still fills
  five safe slots across preferred/distant bands and does not count a unique unsafe
  position toward squad capacity. Other maps and ordinary raids remain unchanged.
- The protected Tactical/Zombie collection postfix removes the exact leader marker in
  armed/loading and active Factory, using a copied dictionary and the original
  logger/remaining marker references. Original collections and markers are intact.
  Solo (including Zombie), TDM, other modes/maps, stale sessions, foreign worlds/games and mismatched
  map IDs pass through. A newly armed mode takes precedence over stale active mode.
- Installed `locations/factory4_day/base.json` and `factory4_night/base.json` each
  contain 145 records. All ten targets have matching IDs/coordinates in both
  variants, are Player-only and have no bot-zone name. Running the actual postfix
  retains all 135 other physical objects and leaves both original 145-entry
  collections unchanged in armed/loading and active protected Tactical/Zombie. Every
  actual unsafe point is also rejected by AI selection and owned TDM/Tactical/Zombie
  dispatch; every other installed point passes these boundaries. Ordinary raids
  retain their original collections, choices and point state.
  Fixtures use `SPT_DATABASE` or the local SPT runtime database; an absent database
  is explicitly reported as skipped on other hosts. Both ran on this host.

`Verify-FactoryRegression.ps1` rejects fourteen temporary production-source mutants:
disabled point policy, missing Factory night classification, a night-only gap for
one underground marker, disabled planner filter, unsafe capacity, leaked team
view, stale-world admission and reversed armed-mode precedence, omission of each
of the four newly identified courtyard IDs, omission of Zombie leader protection
and leakage into unprotected solo play. The night-only
gap must fail the installed night collection assertion when the database is
available; the optional database-free run detects it through the shared policy.
Every mutant must fail
its named behavior assertion, never merely compilation. Production source hashes
must remain unchanged, and the script finishes with a fresh production baseline.

Native declarations were read in `../_decompiled/EFT.Game.Spawning/`:
`ISpawnPoint.Id`, public `SpawnPointsCollection.CreateFromScene(DateTime?,
SpawnPointParams[])`, `_dictionary: Dictionary<ISpawnPoint, SpawnPointMarker>`,
`_logger` and its two-argument constructor. The stand-in models collection/index
identity, not Unity marker objects. The independent root review verifies installed
assembly signatures, registration, entry/respawn consumers and actual deployment.

Limitations: this does not execute Unity, Harmony detours, actual spawn cooldown
updates, live asynchronous bot creation, AI navigation or player spawn selection.
The accounting stand-in observes whether the production prefix allows the
native boundary; it does not claim to reproduce the native bot lifecycle. Real
method binding must also be checked against the deployed DLL, and a raid remains
necessary to validate actual placement and normal-player behavior.

## Bounded-area consumer contract

`AreaChecks.cs` additionally exercises the production AI result filter and
native dispatch prefix with explicit external area verdicts: loading/preparing
must reject before counters, outside/disconnected or null/empty batches must not
use native fallback, and accepted one-point requests retain their accounting.
Ordinary/full-map calls remain unchanged. `AreaStandIns.cs` supplies readiness
and point verdicts; these tests do not implement or prove scene/NavMesh behavior.
