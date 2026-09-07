# Customs player-spawn contract checks

Run `Verify.ps1` from this directory (root agent serializes all executions).
This standalone project links the **actual** `ArenaAreaSpawnPolicy.cs`; it has
no game deployment target. The program checks genuine PMC versus Savage-only
Player/BotPmc markers, both exact factions, Pmc/All flags, malformed or missing
profile sides/masks, all native category/side combinations, stable marker
selection and preservation of source records. Four deliberate copy-only
mutations must exit unsuccessfully at their named assertions, followed by an
unchanged-production recheck. Generated copies remain under ignored `.work/`.

The doubles provide only the two `ISpawnPoint` properties read by the policy
and the actual native enum values. `Verify.ps1` checks these against local
decompiled contracts and checks that the runtime still calls this helper from
initial collection filtering, readiness and respawn selection. Final client
compilation uses the real installed types; this executable does not host EFT or
simulate Unity loading, NavMesh, collision, replacement bodies or native spawn
selection. Those remain integration/GPU checks, not claimed standalone evidence.

Inspected native sources under `D:/SPT41/Development/_decompiled`:

- `EFT/EPlayerSide.cs`: USEC1, BEAR2, Savage4.
- `EFT/EPlayerSideMask.cs`: Pmc3 and All7 are combined flags, not exact factions.
- `EFT.Game.Spawning/ESpawnCategoryMask.cs`: Player1 and BotPmc64 are independent.
- `EFT.Game.Spawning/ISpawnPoint.cs`: read-only Sides and Categories properties.
- `EFT.Game.Spawning/SpawnPointExtension.cs:228-252`: ordinary side matching
  checks mask intersection. Its nullable-side/debug bypass is deliberately not
  inherited by the bounded policy: an unresolved or unsupported PMC side must
  never silently authorize every marker.
- `EFT/BaseLocalGame.cs:358,428,519-526`: Profile is assigned during game creation,
  before Run builds the spawn collection. The first filter must not depend on
  MainPlayer, which is created later. Runtime uses the same game profile for
  readiness and subsequent respawns, so replacing the player body does not lose
  its faction. Exact world/session checks remain in ArenaAreaRuntime unchanged.

Production scope: only side/category selection and area-generic ready labels/
capacity were changed. Required AI count is `2 * MaxTeamSize - 1`; the alternative
player threshold remains two distinct positions separated by 40m. Existing
geometry, connected paths, blocked-marker checks, teleports, boundary recovery
and ordinary/PFT scope were not changed.

Execution status: prepared and reviewed locally; **not executed by this leaf**.
Root owns the serialized baseline, negative controls and final real-client build.
