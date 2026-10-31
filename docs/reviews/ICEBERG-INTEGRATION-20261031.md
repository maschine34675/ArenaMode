# Iceberg Arena integration

Date: 2026-10-31, branch `feature/iceberg` from `master` `54c464a1`. This adds
`arena_iceberg` as an optional ArenaMapPort location the way Bowl was added in
October ([admission](BOWL-INTEGRATION-20261023.md), [team boxes](BOWL-TEAM-BOXES-20261023.md),
[Domination](DOMINATION-PORTED-20261026.md)). ArenaMode neither distributes nor
changes the map.

**MapPort requirement.** Iceberg exists only in an ArenaMapPort build that ships
`arena_iceberg`; today that is the unreleased `feature/iceberg` build
`0.4.0+32fda33` installed on this machine. The published ArenaMapPort 0.4.0 has no
Iceberg and carries the same version string, so ArenaMode detects the map by the
presence of an enabled live location with the exact ID (server) and the MapPort
plugin identity (client), never by version. Without such a build the map is simply
not offered. The whitelist row stays at 0.4.0.

## Admission (commit A)

Commit `21abac26`.

- Catalog: `IcebergId = "arena_iceberg"`, `IsIceberg`, the canonical lookup and
  `AnyMapAvailable`. Unknown spellings (`arena_iceberg_extra`, `iceberg`) stay
  unsupported.
- Modes: Deathmatch, Gun Game, Team DM, solo Zombies and PFT Zombies. Domination
  follows in commit C. **Tactical is refused, solo and PFT**, in the shared resolver
  `ArenaAreaCatalog.TryResolve` next to the Equator rule, so menu, arm and server
  response agree: "Iceberg has too few distant bot spawn points for Tactical's
  five-opponent squad." The map picker hint reads "Too few distant spawns: no
  Tactical". See "Decisions" below.
- Participants: default eight in DM/Gun Game/Team DM (installed `BotMax 8`); DM/Gun
  Game 2–24; Team DM 4–24 until commit B, then 4–20 through the team boxes.
- Server defaults: `BotZone_Main`, `BotMax 8`, `MaxBotPerZone 8`,
  `BurstMinAliveBots 4`, as for the other ports. The default never installs a map.
- Discovery: `ArenaOptionalMapPolicy` queries the live database for the exact ID on
  every client-settings request; a missing, disabled or wrong-ID location is dropped
  only from the detached client view.
- Presentation: gold **ICEBERG** label, MapPort's native position (which sits under the
  Arena mode panel in the Arena flow, see "Not claimed"), thumbnail and
  time choice are kept (AirPit/Bowl behaviour, not the Bay5/Equator override).
