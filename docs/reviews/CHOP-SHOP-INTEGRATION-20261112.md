# Chop Shop Arena integration

Date: 2026-11-12, branch `feature/chop-shop` from `master` `39c5e5d3`. This adds
`arena_autoservice` (Arena's own name: **Chop Shop**) as an optional ArenaMapPort location the
way Iceberg was added on 31 October ([contract](ICEBERG-INTEGRATION-20261031.md)), after Bowl
([admission](BOWL-INTEGRATION-20261023.md), [team boxes](BOWL-TEAM-BOXES-20261023.md),
[Domination](DOMINATION-PORTED-20261026.md)). ArenaMode neither distributes nor changes the map.
The location id is MapPort's and fixed; "Chop Shop" is the player-facing name and the label on
Arena's location screen ("CHOP SHOP").

**MapPort requirement and detection.** Chop Shop exists only in an ArenaMapPort build that ships
`arena_autoservice`; today that is the unreleased `feature/autoservice` build `0.5.0+b7876d3`
installed on this machine. The published ArenaMapPort 0.5.0 has no Chop Shop and carries the same
version string, so ArenaMode detects the map by an enabled live location with the exact id (server,
`ArenaOptionalMapPolicy` on every client-settings request) and the MapPort plugin identity
(client), never by version. Without such a build the map is simply not offered. The whitelist row
stays at 0.5.0, and no ArenaMode release may announce the map before an ArenaMapPort release ships
it (README and FORGE-DESCRIPTION do not name it).

**Status in one line.** Admission, team boxes and Domination are implemented and verified offline.
The team-box height and the Domination point were measured on GPU-2 on 2026-11-12 in an ordinary
PMC raid (gates opened by MapPort, no ArenaMode involvement). **The native ArenaMode acceptance
run (gates held, formation, Domination, the Tactical refusal) is still pending.**

## Admission (commit A)

Commit `c09e886a`.

- Catalog: `AutoServiceId = "arena_autoservice"`, `IsAutoService`, the canonical lookup and
  `AnyMapAvailable`. Unknown spellings stay unsupported.
- Modes: Deathmatch, Gun Game, Team DM (generic markers until commit B, then the team boxes),
  Domination (commit C) and solo Zombies. **Tactical is refused, solo and PFT**, and **PFT Zombies
  is refused**, both in the shared resolver `ArenaAreaCatalog.TryResolve` next to the Equator and
  Iceberg rules, so menu, arm and server response agree:
  - Tactical: "Chop Shop has too few distant bot spawn points for Tactical's five-opponent squad."
  - PFT Zombies: "Chop Shop is too small for Zombies with a PitFireTeam squad; select Solo or
    another map."
  The picker hint is "Few distant spawns: no Tactical / squad Zombies" (one text solo and PFT,
  as Equator's, because every PFT mode is refused). See "Decisions".
- Participants: default **six** (five bots) on its own branch of `ArenaParticipantPolicy.DefaultTotal`.
  Ranges stay generic: DM/Gun Game 2-24, Team DM and Domination 4-20 through the boxes.
- Server defaults: `BotZone_Main`, `BotMax 8`, `MaxBotPerZone 8`, `BurstMinAliveBots 4`, as for
  every port. The default never installs a map.
- Presentation: gold **CHOP SHOP** label at MapPort's native anchor (MapPort places the marker at
  870/428; no `_reposition`), thumbnail and time choice kept (server time, no relabel, as Iceberg).
- Pickups (built only from the current raid's Bot/BotPmc rows), phase lighting and both PFT coverless
  adapters (spawn core and combat) accept the exact id. Both PFT modes are refused on this map, so
  the adapters never run on it today; every port is listed there (Equator too, also fully
  PFT-refused) and the two compiled faults of `tools/AirPitChecks/NegativeControls.ps1` cover the
  entry.

## Migration (commit A)

Optional-map revision **6**. New `IcebergDefaultClientAllowlist` (the former default);
`DefaultClientAllowlist` is that plus `arena_autoservice`.

| Saved list | Revision | Result |
|---|---|---|
| Exact Iceberg default (Bowl + Iceberg) | < 6 | gains Chop Shop |
| Exact Iceberg default | 6 or later | unchanged (Chop Shop removed by the user) |
| Exact Bowl default | < 5 | gains Iceberg and Chop Shop |
| Exact Bowl default | 5 | unchanged: Iceberg was removed by the user, so neither Iceberg nor Chop Shop is added |
| Exact AirPit default | < 4 | gains Bowl, Iceberg and Chop Shop |
| Exact AirPit default | 4 | unchanged (Bowl removed by the user) |
| Exact Bay5 / Equator / native defaults | their old cutoffs (< 3 / < 2 / 0) | gain everything up to Chop Shop |
| Custom lists, disabled entries, per-map tuning | any | user-owned, unchanged |
| Malformed or future revision marker | - | never re-enables a map |

The server's `ConfigStore` mirrors it (`icebergDefault = revision < 6 && ExactMapSet(IcebergDefault)`,
a removal clause for `AutoServiceId`) and records revision 6; removing Chop Shop after the upgrade
survives the next repair and a real `ConfigStore.Reload`. The client binds the same policy through
the real BepInEx `Settings.Init` (`tools/AreaConfigChecks`: the former `future-version` row moved
from revision 6 to 7; new rows `iceberg-release` (Iceberg default at 5 gains Chop Shop),
`chopshop-removed` (Iceberg default at 6 stays), `iceberg-removed` (Bowl default at 5 stays) and
`bowl-release` (Bowl default below 5 gains both)).

## Team boxes (commit B)

Commit `4feaa9f6`.

The pockets are the map's two authored TeamFight bounds (`D:/ArenaMapPort/tools/maps/autoservice.modes.json`,
`autoservice-native-v2`). USEC is the **east** pocket (`SPAWN_BOUND_Fighter`), BEAR the **west**
pocket (`SPAWN_BOUND_Fighter (1)`), matching MapPort's `player-usec`/`player-bear` rows (MapPort
itself marks that split "copied from Bowl, and a guess"; ArenaMode's Bowl convention is the same).
Every marker is named "Usec house (n)": the name says nothing about the faction, only the
containing bound does. The formation `arena_autoservice` / "Chop Shop" / "USEC east / BEAR west"
adds twenty private `arena-autoservice-tdm-v1-east-01..10` and `west-01..10` records (infiltration
`ArenaAutoService`, `BotZone_Main`, core 0) only to the protected raid clone, with the authored
floats and yaws in sidecar order.

