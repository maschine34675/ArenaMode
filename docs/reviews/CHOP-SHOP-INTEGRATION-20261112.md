# Chop Shop Arena integration

> **2026-11-13: superseded in part by [SPAWN-ROWS-FOLLOWUP-20261113.md](SPAWN-ROWS-FOLLOWUP-20261113.md).**
> ArenaMapPort's rows of 2026-11-12 (16 Bot rows instead of 7) changed decision 1 (solo Tactical is admitted,
> PitFireTeam Tactical stays refused, with a new reason text) and decision 3 (default eight instead of six),
> and closed both known limits (Domination 6 of 6 pickups, no dead patch in the yard centre). The exits stand
> inside the pockets, so "No booth exits" no longer holds. The text below is the state of 2026-11-12.

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

**Status in one line.** Admission, team boxes and Domination are implemented, verified offline and
accepted natively on GPU-2 on 2026-11-12 ([native report](../reports/2026-11/GPU-CHOP-SHOP-20261112.md)):
the menu, Deathmatch, Team DM 4v4 and 10v10 with held gates, Domination, solo Zombies and an ordinary PMC
control ran with 0 errors. The cycle found two things that changed the build (the gate watchdog is 15 s;
a failed infected dispatch backs off for 2 s) and two limits that stay (Domination places 5 of 6
pickups; a dead patch in the yard centre). What was not run is listed under "Not claimed".

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
  Seen natively under Zombies: the label spans about x 975..1108 at y 457 on 1920x1080, clear of
  CUSTOMS, EQUATOR, INTERCHANGE and the Arena panel (which ends at x 416); the picker hint fits in one
  line without an ellipsis. Under Tactical the tile is not offered (`map tile "CHOP SHOP" is not offered`).
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
| Malformed or future revision marker | - | Server: a malformed marker is read as the highest revision (`int.MaxValue`), so nothing is added and a removed map stays removed. Client: the marker is a typed BepInEx entry; a future revision (above 6) skips the migration, and a value that does not parse as an integer falls back to the entry's default 0, which is not a "future" marker (see "Review") |

The server's `ConfigStore` mirrors it (`icebergDefault = revision < 6 && ExactMapSet(IcebergDefault)`,
a removal clause for `AutoServiceId`) and records revision 6; removing Chop Shop after the upgrade
survives the next repair and a real `ConfigStore.Reload`. The client binds the same policy through
the real BepInEx `Settings.Init` (`tools/AreaConfigChecks`: the new rows are `iceberg-release`
(Iceberg default at 5 gains Chop Shop) and `chopshop-removed` (Iceberg default at 6 stays);
`bowl-release` and `iceberg-removed` existed before this branch; the former `future-version` row
moved from revision 6 to 7).

**Seen natively (build 1, first start on GPU-2):** `General/Maps` gained `arena_autoservice` and
`OptionalMapsRevision` went 5 to 6; the server `config.json` got `optionalMapsRevision` 6 and the
`arena_autoservice` row (enabled, `botMax` 8, `maxBotPerZone` 8, `BotZone_Main`, `burstMinAliveBots` 4).
Both lists were the exact Iceberg default before.

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
above that step and 2.7 m below the west roof; -4.8 is 1.0 m below the lowest marker (-3.84).
Not every separate island lies outside the band: the one at y -1.8 to -2.0 on the east pocket's north
edge and a shelf at the west pocket's south end (y -1.2) lie inside it. They are unreachable (see the
second survey below), so no held path and no formation point can use them. Arena's
own NavMesh has no polygon inside either footprint above -3.35 or below -3.83, so the band loses no
walkable surface of Arena's mesh. Smallest marker margins: 0.96 m to the floor of the box (Usec
house (19) at -3.84), 1.817 m horizontally (east x max, Usec house (19)) and 2.60 m (west z min,
(13)). The height is one constant pair in `ArenaTeamBoxCatalog.AutoService`.

