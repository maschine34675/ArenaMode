# Protected PFT combat on coverless ported maps

2026-10-09. Follow-up to `PFT-PORTED-MAPS-REVIEW.md`; fixes combat faults found
after the four companions successfully arrived. This adapter does not change
spawn admission, team identity, inventory, score or persistence.

## Evidence and native contracts

GPU-2 log `LogOutput-SPT-GPU-2-2026-10-09_18-22-47.log`:

- Bay5 Zombie, 18:31:35.122, and Equator Zombie, 18:34:08.269: PFT's cover
  selection calls `CoverPointMaster.GetCoverPointMain2`, then
  `CoverFindGraphSearchData.TryAdd` dereferences a missing start node.
- Equator Zombie, 18:34:04.498: infected hearing/goal selection calls
  `BotPathFinderCorePoints.CalculatePathFull`; its failed direct path falls back
  to a missing cover node. This is a different native path from PFT cover search.

The installed PFT 0.10.3 decompile is under
`artifacts/zombie-voice-pft-20261009/sain-analysis/pft-installed/`.
`pitTeam.Utils/Covers.cs:230-255` accepts a null primary cover and an empty list.
Its separate `FindPointForAssault` at line 538 also calls the same native cover
entry, so intercepting only `GetVanillaBackedCandidates` would be incomplete.
PFT source was inspected for contracts, not copied or bundled.

Authoritative native sources under `../_decompiled/`:

- `CoverFindGraphSearchData.cs:25-43`: absent start node reaches `e.Id`.
- `CoverPointMaster.cs:207`: nullable `GetCoverPointMain2(CoverSearchData)`;
  its later voxel fallbacks also assume authored cover nodes. Merely guarding
  `TryAdd` would leave those later dereferences exposed.
- `BotPathFinderCorePoints.cs:164`: the vector `CalculatePathFull` first tries
  `FindPath`, then honors `onlyShortTrie`, then enters cover-based reconstruction.
- `BotPathFinderCorePoints.cs:442`: `FindPath` requires a complete Unity NavMesh
  path. `GoToPosition` at line 521 forwards `onlyShortTrie`; its separate
  `mustHaveWay` failure branch teleports through another unavailable cover node.

## Bounded behavior

`PitFireTeamPortedMapCombat.Configure(Harmony)` resolves both exact native
signatures before applying either patch. Integration calls it only after the
existing exact PFT version validation. Both prefixes run at `Priority.Last` and
respect `__runOriginal`, leaving an earlier mod's cancelled call/result intact.

Every changed call requires active Arena, an authenticated PFT roster, no
protected finalization, frozen Tactical or Zombie mode, a Started LocalGame,
the exact active world/game/controller, and a currently registered live AI body
whose AI owner and world still match. The map must be exactly `arena_bay5` or
`arena_equator`, and the real controller cover list must exist and contain zero
points. Missing/malformed cover data, a different map, ordinary/solo raids,
loading/stopping, stale bodies and maps with real cover points retain native
behavior. No global data, game flag or cover list is modified.

Cover search additionally requires the authenticated companion identity, its
exact `CoverSearchInfo`, and its own group's cover master. It returns null,
which means no authored cover, and leaves PFT's subsequent combat decisions to
PFT. Ordinary enemy cover queries are not intercepted. No fabricated cover or
guessed node is supplied.

Movement applies to every exact live AI body in the protected raid, including
infected: the observed second fault was infected movement, so a companion-only
gate would not solve it. The pathfinder must also hold the exact controller
cover data. The prefix changes only `onlyShortTrie=true` and `mustHaveWay=false`.
The native method still owns movement, posture, direct NavMesh search and its
result. An incomplete direct route fails normally before the unavailable cover
fallback; there is no invented success, partial-path conversion or teleport.
The independent native `slowCalcUsingNativeNavMesh` option remains untouched,
including its existing acceptance of partial paths. Each intervention logs at
most once per world; the diagnostic retains only a weak world reference.

## Four review passes and offline verification

1. **Lifecycle/body ownership:** checked authentication, activation/finalization,
   Started status, exact game/world/controller and registered physical identity;
   negative controls include same-profile replacement bodies and every excluded
   mode/map/status. The adapter creates no asynchronous work or retained bot state.
2. **Cover semantics:** verified null is the actual native return contract and
   accepted by installed PFT; both PFT cover callers converge at this native seam.
   Exact cover-master/search identity and zero-cover conditions are exercised.
3. **Navigation semantics:** compared the actual native branch order; normal
   complete-path success remains before the short failure return. Both missing
   cover fallbacks must be avoided together. The harness's simplified branch
   model is explicitly a model, not native IL execution or NavMesh acceptance.
4. **Inter-mod ownership/regression:** late Harmony prefixes preserve earlier
   suppression/results; the source-linked harness exercises these prefixes,
   bounded diagnostics and exclusions. Mandatory source checks tie the relevant
   native/PFT assumptions to the locally installed decompilation. Compiled
   production mutations test that removing each important guard causes a named
   assertion failure; a final clean run verifies the unmodified production file.

Run `tools/PortedCoverChecks/Verify.ps1`. The project links the actual production
adapter and uses minimal game/Harmony stubs. It does not build or deploy the main
client. The final run passed **150 assertions and rejected all 25 compiled
faults** at their expected named assertions; the clean production run also
passed after the mutations. Logs and mutant sources are in
`artifacts/ported-cover-checks/4499eeb3952c42c898ec00b58493c903/`.
Independent source review by the PFT implementation worker and parent found no
P0–P2 issue in the current adapter, including native branch semantics and the
late Harmony ownership gates. Actual client binding/build and fresh
Bay5/Equator combat tests remain separate from these offline checks; no GPU
acceptance is claimed here.

## Separate shutdown finding

At 18:34:26.400, `BotMemory.GoalTargetBeingHitAction` throws during melee damage,
192 ms after native `Stop(Left)` committed at 18:34:26.208. Native
`EFT/BotMemory.cs:803` reads the melee manager and `GoalEnemy.Person` in a damage
subscriber; the stack does not identify which reference expired. This is
consistent with an exit/teardown race, not either coverless-map fault. This
change deliberately neither swallows damage exceptions nor alters that handler.