**Interiors.** Both authored bounds have yaw 270.0 (scene quaternion 0.0003 degrees off), so the
boxes are world-aligned. Each horizontal face is the authored AABB inset by the 0.35 m capsule
radius `FloorAndClear` uses; the face toward the gates is additionally held at the gate leaf's
pocket-side face plus 0.35 m where that is stricter. Unlike Iceberg the authored bounds do not
reach past the closed gates (only the east x min coincides with the big gate leaf's yard face), so
the clip changes one face.

| Face | Authored bound (world) | Used | Reason |
|---|---|---|---|
| USEC (east) x min | -344.2145 | -343.587 | big gate leaf pocket-side face -343.937 + 0.35; the authored inset alone would be -343.865 |
| USEC x max | -332.6485 | -332.999 | authored, inset 0.35 |
| USEC z min / max | 473.471 / 512.1141 | 473.821 / 511.764 | authored, inset 0.35 |
| BEAR (west) x min | -413.9845 | -413.635 | authored, inset 0.35 |
| BEAR x max | -402.4185 | -402.769 | authored inset; the techlight leaf (pocket-side face -402.320 - 0.35 = -402.670) is looser, so the inset stays |
| BEAR z min / max | 473.1481 / 511.7911 | 473.498 / 511.441 | authored, inset 0.35 |
| Both, y | USEC -11.36 .. 5.75, BEAR -7.72 .. 2.12 | **-4.8 .. -1.0** | cut, see below |

The equator shutter leaf (x -344.650 .. -344.558) lies west of the east x min and is excluded by the
same face; no gate sits in a z end. Without the clip a held path or a pickup seed in the strip
between leaf and authored edge would count as inside the closed box.

**Height (measured on GPU-2 on 2026-11-12).** The authored east bound spans 17.1 m and reaches the
tribune decks; the west bound's top (2.117) would admit the walkable roof. The cut to -4.8 .. -1.0
for both pockets rests on a navmesh grid over each footprint: the west roof is walkable navmesh at
y 1.7 .. 1.9 over the whole footprint; the east pocket has separate navmesh islands at -1.8, -0.8,
-0.2 and 1.0 (each `path_partial` from inside the pocket with the gates open, i.e. not part of the
pocket); the highest connected pocket surface is a step at (-343.5, -2.5, 489.5). -1.0 is 1.5 m
above that step and 2.7 m below the west roof; -4.8 is 1.0 m below the lowest marker (-3.84). Arena's
own NavMesh has no polygon inside either footprint above -3.35 or below -3.83, so the band loses no
walkable surface of Arena's mesh. Smallest marker margins: 0.96 m to the floor of the box (Usec
house (19) at -3.84), 1.817 m horizontally (east x max, Usec house (19)) and 2.60 m (west z min,
(13)). The height is one constant pair in `ArenaTeamBoxCatalog.AutoService`.