**Second survey (GPU-2, 2026-11-12, build 1, ordinary PMC raid, gates open).** A teleport dry run on a
2.5 m grid at y -1.6 over both footprints, looking at every surface between the floor and y 0.4
([native report](../reports/2026-11/GPU-CHOP-SHOP-20261112.md)). Connected to the pocket floor
(complete paths from Usec house (4), 9.0 to 15.1 m): curbs at -3.3 along the east wall (x -333.6 ..
-334.0, z 482.9 .. 501.1) and at the north-east corner (-333.6, -3.1, 511.0), and the step at the west
wall ((-343.6, -3.0, 488.1), (-343.5, -2.5, 489.5)). Separate islands (`path_partial` from Usec house
(4) and from the donor in the yard): a strip along the north edge ((-340.5, -1.7, 511.4), (-341.7, -2.0,
511.5), (-337.5, -2.8, 512.0), (-338.5, -0.8, 511.7)); a ledge along the west wall at -0.3 (x -343,
z 494.5 .. 501); (-339 .. -343, -0.4, 473.3 .. 473.7) south of the box. West footprint: the floor, a curb
at (-403.8, -3.3, 482.2), an unstandable shelf at the south end (x -408 .. -411, z 474 .. 475, "no room
to stand") and the roof at 1.7 .. 1.9. **Inside the band and unreachable:** the north-edge strip at
y -1.7 .. -2.0 (x -340.5 .. -341.7, z 511.2 .. 511.5) and the west shelf. **Everything at -0.8 and above is
outside the band.** The band is not narrowed to cut those two out: 0.3 m above the step would be the only
room for it.

**Donor, anchors, exits.** Donor and outside destination: the installed
`arena_autoservice-bot-core-03` row at (-367.44, -3.79, 492.11), exact floats, `BotZone_Main`,
core 0, delay 4, categories exactly Bot/BotPmc, 7.6 m from the capture point. All seven Bot rows are
reachable from both pockets on GPU-2 (west 25.6 .. 57.3 m, east 26.9 .. 67.5 m); core-03 has complete
paths of 44.4 m (west, 10 corners) and 39.9 m (east, 9 corners). In the native Team DM runs the
routes from the player to the capture point (48.4 m) and to the donor (49.3 m) were complete. Anchors: USEC = Usec house (4)
(MapPort `player-usec`), BEAR = Usec house (6) (MapPort `player-bear`). No booth exits: "Chop Shop
East Gate" is 4.9 m and "Chop Shop West Gate" about 9 m beyond the gate planes (8 s exfils in the yard).

**Gates (15 s watchdog, commit `0f14cdb9`).** No legacy minimum (`MinimumGateOpeningSeconds` 0): the
installed MapPort client exposes `GatesOpen`/`GatesOpened`. The watchdog is **15 s**
(`GateOpeningTimeoutSeconds`: 20 s for Bowl, 15 s for Chop Shop, 10 s for the rest). The reason is
the polling, not the drive: the match flow polls the opening once a second, and the native run measured
5.00 s from release to the revalidation at MapPort's default drive scale 0.5 (MapPort `drove 23 part(s)
to the open pose in 4.0 s`) and 9.00 s at drive scale 1.0 (8.0 s drive, 958 frames, mean 8.4 ms, max
27.6 ms). The ordinary ten seconds would leave two polls after an eight-second drive, and a missed poll ends the
protected match; 15 s leave seven. The earlier wording of this contract ("the watchdog keeps 2 s of margin
at scale 1.0, recorded as a margin, not as a case") was a number about the drive and is replaced. The
watchdog still only catches a formation that never reconnects. The completion contract still requires
`GatesOpen` and all twenty complete outside paths with floor/capsule checks before combat, and never
re-closes during respawns. The bot spawn shield and the 20-participant cap follow from
`ArenaTeamBoxCatalog.Applies`. `SpawnGateChecks` holds the Chop Shop opening (no abort at ten seconds,
fail closed at fifteen).

**Seen natively (build 1 and 2).** Held gates and formation worked as designed: `team-box capacity: USEC
10/10 safe connected places, 4 required` (10 required in the 10v10 run), `formation ready`, bot spawns and
arrivals with "actual" equal to the point, `gates open: all twenty formation paths revalidated; 5.00s since
release (MapPort GatesOpen)`, 4 USEC bots in the east pocket and 3 BEAR bots in the west pocket at that
moment, 7/7 in the field 20 s later. The test-hook death respawned the local player at
`arena-autoservice-tdm-v1-west-05` (Usec house (10)) and read it back there.

## Domination (commit C)

Commit `94ff82ba`.

Chop Shop has **one** capture point; `DominationMapLayout` has the row with the single point "X" at
(-369.29, -3.8, 499.49): X/Z and the height are all the root's own (the
`CAPTURE_POINT/BOWL_CAPTURE_POINT_LIGHT_03` light root of `autoservice.modes.json`; the name is a
Bowl leftover, but it is the only capture point the scene carries; the sidecar's `capture_zones` is
empty). **Measured on GPU-2 on 2026-11-12** (teleport dry run, `NavMesh.SamplePosition`, room to
stand): the host accepted that exact point (snap below 0.05 m) with complete paths of 42.2 m from
the west spawn and 37.1 m from Usec house (4). **Verified natively under the protected match** (build
1 and 2): `Domination points verified: X=(-369.290, -3.805, 499.490)` (the light root's own point,
projected 5 mm), gates open 5.00 s, `Domination objective routes from both opened team boxes verified`,
`Domination ownership: X=neutral` then `X=USEC` 54 s after the start, bots on the "Arena Combat
Domination" layer, HUD "4v4 BEAR 0 : 48 USEC | First to 300 points | 08:58 left" and "X USEC", MapPort's
timer monitor on the wall showing the same 08:58. Unlike Iceberg nothing is substituted. The height
lives in one constant. The shared projection (`SamplePosition` within 0.75 m, |dy| <= 0.5, support
ray, capsule clearance) is not loosened; a refused point aborts the protected match. Chop Shop joins
`ArenaAreaCatalog`'s explicit Domination list (a future port is still never admitted by default);
`DominationMapLayout.ControlPointCount` gives 1 and the menu shows "Hold 1 point". Domination shares
the team boxes, the gate completion, the physical roster and the spawn-shield ownership of
commit B.

**Known limit: 5 of 6 pickups.** In every native Domination attempt the pickup placement logged
`placed=5/6; ... refusals=[another pickup is within 3m=..., control point or its 2m clearance=2]`. The
pickups are built only from the seven Bot/BotPmc rows; `ArenaObjectivePickupSpacing` refuses anything
within 6 m of the capture point at |dy| <= 2, and core-01 (5.0 m) and core-02 (2.4 m) lie inside that
clearance. Five rows remain for six kinds and the Health pickup is the one left out. Plain Team DM is not
affected. Adding candidates inside ArenaMode does not help (the map has seven rows and the
Factory-only objective extension does not apply); more Bot/BotPmc rows in ArenaMapPort's data would,
and ArenaMode does not edit that data.

## Zombies: failed dispatch backoff (commit 0f14cdb9)

A general Zombies change found on Chop Shop, not a Chop Shop case. With the player in the yard centre
(-379, -3.8, 495) no Bot row lies beyond the 25 m floor. Native build 1: `points 0 preferred + 0 distant,
selected 0` and `Arena burst spawn: no usable spawn point for <role> - request dropped` (Warning), **133
times in 30 s**, each after a profile request, because the wave retried a failed slot on its pacing
cadence. The rule now: `ZombieWavePolicy.ReportFailedDispatch(ref state, now)` undoes the optimistic slot
commit as before and also holds the wave's next attempt until `now + FailedDispatchBackoffSeconds`
(2 s, never earlier than the pacing already set). A caller without a time (`float.NaN`, the pure slot
accounting tests) keeps the old pacing. Both call sites in `ZombieMode.Tick` (the asynchronous
`ConsumeFailedDispatches` loop and the synchronous refusal) pass `now`. The slot is still retried,
nothing is written off, and the 25 m floor and the rows are unchanged. Native build 2: **18 dropped
requests in 36 s** (one every 2.1 s); the refill resumed 1.1 s after the player was back at the pocket
(`infectedAssault arrived`) and reached 32/32 again; 0 errors.

Tests: `ZombieWavePolicyTests` (the backoff, and the unchanged pacing without a time), each shown red against
the removed line; `ZombieWavePendingChecks` drives the real `ZombieMode` with a clock of its own and
follows the backoff since `34e21317` (the asynchronous failure acknowledged at 1 is retried at 3; the
synchronous rejections at 1, 3, 5, 7 and 9 recover at 11; the two older mutants keep their labels, and a
thirteenth, `failed-dispatch-no-backoff`, removes the hold and must fail "a failed dispatch is not retried
on the pacing cadence").

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
3. **Default six participants** (five bots), argued as in the comment at
   `ArenaParticipantPolicy.DefaultTotal`: Chop Shop has seven Bot/BotPmc rows, the fewest of any port.
   The 8 of the other roughly 4,800 m2 ports would ask for seven bots from seven rows and leave the burst
   selector no row to spare, so the preset stays two below it. It is a menu preset, not a ranking by
   area: Chop Shop (1,915.6 m2) is the smallest port, smaller than Equator (2,783 m2), whose 4 follows
   from its own distant-row count. Deathmatch ran with six natively. The default is a menu default,
   not a performance guarantee.
4. **Optional-map revision 6** with the exact-default rules above; an exact Bowl default at revision
   5 is a deliberate Iceberg removal and gains nothing.
5. **Team-box interiors** as derived above, argued at `ArenaTeamBoxCatalog.AutoService`.
6. **Gate timing:** no legacy minimum, a 15 s watchdog (polling, not drive time; see "Gates").
7. **Domination point at the root's own height**, no floor substitution, shared projection unchanged.
8. **Monitors and crowd are MapPort-side.** Chop Shop has Arena's own 30-fan crowd since 2026-11-11;
   ArenaMode binds reactions by `ReactionKinds`, not by map id. No production change here. Natively
   (Deathmatch, build 1 and 2) the crowd attached and reactions were claimed and sent (kinds `0x3E`;
   reaction Start at round start), and MapPort's timer monitor showed the match clock; nobody listened
   to the crowd or read the monitors, so what is claimed is the log lines and a screenshot.
9. **Installed Chop Shop data is a harness dependency.** `ArenaPickupChecks` and
   `tools/ChopShopChecks` read the installed map and fail loudly without
   `db/arena_autoservice/base.json`, naming the requirement (an ArenaMapPort build that ships
   `arena_autoservice`: today the unreleased `feature/autoservice` build `0.5.0+b7876d3`). So
   `ChopShopChecks`, `ChopShopTeamBoxChecks`, `IcebergChecks`, `IcebergTeamBoxChecks`,
   `BowlChecks`, `BowlTeamBoxChecks` and any full harness run need that installed data; the
   published 0.5.0 is not enough. No silent skip.
10. **README and FORGE-DESCRIPTION carry no Chop Shop** (announcement rule above).

## Values the native run settled

The values that were provisional before the run, as the native cycle on GPU-2 showed them
([native report](../reports/2026-11/GPU-CHOP-SHOP-20261112.md)):

- **Box height -4.8 .. -1.0:** held. Formation, capacity (10/10 with 4 or 10 required), arrivals,
  revalidation and respawn worked with it; a second survey found nothing else connected inside the band
  (see "Height").
- **Donor and outside destination `arena_autoservice-bot-core-03`:** held. Complete paths from both
  pockets; the routes to the capture point and the donor were complete in the Team DM runs.
- **Domination height -3.8 and the "X" projection under the protected match:** held,
  `X=(-369.290, -3.805, 499.490)`.
- **The 20 markers' room to stand:** all 20 restored (`restored 20 box markers`), and the formation
  passed with 4 and 10 per side. The three east markers the dry-run tool had refused by its wall
  heuristic were not a problem under the real formation.
- **Gate timing:** not held as written: the watchdog is now 15 s (see "Gates").
- **The picker hint width:** settled; one line without an ellipsis.
- **The spawn stall in the yard centre:** seen, and answered for Zombies by the backoff; the dead patch
  itself stays (see "Not claimed").

## Verification

Offline unless stated; the native cycle is in the
[native report](../reports/2026-11/GPU-CHOP-SHOP-20261112.md). Every dotnet call used `tools/AirPitChecks/NoDeploy.targets` and an absent
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

The five GPU scenarios of the native run are kept in `tools/ChopShopChecks/scenarios/`
(`chopshop-dm`, `-tdm`, `-dom`, `-zombie`, `-pmc`; see that README). Known red, not caused by Chop Shop: `AnnouncementIntegrationChecks` (compile errors
since 2026-10-27), `tools/PftTacticalKitChecks/Verify-Integration.ps1` (missing `_reference` path),
and the xunit flake `ArenaDirectStartTests.AlreadyStartingServerIsReusedAndForeignImageIsRefused`
(passes on rerun).


### Fix round (commits `0f14cdb9` and `34e21317`)

After the native cycle, with the same no-deploy rules:

- xunit filtered runs, each new test shown red first against the broken production line: the failed-dispatch
  backoff (`ZombieWavePolicyTests`, with the unchanged pacing without a time) and the 15 s watchdog plus
  the island rows (`ChopShopTeamSpawnTests`); the file was restored byte-identically and its time stamp
  refreshed.
- `SpawnGateChecks` with the Chop Shop opening (no abort at ten seconds, fail closed at fifteen).
- `ZombieSoloChecks`: 13 mutants rejected.
- `ZombieWavePendingChecks`: 13 faults rejected, after `34e21317` taught the harness the backoff (it had
  expected the retry one evaluation later and was red at `0f14cdb9`) and the fix round's own review asked
  for a mutant that removes the backoff (`failed-dispatch-no-backoff`).
- `ZombieTeamChecks`: 18 mutants rejected.
- `ZombieKitWarmupChecks`: 102 assertions, 11 faults rejected.
- `DominationRuntimeChecks`: 680 assertions, 14 source mutants rejected (the README's 597 was stale).

The first run of these harnesses through the runner reported FAIL for all of them on the runner's
installed-file guard (`Offline verification changed an Arena installation file: ...BigBrainCompat.dll`):
the native deployment of build 2 was changing the live install at the same time, which is exactly what
the guard is for. The runs were repeated without a concurrent deploy
(`ARENA_VERIFICATION_SUMMARY passed=5 failed=0 seconds=344`).

A second, narrow adversarial review of the two fix commits (one reviewer, read-only) found no defect: the
backoff uses the clock `Evaluate` uses, a synchronous `false` from `SpawnInfected` is not a routine "busy"
answer (so ordinary filling is not slowed: natively 32 arrivals in 5.5 s after, 6.2 s before), a restored
slot keeps the wave unsaturated and the stuck-wave watchdog unarmed, a failure cannot be reported after a
wave change, and nothing else reads `GateOpeningTimeoutSeconds` or assumes ten seconds (MapPort's hold
limit is 300 s). Its two suggestions were mutants; the backoff one was added, the watchdog one was not (the
mutant table rewrites the runtime, not the catalog; the xunit value and the harness baseline hold it).

### Final run (HEAD of the branch, 2026-11-12)

```text
pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 -SkipUnitTests -Harness ChopShopChecks,ChopShopTeamBoxChecks,IcebergChecks,IcebergTeamBoxChecks,BowlTeamBoxChecks,SpawnShieldScopeChecks,TeamDeathmatchRuntimeChecks,CompactModeUxChecks,DominationAdmissionChecks,DominationRuntimeChecks,TdmMatchMenuChecks,ZombieSoloChecks,ZombieWavePendingChecks,ZombieTeamChecks,ZombieKitWarmupChecks,SpawnGateChecks,MapModeAdmissionChecks
ARENA_VERIFICATION_SUMMARY passed=17 failed=0 seconds=1783
```

The runner hashed the live install before and after every step and found it unchanged. The full suite ran
inside `ChopShopTeamBoxChecks` (and again inside the Iceberg and Bowl box harnesses): 3,893 tests, 3,890
passed, 3 known environment skips.

| Harness | Result |
|---|---|
| ChopShopChecks | installed data 4 Player / 7 Bot rows with the seven expected ids; its filtered server tests and programs (ArenaExtraction 143, ArenaPickup 521 assertions); 8 compiled PFT adapter faults rejected |
| ChopShopTeamBoxChecks | donor `arena_autoservice-bot-core-03` exact; full suite as above; EquatorTeamSpawn 2,376; SpawnGate 393; MapModeAdmission 801; pickups 521; Release builds: 0 errors |
| IcebergChecks / IcebergTeamBoxChecks / BowlTeamBoxChecks | passed (the neighbours whose lists and oracles this branch extended) |
| SpawnGateChecks | 393 assertions (the Chop Shop opening adds six), 26 negative controls |
| SpawnShieldScopeChecks | passed, 5 compiled fault controls |
| TeamDeathmatchRuntimeChecks | 577 checks |
| CompactModeUxChecks | 332 assertions, its mutants |
| DominationAdmissionChecks | 12 compiled mutants, 7 wiring controls |
| DominationRuntimeChecks | 680 assertions, 14 source faults |
| TdmMatchMenuChecks | 2,741 assertions |
| ZombieSoloChecks / ZombieTeamChecks | 13 / 18 mutants rejected |
| ZombieWavePendingChecks | 12 faults in this run; 13 after the added mutant (run alone afterwards: passed) |
| ZombieKitWarmupChecks | 102 assertions, 11 compiled faults |
| MapModeAdmissionChecks | 801 assertions, its compiled faults |

## Review

An adversarial review ran over the branch after the offline verification: five lenses (migration,
team boxes, modes, isolation, tests and documents), each finding re-checked by a second pass. **No
blocker.**

- **Confirmed:** Domination on Chop Shop places at most 5 of 6 pickups (now a known limit, seen
  natively, see "Domination"); the contract listed `AreaConfigChecks` rows as new that already existed on
  master (corrected above).
- **Refuted, with reasons:** (1) an unparsable `OptionalMapsRevision` is read as 0 by the client and can
  re-add a removed map, while the server maps it to `int.MaxValue`: the scenario needs a hand-corrupted,
  plugin-written and menu-hidden marker together with an exact old default list, the client logic is the
  one of the Iceberg admission, and the effect is benign (a client list entry the server lacks only hides
  the map); the contract row was reworded to say what each side does instead of "never re-enables".
  (2) the client matches the old default list exactly while the server matches by map set, so a reordered
  or re-spaced list is not migrated: deliberate ("any edited allowlist remains user-owned"), identical to
  the earlier admissions. (3) some new tests cannot fail for the defects credited to them: the same
  patterns exist for Iceberg, and the production refusals are asserted elsewhere
  (`ChopShopMapTests`).
- **Settled natively:** the picker hint width (one line, no ellipsis).
- **Document fixes applied:** AREA-RULES said the east islands are all above -1.0 (the strip at
  y -1.7 .. -2.0 and the west shelf are inside the band and unreachable); DEPENDENCIES lacked Chop Shop in
  the PitFireTeam exclusions and said "five ported maps" for Domination; two production comments called
  Chop Shop the smallest port "after" Equator and placed the default of six "between" Equator and the
  8-group (it is smaller than Equator and the default rests on seven Bot rows); the
  `DominationRuntimeChecks` README kept a stale 597-assertion baseline (680 now).

## Not claimed

Not run in the native cycle (see the [report](../reports/2026-11/GPU-CHOP-SHOP-20261112.md)):

- PitFireTeam on this map (both PFT modes are refused); Gun Game (it takes the Deathmatch path); a match
  played to its result and its reward; Deathmatch or Gun Game with more than 6 participants (up to 23 bots
  from 7 rows); crowd reactions heard and monitors read by a person (the log shows reactions claimed and
  sent, the screenshot shows the timer monitor); daytime, weather, performance over a long session; the
  DynamicMaps overlay (MapPort ships no folder for this map, so Domination objectives have no overlay).
- The killcam (switched off in the test configuration) and the Fika fork (thatHeathenJay's co-op fork) were
  not exercised. Redeploy's respawn was: the Team DM deaths respawned into the pocket through it.

Known limits seen natively and not fixed:

- **Domination places 5 of 6 pickups** (the Health pickup is left out).
- **A dead patch in the yard centre** (about 4 % of the yard has no Bot row beyond the 25 m floor):
  Zombies back off for 2 s per failed dispatch; Deathmatch and Gun Game drip more slowly there (one
  attempt per 3 s).
- Both would go away with more Bot/BotPmc rows in ArenaMapPort's data for this map, which ArenaMode does
  not edit.
- **MapPort release:** Chop Shop must not be announced in an ArenaMode release before an
  ArenaMapPort release ships it; the whitelist, DEPENDENCIES, FEATURES, README and FORGE text move
  together when it does.