- Pickups (built only from the current raid's Bot/BotPmc rows), phase lighting and
  both PFT coverless adapters (spawn core and combat) accept the exact ID. Iceberg
  has eleven real AI core points and no baked cover. PFT Tactical never reaches the
  adapters on Iceberg because the resolver refuses it; PFT Zombies does.

## Migration (commit A)

Optional-map revision 5. `BowlDefaultClientAllowlist` names the former default;
`DefaultClientAllowlist` is that plus Iceberg.

| Saved list | Revision | Result |
|---|---|---|
| Exact Bowl default | < 5 | gains Iceberg |
| Exact AirPit default | < 4 | gains Bowl and Iceberg |
| Exact AirPit default | 4 | unchanged: Bowl was removed by the user, so neither Bowl nor Iceberg is added |
| Exact Bay5 / Equator / native defaults | their old cutoffs (< 3 / < 2 / 0) | gain everything up to Iceberg |
| Exact Bowl default | 5 or later | unchanged (Iceberg removed by the user) |
| Custom lists, disabled entries, per-map tuning | any | user-owned, unchanged |
| Malformed or future revision marker | — | never re-enables a map |

The server's `ConfigStore` applies the same cutoffs to the saved map set
(`bowlDefault = revision < 5 && ExactMapSet(BowlDefault)`) and records revision 5;
removing Iceberg after the upgrade survives the next repair and a real
`ConfigStore.Reload`. The client binds the same policy through the real BepInEx
`Settings.Init` (checked by `tools/AreaConfigChecks`: `bowl-release` gains,
`iceberg-removed` at revision 5 and `future-version` at revision 6 do not).

## Team boxes (commit B)

Commit `4e727297`.

Original Arena has no TeamFight mode on Iceberg; the pockets come from CheckPoint
(`D:/ArenaMapPort/tools/maps/iceberg.modes.json`, `iceberg-native-v2`). USEC is
west (`Usec house (5..14)` in `SPAWN_BOUND_DEFENCE`), BEAR is east (`Bear house
(5..14)` in `SPAWN_BOUND_FIGHTER`), matching MapPort's `player-usec`/`player-bear`
rows. The formation `arena_iceberg` / "USEC west / BEAR east" adds twenty private
`arena-iceberg-tdm-v1-west-01..10` and `east-01..10` records (infiltration
`ArenaIceberg`, `BotZone_Main`, core 0, 0.8 m colliders) only to the protected raid
clone. Positions and yaws are the authored ones; Bear keeps its three heights
(floor 0.7345, terrace 1.8862 for (7)/(9)/(11), 0.8753 for (14)).

**Interiors.** The authored SPAWN_BOUNDs cross the closed gates, so they are not
used as-is. Each box is world-aligned (the authored yaws 179.9999 and 270.2255 are
within 0.23 degrees of the axes) and clipped at its gate plane minus the 0.35 m
capsule radius that `FloorAndClear` uses:

| Face | Authored bound (world) | Used | Reason |
|---|---|---|---|
| USEC x min | -48.6605 | -48.31 | authored, inset 0.35 m |
| USEC x max | -32.9111 | -36.392 | GATES_B leaf plane x -36.042 minus 0.35 m (authored bound reaches 3.1 m past the closed gate) |
| USEC z min | -97.2762 | -96.926 | authored, inset 0.35 m |
| USEC z max | -79.9335 | -76.014 | the pocket continues north to the rotating CheckPoint gate (z -75.664) minus 0.35 m; Usec house (6) sits on the authored edge |
| USEC y | 2.3277 .. 10.2709 | 2.328 .. 10.271 | full authored height (Bowl rule) |
| BEAR x min | 80.9597 | 82.732 | GATES_A leaf plane x 82.382 plus 0.35 m (authored bound reaches 1.4 m past the closed gate) |
| BEAR x max | 114.7944 | 114.444 | authored, inset 0.35 m |
| BEAR z min | -98.8152 | -98.465 | authored, inset 0.35 m |
| BEAR z max | -77.8728 | -78.223 | authored, inset 0.35 m |
| BEAR y | -8.262 .. 8.052 | -0.4 .. 5.0 | the authored 16.3 m would admit other floors; both pocket floors plus a standing body |

All twenty markers lie inside their own box (smallest margin 0.634 m, Usec house
(13) to the gate face) and outside the other. Anchors: USEC index 2 (`Usec house
(7)`), BEAR index 19 (`Bear house (14)`, MapPort's `player-all`). Donor and outside
destination: the installed `arena_iceberg-bot-core-01` at (-10.88, 3.25, -80.66),
exact floats, `BotZone_Main`, core 0, delay 4, categories exactly Bot/BotPmc; it
had a complete path from the Bear spawn in MapPort's GPU-2 raid of 2026-10-30. No
booth exits: East Gate is 10.4 m beyond GATES_A, West Gate 5.6 m beyond GATES_B.

**Gates.** No Iceberg special case: `MinimumGateOpeningSeconds` 0 and the ordinary
ten-second watchdog. Every MapPort build that ships Iceberg exposes `GatesOpen`, so
the legacy minimum is never used; the longest drive (GATES_A leaf B) is 5 s at drive
scale 1.0 (2.5 s at the default 0.5). The existing completion contract still
requires `GatesOpen` and all twenty complete outside paths with floor/capsule
checks before combat, and never re-closes during respawns. The bot spawn shield
and the 20-participant cap follow from `ArenaTeamBoxCatalog.Applies`.

## Domination (commit C)

Commit `9f3cb3b3`.

`DominationMapLayout` row Iceberg: A (16.77, 3.9, -91.39), B (10.614, 2.69,
-77.727), C (21.91, 2.85, -47.3), from the `CAPTURE_POINT/CAPTURE_POINT_LIGHT_02_A/B/C`
roots of the sidecar (scene `Arena_Sanatorium_CheckPoint_DesignStuff`, level376), the
same per-light source whose roots equal the CapturePointEntity anchors on the released
ports. The sidecar's `capture_point` centroid is null and is not used. Iceberg is the
fifth port in `ArenaAreaCatalog`'s explicit Domination list and the menu shows
"Hold 3 points".

These are trigger centres, not floors: A lies 0.5 m below the pool's basin floor
(4.40) and 1.1 m below the deck (about 5.0); B lies 0.4 m below its floor. The
shared projection is not loosened and no relocation is added: ported points get
`SamplePosition` within 0.75 m with |dy| <= 0.5, a support ray and capsule
clearance, and a refused point aborts the protected match instead of moving it. The
host decides whether A and B project (see "Not claimed"). All three points are
closer to the west pocket (planar Bear to A/B/C about 78/85/84 m, USEC about
57/52/75 m); CheckPoint is an attack/defence layout and this is not compensated.

## Decisions

1. **Tactical is blocked on Iceberg (solo and PFT).** The installed data has nine
   Bot/BotPmc markers; a surviving leader on about 48 m² of the centre (within
   x -12.6..12.5, z -71.9..-58.8) has only four of them beyond the unchanged 25 m
   floor (Equator's failure). The team boxes do not help: Tactical uses the generic
   markers. Removing the guard needs a native proof that the region cannot be
   walked, or new box-based Tactical work.
2. **PFT Zombies is admitted**, like AirPit/Bowl: the Equator exclusion is a
   compactness judgement and Iceberg is large (main mesh about 4,788 m²). PFT on
   Iceberg is natively untested.
3. **Default eight participants** (installed `BotMax 8`, nine bot markers).
4. **Team-box interiors** as derived above, argued at `ArenaTeamBoxCatalog.Iceberg`.
5. **Gate timing** without an Iceberg case (above).
6. **Domination anchors** at the authored trigger centres; no floor substitution.
7. **Installed Iceberg data is a harness dependency** (review round). `ArenaPickupChecks`
   reads every installed ported map and fails loudly without
   `db/arena_iceberg/base.json`; its message names the requirement (an ArenaMapPort build
   that ships `arena_iceberg`: the feature build `0.4.0+32fda33` or a later release). So
   `IcebergChecks`, and through ArenaPickupChecks `BowlChecks`, `BowlTeamBoxChecks`,
   `IcebergTeamBoxChecks` and any full harness run, need that installed MapPort data; the
   published 0.4.0 is not enough. No silent skip.
8. **PFT decompile default** (review round). `PortedCoverChecks` reads
   `ARENA_PFT_INSTALLED_SOURCE` when set, else `artifacts/pft-installed/pitTeam.Utils/Covers.cs`,
   and fails naming `tools/Decompile-PitFireTeam.ps1` when neither exists. The script
   decompiles the installed `BepInEx/plugins/pitFireTeam/pitFireTeam.dll` read-only with
   `ilspycmd -p` and the game, BepInEx and SPT reference folders (without them ILSpy drops
   the named argument `checkCurrent: false` the check reads), and refuses a non-empty output
   folder instead of deleting or mixing it.
9. **FORGE-DESCRIPTION.md carries no Iceberg** (review round). It is the manual Forge
   upload draft for 0.4.1, which has no Iceberg; the branch's Iceberg lines were removed
   again. Add them (mode list, 2v2–10v10 pockets, spawn shield, Domination A/B/C) when an
   ArenaMapPort release ships `arena_iceberg`.

## Verification

Offline only, on the committed code of `9f3cb3b3`. Every dotnet call used
`tools/AirPitChecks/NoDeploy.targets` and an absent `SPTUserMods` sentinel; the runner
hashed the live install before and after every step and found it unchanged.

```text
pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 -SkipUnitTests -Harness IcebergChecks,IcebergTeamBoxChecks,SpawnGateChecks,EquatorTeamSpawnChecks,SpawnShieldScopeChecks,PftPortedMapChecks,PortedCoverChecks,NativeMenuChecks,ParticipantMenuChecks,AreaConfigChecks,AreaMapPreviewChecks,ArenaPickupChecks,ArenaLightingChecks,CompactModeUxChecks,ArenaExtractionChecks,DominationAdmissionChecks,DominationRuntimeChecks,TeamDeathmatchRuntimeChecks
```

Result: 18 passed, 0 failed (1,388 s). `IcebergTeamBoxChecks` ran the full suite once:
3,621 tests, 3,618 passed, 3 known environment skips (installed APBS, two reward
real-baseline cases). Before: 3,521 / 3,518 / 3 at `master` `54c464a1`.

| Harness | Result |
|---|---|
| IcebergChecks | installed data 4 Player / 9 Bot rows; 180 filtered server tests; programs below; 6 compiled PFT adapter faults rejected (2 new for Iceberg) |
| IcebergTeamBoxChecks | installed donor `arena_iceberg-bot-core-01` exact; player rows inside their boxes; full suite; EquatorTeamSpawn 1,980 / 12 controls; SpawnGate 337 / 26 controls; MapModeAdmission 801; pickups 514; Release builds of client, companion, server: 0 errors |
| SpawnGateChecks | 337 assertions (OriginalBoxes and CompletionSignal on Iceberg), 26 negative controls |
| EquatorTeamSpawnChecks | 1,980 assertions (Iceberg TDM and Domination), 12 negative controls |
| SpawnShieldScopeChecks | passed, 5 compiled fault controls |
| PftPortedMapChecks / PortedCoverChecks | 314 (+109 native-hook) / 231 assertions, with their fault controls |
| NativeMenuChecks | 956 assertions (Iceberg admission with Tactical refused, Domination lists) |
| ParticipantMenuChecks | 6,173 participant, 207 native-area-hook, 66 area-view assertions, 7 controls |
| AreaConfigChecks | 317 assertions, real BepInEx (rows `bowl-release`, `iceberg-removed`, `future-version`); historical 588090f defects reproduced |
| AreaMapPreviewChecks | 4,490 assertions (ICEBERG label, native position), 5 controls |
| ArenaPickupChecks | 514 assertions; Iceberg 9 installed candidates; 41 controls |
| ArenaLightingChecks | 706 assertions |
| CompactModeUxChecks | 317 assertions ("Hold 3 point" on Iceberg), 3 controls |
| ArenaExtractionChecks | 135 assertions, 12 source faults |
| DominationAdmissionChecks | 172 checks, 10 compiled mutants (reject-arena_iceberg; allow-unreviewed on the Iceberg clause), 4 wiring controls |
| DominationRuntimeChecks | 597 assertions, 13 source faults (new: iceberg-lifted-trigger) |
| TeamDeathmatchRuntimeChecks | 539 checks (Iceberg Domination with three points) |

A no-deploy Release build of `ArenaMode.sln` had 0 errors and the 4 known nullable
warnings in `ClientSettingsModels.cs`.

**New tests proved red first.** Each production line below was broken alone, the
filtered tests ran through the no-deploy wrapper, the named tests failed, and the file was
restored byte-identically: revision-5 server guard (`!bowlDefault`), its `revision < 5`
cutoff, the client AirPit `revision < 4` cutoff, the client Bowl-default clause, the
malformed-marker rule, the Tactical block, the canonical ID, the default of eight, the
server default entry, live discovery; the box `Find` and `Owns` branches, the USEC gate
clip, the USEC north extension, the Bear gate clip and height, a flattened Bear terrace,
the donor row and the BEAR anchor; the Domination admission clause.

**Harnesses repaired on the way** (already broken at `master`, not by Iceberg):
AreaConfigChecks did not compile (Settings.cs needs `ArenaTeamSideSelection` since
`1c86b9c5`) and its PFT summary oracle predated `0e1a3f75`; ArenaLightingChecks did not
compile (`ArenaTeamPalette` needs the population policy since `bdd679ec`);
PortedCoverChecks read a PFT decompile under the cleaned `artifacts/` (see Decision 8 for
its replacement); ParticipantMenuChecks capped only Team DM at 20 on box
maps although Domination shares the cap since `fb587a6b`; AreaTeamMenuChecks' panel-top
literals predated `517ca1db`. `tools/ArenaLabyrinthAccessChecks` did not compile
(`ArenaMenuSelection.cs` needs `Bots`); the review round links the Domination bridge stand-in
(it does not cover Iceberg).

### Review-fix round

Three independent reviews of commits A-D (`54c464a1..d16a3a94`) found no blocker and twelve minor
findings. The fixes below are offline only; nothing was deployed.

- **Menu hint.** `tools/AreaConfigChecks` asserts the Zombies map hints with literal texts:
  Iceberg solo "Too few distant spawns: no Tactical", Iceberg PFT "PitFireTeam squad", Bowl
  empty, Equator "Limited space: no Tactical / squad Zombies". Proved red once: without the
  Iceberg hint line of `ArenaAreaSelectionState.cs` the run stopped at
  `MAP-MODE-HINT: arena_iceberg/zombie (PFT=False) must hint "Too few distant spawns: no Tactical", got "".`
- **Objective count.** The menu's map guard moved unchanged from
  `ArenaModeSelectionView.ControlPointCount` into `DominationMapLayout.ControlPointCount(map)`
  (same catalog predicates; `Resolve` only for a map with a layout, 0 otherwise); the view
  keeps the mode check. `tools/CompactModeUxChecks` checks it against literals: Factory
  day/night 3, Equator 1, Bay5 1, Air Pit 2, Bowl 2, Iceberg 3, Customs, Labs, Labyrinth, an
  unknown ID and null 0. Proved red once: without the helper's Iceberg clause the run stopped
  at `menu objective count for arena_iceberg: expected 3, got 0`. CompactModeUxChecks,
  DominationRuntimeChecks and AnnouncementIntegrationChecks compile `DominationMapLayout.cs`
  without the map catalog and now link it.
- **Server tests.** `IcebergTeamSpawnTests` gains `MissingModeRejectsWithoutTouchingNativeMap`
  and a Tactical row in `OtherModesKeepNativeMarkersAndDoNotRequireTeamBoxDonor`. Unlike Bowl,
  the server mutator refuses Tactical on Iceberg (the resolver's message) before it changes any
  raid data, so that row asserts the refusal and the unchanged map rather than a successful
  mutation. The protected-clone yaw is checked against the authored yaw (every Iceberg yaw lies
  in 0..359.99) instead of `NativeYaw`.
- **Harness dependencies.** Decisions 7 and 8 (installed Iceberg data, PFT decompile);
  `tools/ArenaLabyrinthAccessChecks` compiles again with the Domination bridge stand-in that
  NativeMenuChecks and ArenaStartErrorChecks already link.
- **Documents.** ArenaMapPort 0.4.0 is described as published (2026-10-30) in README,
  DEPENDENCIES, FEATURES and FORGE-DESCRIPTION; the Iceberg sentences stay, the CHANGELOG's
  release-time wording stays, FORGE-DESCRIPTION has no Iceberg (Decision 9).

Both runs went through the runner, which hashed the live install before and after every step
and found it unchanged; PortedCoverChecks ran without `ARENA_PFT_INSTALLED_SOURCE`.

```text
pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 -SkipUnitTests -Harness AreaConfigChecks,CompactModeUxChecks,ArenaPickupChecks,PortedCoverChecks,IcebergChecks,ArenaLabyrinthAccessChecks,AnnouncementIntegrationChecks,DominationRuntimeChecks,TdmMatchMenuChecks,TeamGiveUpChecks,IcebergTeamBoxChecks
ARENA_VERIFICATION_SUMMARY passed=8 failed=3 seconds=690
pwsh -NoProfile -File tools/Run-ArenaVerification.ps1 -SkipUnitTests -Harness CompactModeUxChecks,IcebergTeamBoxChecks
ARENA_VERIFICATION_SUMMARY passed=2 failed=0 seconds=310
```

| Harness | Result |
|---|---|
| AreaConfigChecks | 321 assertions, real BepInEx; historical 588090f defects reproduced |
| CompactModeUxChecks | run 1 failed: the incremental build reused the red-proof binary because the restored file kept its older time stamp; run 2 (file time refreshed, content unchanged): 329 assertions, 3 controls |
| ArenaPickupChecks | 514 assertions, 41 controls |
| PortedCoverChecks | 231 assertions, 25 compiled faults; source `artifacts/pft-installed/pitTeam.Utils/Covers.cs` from `tools/Decompile-PitFireTeam.ps1` |
| IcebergChecks | installed data 4 Player / 9 Bot rows; 180 filtered server tests; its programs; 6 compiled PFT adapter faults rejected |
| ArenaLabyrinthAccessChecks | 50 assertions (the program; its `Verify-*.ps1` scripts were not run) |
| AnnouncementIntegrationChecks | **failed, not repaired**: 12 compile errors in code from `e7d208ad` (master, 2026-10-27): its `Vector3` double has no `x`/`z` for `DominationMapLayout`'s Factory selection, and `TeamDeathmatchMode.Domination.cs` needs `Config` |
| DominationRuntimeChecks | 597 assertions, 13 source faults |
| TdmMatchMenuChecks | 2,670 assertions (the program, compiling the changed view) |
| TeamGiveUpChecks | passed (the program) |
| IcebergTeamBoxChecks | run 1 failed at the first Tactical row (expected a successful mutation); run 2: 3,623 tests, 3,620 passed, 3 known skips (IcebergTeamSpawnTests 52, IcebergMapTests 20 passed); EquatorTeamSpawn 1,980; SpawnGate 337; ParticipantMenu 6,173; NativeMenu 956; MapModeAdmission 801; pickups 514; Release builds of client, companion, server: 0 errors |

`Run-ArenaVerification.ps1` has no test filter; the Iceberg test classes ran inside
IcebergTeamBoxChecks' full suite.

## Not claimed

- **No native run.** Nothing here was played on a GPU host; the orchestrator runs
  the native test afterwards. Open host questions: whether the projection finds A
  (pool basin) and B; whether `Bear house (12)`, 0.53 m from the tunnel wall in
  MapPort's offline model, passes `FloorAndClear`; whether `bot-core-01` reaches all
  twenty pocket points and A/B/C in both directions; gate hold/release, carvers and
  the 2.5 s drive with an Arena hold (MapPort's own raids never held the gates);
  10v10 occupancy; the nine-candidate pickup pool in Domination (cores 03, 04 and 09
  sit next to B, C and A).
- **Marker position.** Iceberg's overview marker (MapPort's `RelativeMapPos`) lies under
  the Arena mode panel in the Arena flow: on GPU-2 (31.10.) only "BERG" showed right of the
  panel, the tile's click point (425, 315) is just clear of it. ArenaMode cannot move it:
  MapPort's `ArenaModeMapPositionIntegration` restores the native anchor after
  `ArenaPortedMapPresentation.Create` (an attempted Arena-flow anchor `.31/.09` had no
  effect and was reverted). Moving the marker east is a MapPort data change.
- PFT Zombies on Iceberg; the monitors (four of six holders show a face; only the
  clock is visible on Iceberg).
- MapPort release: Iceberg must not be announced in an ArenaMode release before an
  ArenaMapPort release ships it; the whitelist, DEPENDENCIES, FEATURES, README and
  FORGE text move together when it does.