**Donor, anchors, exits.** Donor and outside destination: the installed
`arena_autoservice-bot-core-03` row at (-367.44, -3.79, 492.11), exact floats, `BotZone_Main`,
core 0, delay 4, categories exactly Bot/BotPmc, 7.6 m from the capture point. All seven Bot rows are
reachable from both pockets on GPU-2 (west 25.6 .. 57.3 m, east 26.9 .. 67.5 m); core-03 has complete
paths of 44.4 m (west, 10 corners) and 39.9 m (east, 9 corners). Anchors: USEC = Usec house (4)
(MapPort `player-usec`), BEAR = Usec house (6) (MapPort `player-bear`). No booth exits: "Chop Shop
East Gate" is 4.9 m and "Chop Shop West Gate" about 9 m beyond the gate planes (8 s exfils in the yard).

**Gates.** No Chop Shop case: `MinimumGateOpeningSeconds` 0 and the ordinary ten-second watchdog.
The installed MapPort client exposes `GatesOpen`/`GatesOpened`; the longest gate drive is 8.0 s at
drive scale 1.0 (4.0 s at the default 0.5), which leaves the watchdog 2 s at scale 1.0 and 6 s at the
default. Recorded as a margin, not as a case. The completion contract still requires `GatesOpen` and
all twenty complete outside paths with floor/capsule checks before combat, and never re-closes
during respawns. The bot spawn shield and the 20-participant cap follow from
`ArenaTeamBoxCatalog.Applies`.

## Domination (commit C)

Commit `94ff82ba`.

Chop Shop has **one** capture point; `DominationMapLayout` has the row with the single point "X" at
(-369.29, -3.8, 499.49): X/Z and the height are all the root's own (the
`CAPTURE_POINT/BOWL_CAPTURE_POINT_LIGHT_03` light root of `autoservice.modes.json`; the name is a
Bowl leftover, but it is the only capture point the scene carries; the sidecar's `capture_zones` is
empty). **Measured on GPU-2 on 2026-11-12** (teleport dry run, `NavMesh.SamplePosition`, room to
stand): the host accepted that exact point (snap below 0.05 m) with complete paths of 42.2 m from
the west spawn and 37.1 m from Usec house (4). Unlike Iceberg nothing is substituted. The height
lives in one constant. The shared projection (`SamplePosition` within 0.75 m, |dy| <= 0.5, support
ray, capsule clearance) is not loosened; a refused point aborts the protected match. Chop Shop joins
`ArenaAreaCatalog`'s explicit Domination list (a future port is still never admitted by default);
`DominationMapLayout.ControlPointCount` gives 1 and the menu shows "Hold 1 point". Domination shares
the team boxes, the gate completion, the physical roster and the spawn-shield ownership of
commit B.

## Decisions

1. **Tactical is refused (solo and PFT).** Seven Bot/BotPmc rows only. A leader in the yard centre
   (-379, 495) has none of them beyond the unchanged 25 m floor; on the central half of the marker
   box every 1 m cell sees fewer than five (the same survey gives Iceberg 45/810 and Equator
   437/510); MapPort's mesh measurement says the south-east of the yard has no row at all. The team
   boxes do not help: Tactical uses the generic markers. Removing the guard needs a native proof
   or box-based Tactical work.
