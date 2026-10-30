# Area team runtime review and executable checks

2026-09-08; isolated `feature/area-team-setup`, based on `62d610e`.
This is the independent runtime review of the root implementation, not GPU
acceptance or a review of the server/menu implementation.

## Native contract checked

Installed decompilation under `D:\SPT41\Development\_decompiled` confirms:

- `EFT/BaseLocalGame.cs`, `Run`: creates `SpawnPointsCollection` from the scene,
  passes that same object to `SpawnSystemFactory` with `GameWorld` as players,
  and captures that `ISpawnSystem` in its player factory. Initial selection calls
  `SelectSpawnPoint(Player, Profile.Info.Side, ..., Profile.Id)`. The new patch
  resolves the explicit `ISpawnSystem.SelectSpawnPoint` implementation through
  the native interface map. Exact world/collection/profile/category/side guards
  therefore match the actual call chain, not a guessed public method.
- `EFT.Game.Spawning/SpawnPointMarker.cs`, `Create`: a named bot-zone marker is
  added to that zone's `SpawnPointMarkers`. `BotZone.SpawnPoints` projects all
  those markers; it does not remove `BotPmc` categories. `BotSpawner.SpawnZones`
  excludes only sniper zones when called with false. The surveyed
  `ZoneFactoryCenter` BotPmc markers have a native route into these zone arrays.
  Actual populated scene arrays still need runtime inspection; source/database
  agreement alone does not prove live hydration or cache freshness.
- `EFT/BotSpawner.cs`, `method_7`: sends each selected marker position unchanged
  to `BotCreationData.AddPosition`. `BotCreatorClient` consumes its `PositionNote`
  and passes the same position through `LocalGame.CreateBotByProfile` and
  `LocalPlayer.Create`. `Player.Create` assigns `Transform.Original.position`.
  There is no deliberate random position offset on this single-profile path.
  Controller activation/mod hooks can still displace a body; a two-metre arrival
  tolerance is conservative relative to this native chain, not measured GPU
  jitter. Rejected arrivals log requested and actual positions.
- `EFT.Game.Spawning/SpawnPoint.cs` exposes mutable public `Id` and `Position`
  fields. `SpawnPointMarker.FixParams` can update them; a reservation must freeze
  identity/coordinates instead of letting a later change redirect validation.

## Findings raised and addressed by the root

1. Arrival validation initially accepted any connected point inside the area.
   Root added exact requested faction and bounded distance from the reserved
   marker; the pre-existing `HasRequestedFaction` also requires a live matching
   native body before the area check.
2. Area rejection initially omitted pending-ledger release, while earlier stale
   or wrong-faction branches kept the area reservation until timeout. Root added
   the missing release and disposed reservations on those rejection paths.
   The native dispatch boundary retains its earlier exact game/generation/ticket
   checks and distinguishes a provably rejected dispatch from an uncertain one.
3. Readiness initially counted only connected scene candidates. Root now
   intersects actual native dispatch-zone arrays by identity and position. A
   subsequent review found that a distinct blocked zone wrapper could still
   count; root aligned the actual wrapper's category/blocked/exclusion checks.
   This avoids using the active-phase `AllowsSpawn` guard during Preparing.
4. Player preflight initially chose an unoccupied location without reserving it,
   leaving a gap while Redeploy prepared the replacement. Root now reserves
   actual `ArenaSpawnSelector` choices before handover, retains that exact lease
   in `ArenaAreaRespawn.Prepared`, rechecks it at release and drops it only on the
   exact current local player's replacement callback, timeout or reset. In-place
   kit replacements retain their original behavior without such a reservation.
5. Review also identified mutable-marker drift: Prepared freezes its return
   coordinates, whereas a lease originally checked occupancy at the marker's
   later mutable coordinates. Root now captures readonly token, point ID,
   position, side and expiry. ID changes or position drift of one centimetre or
   more expire the lease; occupancy uses the frozen position and disposal uses
   the frozen ID. Separate regressions and faults verify both mutation paths.

The independently reviewed final runtime source has no remaining known P0–P2
finding within this scope. This statement does not substitute for the separate
menu/server review or native GPU acceptance.

## Production-linked verification

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/AreaTeamRuntimeChecks/Verify.ps1 -SptRoot D:\SPT41
```

The standalone project has no mod project references, SPT build imports or
deployment targets. It links the actual new team-runtime partial, initial patch,
spawn-side policy, shared map/area/exclusion catalog, `ArenaAreaRespawn` and
`ArenaSpawnSelector`. Verification extracts the **whole actual PickRespawn
method verbatim** from the main runtime into an ignored compiler input. This
tests its real branch/ranking behavior without maintaining a test-side copy.
Only the unchanged outer lifecycle and Unity navigation operations are controlled
host seams. The initial native-shaped interface call invokes the actual selected
production prefix through a minimal patch stand-in.

The installed Customs `base.json` supplies all marker identities, masks and
positions. Tests require the 28 allowed contour markers and 11 bot candidates,
exercise both local factions at every selected size 2..5, reserve/accept distinct
initial populations, and verify grouping then release to enemy-aware selection.
Additional checks cover exact initial world/collection/profile ownership,
ordinary/legacy pass-through, blocked/missing side pools and dispatch wrappers,
connection/separation drift, both-team occupancy, reservation deadlines and
tokens, late wrong/displaced/occupied arrivals, navigation failures, reset and
old-callback safety. Player tests run the actual adapter/preflight/Prepared chain
and verify reservation retention, no redraw, late occupancy/expiry rejection,
exact replacement release and unchanged in-place replacement.

Fifteen fault controls deliberately remove or reverse: exact initial collection/faction,
initial grouping, lease timeout/token, arrival distance/faction, enemy ranking,
reservation occupancy, player faction, native-zone readiness intersection and
prepared-player occupancy checks, frozen marker-position/identity checks and
frozen-ID disposal. Each must fail its named behavioral assertion;
an unrelated compile error is not a successful negative control. Production is
rebuilt again in `finally`, so no mutant remains the test executable.

Final verification covers 337 assertions and 15 fault controls, including marker
drift and a blocked cloned dispatch wrapper. The
`ARENA_TEAM_RUNTIME_CHECKS_PASSED` output is authoritative for the reviewed final
inputs. `artifacts` directories under this check project are ignored local
evidence, not release files.

## Limits and remaining GPU checks

These tests do not execute Unity NavMesh, Mono detouring, native body creation or
real AI movement. Review of Burst dispatch/arrival cleanup is source based; that
large service is not linked into this focused runtime harness. Root separately
tests the actual TDM mode/population/relations and performs full staged builds.

User testing should explicitly select the new Customs team-start preset, compare
2v2 and 5v5 and (where possible) both PMC factions, check initial west/east groups,
then kill/respawn several times and observe that subsequent placements can use
safer positions across the contour. Watch readiness's native dispatch counts,
arrival displacement/rejection diagnostics, unavailable-respawn messages and
successful protected-profile restoration. Existing full-map TDM, old fixed-2v2
areas, normal PMC/Scav and PFT are not redefined by the new preset.