2. **PFT Zombies is refused**, extending the Equator compactness rule: the main walkable component is
   1,915.6 m2 (Equator 2,783, Iceberg 4,788, Bowl 4,987) with 1-2.4 m corridors at the gate mouths.
   Solo Zombies stays admitted.
3. **Default six participants** (five bots): seven bot rows make it the smallest ported map;
   Equator (2,783 m2, ten rows) defaults to 4, the roughly 4,800 m2 ports to 8. The default is a
   menu default, not a performance guarantee.
4. **Optional-map revision 6** with the exact-default rules above; an exact Bowl default at revision
   5 is a deliberate Iceberg removal and gains nothing.
5. **Team-box interiors** as derived above, argued at `ArenaTeamBoxCatalog.AutoService`.
6. **Gate timing** without a Chop Shop case (margin recorded above).
7. **Domination point at the root's own height**, no floor substitution, shared projection unchanged.
8. **Monitors and crowd are MapPort-side.** Chop Shop has Arena's own 30-fan crowd since 2026-11-11;
   ArenaMode binds reactions by `ReactionKinds`, not by map id. No production change here.
9. **Installed Chop Shop data is a harness dependency.** `ArenaPickupChecks` and
   `tools/ChopShopChecks` read the installed map and fail loudly without
   `db/arena_autoservice/base.json`, naming the requirement (an ArenaMapPort build that ships
   `arena_autoservice`: today the unreleased `feature/autoservice` build `0.5.0+b7876d3`). So
   `ChopShopChecks`, `ChopShopTeamBoxChecks`, `IcebergChecks`, `IcebergTeamBoxChecks`,
   `BowlChecks`, `BowlTeamBoxChecks` and any full harness run need that installed data; the
   published 0.5.0 is not enough. No silent skip.
10. **README and FORGE-DESCRIPTION carry no Chop Shop** (announcement rule above).

## PROVISIONAL values awaiting the native run

Measured on GPU-2 in an ordinary PMC raid; the native ArenaMode run with held gates is pending.
The following stand until then and are the first things to re-check if it disagrees:

- the box height -4.8 .. -1.0 (it was proposed at -4.8 .. -0.4 / 0.9 before the measurement);
- the donor and outside destination `arena_autoservice-bot-core-03` with complete paths from
  both pockets (measured with the gates open, not held);
- the Domination height -3.8 and the "X" projection under the protected match;
- the 20 markers' room to stand (all pass on the host; three east markers were refused only by the
  dry-run tool's straight-line wall heuristic, "crosses_geometry", because the sweep line meets the
  yard ramp's own top; the navmesh path itself is complete, 80.9 .. 86.3 m).

## Verification

Offline only. Every dotnet call used `tools/AirPitChecks/NoDeploy.targets` and an absent
`SPTUserMods` sentinel; the runner hashed the live install before and after every step.

The earlier commits were checked as they landed: commit A, xunit 3,823 tests (3,820 passed, 3
known environment skips) and its harness steps; commit B, xunit 3,889 (3,886 / 3) and eight
harness steps (SpawnGate, EquatorTeamSpawn, SpawnShieldScope, ArenaPickup, ParticipantMenu,
TeamDeathmatchRuntime, MapModeAdmission), 8 passed, 0 failed; commit C added Chop Shop to
CompactModeUxChecks, the NativeMenu and TDM Domination lists, DominationAdmissionChecks (12 mutants)
and DominationRuntimeChecks (a lifted-point mutant). Each new test was shown red against the
production line it claims to catch (find/owns clauses, both gate clips, both height cuts, donor row
and x, both anchors, the admission clauses and the migration cutoffs, then restored byte-identically
with the file time refreshed).

Step D, the harness entry points, run through the runner:

```text
pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 -SkipUnitTests -Harness ChopShopChecks,ChopShopTeamBoxChecks
```

Result: `ARENA_VERIFICATION_SUMMARY passed=2 failed=0` (494 s). The full suite ran once, inside
`ChopShopTeamBoxChecks`: 3,891 tests, 3,888 passed, 3 known environment skips (installed APBS, two
reward real-baseline cases). Numbers: installed data 4 Player and 7 Bot rows; 291 filtered server
tests; NativeMenu 1,018 assertions; AreaConfig 335 (real BepInEx); AreaMapPreview 4,512;
ParticipantMenu 6,742; PftPortedMap 340; PortedCover 258 (with `ARENA_PFT_INSTALLED_SOURCE` pointing
at the main checkout's decompile, the worktree has none); ArenaLighting 810; SpawnShieldScope and
ArenaExtraction (143) passed; ArenaPickup 521; eight compiled PFT adapter faults rejected (two new
for Chop Shop, `AutoServiceMissingPftSpawnAdapter` and `AutoServiceMissingPftCombatAdapter`);
donor `arena_autoservice-bot-core-03` exact with usec=1, bear=1 installed player rows inside their
boxes and 7 bot rows outside both; EquatorTeamSpawn 2,376 assertions with 12 controls; SpawnGate 387
with 26 controls; TeamDeathmatchRuntime and MapModeAdmission (801) passed; Release builds of client,
companion and server with 0 errors. The check is red without the data: with `-SptRoot` pointing
at a tree without `db/arena_autoservice/base.json`, both scripts stop at their first installed-data
line with "Installed Chop Shop data is missing ... Install an ArenaMapPort build that ships
arena_autoservice". The installed snapshot guard found the live install unchanged.

| Harness | What it holds |
|---|---|
| ChopShopChecks | installed data (4 Player, exactly the seven expected Bot rows, all `BotZone_Main`, infiltration `ArenaAutoService`); filtered server tests including `ChopShopMapTests` and `ChopShopTeamSpawnTests`; the NativeMenu, AreaConfig, AreaMapPreview, ParticipantMenu, PftPortedMap, PortedCover, ArenaLighting, SpawnShieldScope, ArenaExtraction and ArenaPickup programs; the compiled PFT adapter faults |
| ChopShopTeamBoxChecks | the installed donor `arena_autoservice-bot-core-03` pinned exactly against the installed row; installed USEC rows inside the east box and BEAR rows inside the west box (independent literals); no bot row inside a box; the installed snapshot guard; the full suite once; EquatorTeamSpawn and SpawnGate with their controls; ParticipantMenu, NativeMenu, TeamDeathmatchRuntime, SpawnShieldScope, MapModeAdmission, pickups; Release builds of client, companion and server |

There is no `tools/ChopShopChecks/scenarios/` folder yet: the GPU scenarios are written with the
native run. Known red, not caused by Chop Shop: `AnnouncementIntegrationChecks` (compile errors
since 2026-10-27), `tools/PftTacticalKitChecks/Verify-Integration.ps1` (missing `_reference` path),
and the xunit flake `ArenaDirectStartTests.AlreadyStartingServerIsReusedAndForeignImageIsRefused`
(passes on rerun).

## Not claimed

- **Not run natively:** the ArenaMode acceptance cycle (held gates, formation, Domination with the
  protected-match projection, the Tactical and PFT Zombies refusals in the menu), daytime, weather,
  performance, long sessions and balance.
- **Crowd reactions** on Chop Shop through ArenaMode (the 30-fan crowd is MapPort's; ArenaMode's
  binding on this map is unverified).
- **Redeploy and Killcam on this map** are untested.
- **Spawn stall in the yard centre:** whether an Arena spawn near the yard centre (the donor and the
  capture point are both there) stalls is not examined.
- **DM and Gun Game up to 24 participants on seven bot rows** are admitted by the generic range but
  not measured against the seven rows.
- **Monitors:** MapPort's four score/time holders are not checked on Chop Shop.
- **DynamicMaps overlay:** MapPort ships no DynamicMaps folder for Chop Shop, so Domination objectives
  have no overlay.
- **Fika fork** (thatHeathenJay's co-op fork) is not touched or tested.
- **MapPort release:** Chop Shop must not be announced in an ArenaMode release before an
  ArenaMapPort release ships it; the whitelist, DEPENDENCIES, FEATURES, README and FORGE text move
  together when it does.
