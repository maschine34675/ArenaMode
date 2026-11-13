# Spawn rows follow-up: Tactical on Equator, Iceberg and Chop Shop, exits in the pockets

Date: 2026-11-13, branch `feature/spawn-rows` from `master` `99260275`. Code and pins are commit
`2a085839` ("Follow ArenaMapPort's new spawn rows: Tactical on Equator, Iceberg and solo Chop Shop, pins on
the decisions"); this document and the living documents are the commit after it. ArenaMode neither
distributes nor changes ArenaMapPort's data. The request (user, 2026-11-13): the maps, Chop Shop in
particular, got more spawn points; see ArenaMapPort's record `docs/progress/spawn-rows-and-pocket-exits.md`
in `D:/ArenaMapPort` (read-only from here).

**Status in one line.** ArenaMapPort 0.5.0+160523d (rows of 2026-11-12) removed the row argument of the
three Tactical refusals and of two participant defaults. They are decided again against the installed data:
Tactical is admitted on Equator and Iceberg (solo and PitFireTeam) and on Chop Shop solo; PitFireTeam
Tactical stays refused on Chop Shop; Chop Shop defaults to eight participants; every formation names its two
pocket exits; the harness pins follow the decisions. The native runs are in the `NATIVE:` paragraph below and
in the report of the same day.

## What ArenaMapPort changed

ArenaMapPort appended Bot/BotPmc rows on all six ports (39 rows in all; the six donor rows are byte-exact,
no appended row lies inside a team box plus 2.5 m, the four Player rows of each map are unchanged) and moved
the two exits of Chop Shop, Bay5, Air Pit, Bowl and Iceberg into the team pockets (Equator's already stood in
its booths). It also moved Chop Shop's selection marker; that is MapPort-side. Installed build:
`0.5.0+160523d` (merge `48b316d` plus a records commit), the `base.json` files under
`user/mods/ArenaMapPort/db/arena_<map>/`. The published ArenaMapPort 0.5.0 has neither these rows nor Chop
Shop and the same version string, so nothing here can be detected by version.

| Map | Bot/BotPmc rows | Rows 25 m or more from the spot that decided the old refusal | Minimum over the map (MapPort's mesh figure) |
|---|---|---|---|
| Equator | 16 (was 10) | 9 from the atrium (0, .2, 2) (was 4) | 6, 100 % of cells with five or more |
| Iceberg | 17 (was 9) | 9 in the old centre rectangle (was 4) | 10, 100 % |
| Chop Shop | 16 (was 7) | 5 from the yard centre (-379, -3.8, 495) (was 0); 3 at (-375, -3.8, 487) | 3, 98.3 % |
| Bay5 | 16 (was 10) | not a decision here | 9 |
| Air Pit | 20 (was 14) | not a decision here | 15 |
| Bowl | 21 (was 17) | not a decision here | 12 |

The counts were recomputed from the installed `base.json` files with the production rule (distinct
coordinates, 3-D distance, the unchanged 25 m floor of `BurstSpawnPointSelector`): Equator 9 at the atrium
and 6 at (-4, .2, 15); Iceberg 9 at (10, 3.15, -68) and 9 over the whole old centre rectangle (x -12.6..12.5,
z -71.9..-58.8); Chop Shop 5 at the yard centre, 3 at (-375, -3.8, 487) and 11 to 13 from each of the four
Player rows (12, 13, 11, 11). Every Player row of Equator (12 to 14) and Iceberg (15 to 17) has more than
five. All Bot/BotPmc rows are `BotZone_Main`; Chop Shop's ids are the seven old ones plus `lasthero-04`,
`lasthero-13`, `fill-01`, `core-06`, `lasthero-03`, `lasthero-10`, `fill-02`, `fill-03`, `fill-04`.

Measured natively on GPU-2 on 2026-11-13 with the build that was installed then (ArenaMode `0.5.0+3ea7eabb`,
MapPort `0.5.0+160523d`, so before this change): Chop Shop Domination placed 6 of 6 pickups
(`placed=6/6`, 16 candidates, Health included); solo Zombies with the player in the yard centre from the
start filled 32 of 32 with "points 5 preferred" and no dropped request. Both known limits of
[the Chop Shop contract](CHOP-SHOP-INTEGRATION-20261112.md) (Domination 5 of 6, the dead patch in the yard
centre) are gone. Evidence stays in the ignored `artifacts/spawn-rows-20261113/gpu/`; the survey's simulation
of the pickup pools (16 candidates on every map but Equator's 15, 14 usable on Chop Shop after the 6 m
Domination clearance) agreed with that run.

## Decisions

1. **Tactical refusals** (`ArenaAreaCatalog.TryResolve`). How Tactical really uses rows, read in the code:
   `TacticalMode.RequestSoloSquad` spawns the squad without `requireExactCount`;
   `SoloTacticalSpawnPolicy` waits up to 45 s and then starts with a partial squad; the squad is requested before a solo
   survivor is moved to a Player row at the round break (`LiveKitRespawn.AtRoundBreak`); see decision 9. The only `requireExactCount: true` is in the
   PitFireTeam path `RequestTeamEnemies`, and a PitFireTeam survivor stays where he stands, so a shortfall there
   ends the match as no contest.
   - **Equator: lifted, solo and PFT.** Its one recorded reason (four rows beyond 25 m from the atrium) is
     gone: 9 there, 6 at the worst spot of its mesh.
   - **Iceberg: lifted, solo and PFT.** At least 9 everywhere on the old centre rectangle, 10 on the mesh.
   - **Chop Shop: solo lifted, PFT Tactical stays refused.** Solo never needs exactly five. In the native runs of
     2026-11-13 its squad was planned from a Player row (11 to 13 rows), but the code does not guarantee that
     (decision 9); the worst spot has a minimum of 3 rows, which is a smaller or later squad after the 45 s
     wait, not a stuck round. A PitFireTeam leader who survives in the yard centre (a minimum of 3 inside
     x -377..-373.5, z 485..489) would end the match as no contest. The reason text says so: "Chop Shop's yard centre has too
     few distant bot spawn points for a PitFireTeam Tactical squad; select Solo or another map."
   - **PFT Zombies on Equator and Chop Shop stays refused.** That argument is space (Equator is compact; Chop Shop's
     main walkable component is 1,915.6 m2 with 1 to 2.4 m corridors at the gate mouths), not rows, so the new
     rows do not touch it.
   - The comments at the code state the installed counts of 2026-11-13 and the solo/PFT difference; the old
     arguments (four of nine, seven rows, 437 of 510 cells) are deleted.
2. **Picker hints** (`ArenaAreaSelectionState.ModeHint`, solo only; PFT shows "PitFireTeam squad"): Equator
   "Limited space: no squad Zombies", Iceberg none, Chop Shop "Small map: no squad Tactical / Zombies".
   `ArenaLocationSelectionPatch`'s "Equator: too small for this mode. Choose a map." stays; it now fires only for
   squad Zombies.
3. **Participants.** Chop Shop joins the 8-group. Its argument for six was "seven rows"; it has sixteen, as
   many as Bay5 and Equator (Iceberg 17, Air Pit 20, Bowl 21), and Arena's own TeamFight on this map is 5v5. Equator stays at 4: that
   preset dates from 2026-09-21, was never derived from rows (`BurstMinAliveBots` is 4 on every port), and the
   comment no longer says it follows from the distant-row count. A value saved per map is not migrated; only a
   fresh configuration gets the new recommendation.
4. **Tactical objective.** With Tactical admitted, the capture point applies on Equator (X), Iceberg (one of
   A/B/C per round) and Chop Shop (X) through the existing `DominationMapLayout` rows with the formation's
   Destination as navigation anchor. No code change; the `Settings.cs` description that listed which maps have
   it now names all seven. Not run natively on these three maps.
5. **BoothExits.** Every formation names its two exits, USEC pocket first, the order Equator's entry uses:
   Equator "Equator North Gate" / "Equator South Gate"; Bay5 "Bay5 North Loading Bay" / "Bay5 South Container
   Lane"; Air Pit "Air Pit North Gate" / "Air Pit South Gate" (the south one stands at the pocket mouth: its box
   edge is 0.4 m outside the pocket bound and its centre 2.4 m outside, MapPort's listed exception); Bowl "Bowl
   BTR Gate" / "Bowl Army Gate"; Iceberg "Iceberg West Gate" (on flat floor) / "Iceberg East Gate" (on the upper
   west part of the ramp, where the floor falls from y 1.9 to 0.75; the wording of ArenaMapPort's
   `tools/maps/iceberg.exits.json`); Chop Shop "Chop Shop East Gate" / "Chop Shop West Gate". The member stays
   data: extraction is isolated in Arena raids (`ArenaExtractionIsolation` matches by object and exit name,
   nothing reads the positions), so the flares only mark the pockets. The comments that said "no booth exits" or
   put the exits beyond the gates are corrected. MapPort stood in the Bowl and Iceberg boxes in two Team DM runs
   without extracting (the Iceberg first spawn, Bear house (9), lies inside the east box).
6. **Harness pins follow the decisions, not the row lists** (next section).
7. **Documents.** This contract, the living documents (AREA-RULES, FEATURES, MAP-MODE-COMPATIBILITY,
   DEPENDENCIES, CHANGELOG, HANDOFF) and a dated note at the top of the three contracts whose Tactical or exit
   statements changed. The reports of 2026-11-12 and older stay as they are. README, FORGE-DESCRIPTION and the
   whitelist stay untouched: no ArenaMode release may announce Chop Shop or the new Tactical maps on unreleased
   ArenaMapPort data before an ArenaMapPort release ships it.
   **Release blocker (lead decision, 2026-11-13).** The published ArenaMapPort 0.5.0 has Equator 10 Bot rows
   (4 distant from the atrium, grid minimum 3), Iceberg 9 (4 distant in the centre) and no Chop Shop; the
   unreleased build with the rows carries the same version string 0.5.0. The reviewed versions of
   `assets/arena-whitelist.json` are advisory: `ArenaWhitelistAdmission` says "Reviewed versions are advisory
   for new sessions" and turns a reviewed-version difference into a note that does not obstruct character
   selection, never into a refusal. Bumping the pin therefore changes a warning text and nothing else, and no
   version compare can tell the two 0.5.0 builds apart. With the published data an ArenaMode built from this
   branch admits PitFireTeam Tactical on Equator and Iceberg, and a leader who survives in the middle gets four
   rows instead of five: `RequestTeamEnemies` ends the match as no contest; solo gets a partial squad after
   45 s. **No ArenaMode release from this state before an ArenaMapPort release ships the rows, and that
   release must carry a new version number.** At release time decide between (a) requiring that ArenaMapPort
   version for PitFireTeam Tactical on Equator and Iceberg at run time (the plugin version, or a count of the
   raid's Bot rows) and (b) an accepted, documented requirement in the release notes. The README.md and
   FORGE-DESCRIPTION.md lines that still say Equator and Iceberg exclude Tactical change in that release,
   not now.
8. **Out of scope here** (filed as separate tasks): Arena arms even when `ArenaExtractionIsolation` failed to
   install (it should refuse), and bots leaving through SAIN's extraction, which depends on the SAIN preset and
   has no Arena-side guard.
9. **Known limit: solo squad dispatch order.** In solo Tactical, `TacticalMode.StartRound` requests the squad
   (`RequestSoloSquad`) before it starts `LiveKitRespawn.AtRoundBreak`, and `BurstSpawnService.SpawnPair` reads
   the player position when `BotCreationData.Create` completes. If the profiles are ready before the swap has
   moved the player, the plan is made against the old position. Native timings of 2026-11-13 (GPU-2, build
   0.5.0+e90d1f72, ArenaMapPort 0.5.0+160523d), solo Tactical on Chop Shop in one raid:
   - Round 1: player on the east Player row (-338.5, -3.8, 490.5); the five opponents stood 24.3, 25.1, 40, 40
     and 42 m away a moment after they went live.
   - Round 2 after a death: the player respawned on the west row (-409.3, -3.7, 487.0); opponents at 27.4, 47.1,
     52.8, 54.3 and 57.9 m, that is, picked against the new position.
   - Round 3 as a survivor who stood in the worst cell (-375, -3.8, 486.5) when round 2 ran out: log
     `09:59:20.463 Tactical round 3: requested 5 opponents; spawn service planned 5`, `09:59:20.538 Arena round
     respawn complete`, `09:59:22.258 Arena burst spawn plan: ... points 13 preferred + 0 distant, selected 5`,
     `09:59:23.249 Tactical round 3 live: 5 active opponents`. Thirteen is the count from the east Player row the
     player was moved to, so the move (0.075 s after the request) landed before the points were picked (1.8 s
     after the request). Opponents at 22.3, 23.1, 24.3, 43.8 and 57.0 m one second after they went live.

   The same order held in nine further round starts of the scenario runs on Iceberg, Equator and Chop Shop: the
   body change came 0.06 to 0.28 s after the request and the pick 1.2 to 10.6 s after it (table in the native
   report). So in all ten measured round starts the points were picked after the move; the code does not
   guarantee that order.
   The reviewer's simulation of the lost race (old position uniform over all rows, destination a Player row at
   least 40 m away) gives at least one squad point within 15 m / 25 m of the new Player row in 43 % / 67 % of
   rounds on Equator, 63 % / 97 % on Chop Shop, 0 % / 12 % on Iceberg, and on the maps admitted before: Bay5
   32 % / 68 %, Air Pit 12 % / 15 %, Bowl 0 % / 5 %. The limit exists in solo Tactical on every map; the three
   new maps do not introduce it. The reorder (move first, then request, or read the position at the swap) is
   filed as a separate task and is not part of this change; the lifts stay as decided.

## Pins

The old pins encoded a row list (`ChopShopChecks`: the exact seven ids) and a count (`MapModeAdmissionChecks`:
Equator from the atrium equals 4). A MapPort data improvement turned both red on master, and through
`MapModeAdmissionChecks` the three team-box harnesses too, although nothing that Arena does was wrong. The
new pins hold what the decisions rest on:

- `tools/ChopShopChecks/Verify.ps1`: `Id` and `Enabled`, four Player rows with the infiltration, at least 16
  Bot/BotPmc rows, all `BotZone_Main`, the donor row `arena_autoservice-bot-core-03` with its exact floats
  (-367.44, -3.79, 492.11). No id list: a later row change needs no re-listing.
- `tools/MapModeAdmissionChecks/Program.cs`, computed with the production selector on the installed data:
  Equator at the atrium and at (-4, .2, 15) each at least five (Tactical admitted solo and PFT); Iceberg at
  (10, y of the nearest row level, -68) at least five; Chop Shop: every Player row at least five (solo
  Tactical) and (-375, -3.8, 487) at least one and below five (the evidence of the PFT refusal). The last
  pin is directional on purpose: when MapPort closes that patch it fails and the PFT rule is to be re-decided,
  not the number re-pinned. The admission matrix follows decision 1 and now includes Iceberg and Chop Shop.
  Compiled faults: `ChopShopPftTactical`, `EquatorTacticalRefused`, `IcebergTacticalRefused`,
  `ChopShopSoloTacticalRefused` (12 in all, was 9).
  Added after the review of the same day (labels as the harness prints them):
  - `SPAWN-ROWS-DATA`, before the squad and evidence pins of the three maps: the installed `base.json` exists (a
    missing `arena_autoservice` file, or the stub without spawn rows that ArenaMapPort's tag `v0.5.0` holds for
    it, fails with a sentence, not a raw exception) and has at least 16
    (Equator), 17 (Iceberg) and 16 (Chop Shop) distinct Bot/BotPmc rows. On older data the message says that the
    installed ArenaMapPort data predates the spawn rows of 2026-11-12, that build 0.5.0+160523d or a later
    release is needed (the published 0.5.0 has 10 and 9 rows and no Chop Shop), that the Tactical admissions of
    2026-11-13 were decided on those rows, and that a red pin on older data is not an ArenaMode defect. It still
    fails, because the decision cannot be verified on old data; through `MapModeAdmissionChecks` the three
    team-box harnesses go red with it.
  - `EQUATOR-GRID` and `ICEBERG-GRID`: the minimum of the production selector (`BurstSpawnPointSelector.CountUsable`)
    over the bounding box of the map's Bot/BotPmc rows in 1 m steps, with y taken from the nearest row, is at
    least 5 (today 6 on Equator, 9 on Iceberg).
  - `CHOPSHOP-GRID`: the same minimum is at least 1 and below 5 (today 3), the evidence of the PFT refusal over
    the whole box beside CHOPSHOP-YARD-CENTER. It is directional on purpose, like that pin.
  - `EXIT-NAMES`: for each of the six ports the exit names of the installed `base.json` equal the `BoothExits`
    of `ArenaTeamBoxCatalog` for that map, as a set (two each). The pin has its own loop over the six
    formations, because Bowl has a formation and no row in the mode matrix.
  - Compiled faults for these pins: `SpawnFloor30m` (the 25 m floor raised to 30 m: Equator's grid minimum
    falls to 4 and the fault dies at `EQUATOR-GRID`), `RenamedExitLiteral` and `RenamedBowlExitLiteral` (an
    exit literal renamed in the catalogue dies at `EXIT-NAMES`); 15 compiled faults in all.
  - Run on 2026-11-13 against scratch roots built from ArenaMapPort's tag `v0.5.0` (`git show` of its
    `base.json` files, nothing installed): with all six maps from the tag the harness stops at
    `SPAWN-ROWS-DATA: the file has 10 distinct Bot/BotPmc rows, at least 16 are required for arena_equator`;
    with the installed rows and the tag's stub `arena_autoservice` (it has no `SpawnPointParams`) at
    `SPAWN-ROWS-DATA: the file carries no spawn rows (a stub location) for arena_autoservice`; with that file
    removed at `SPAWN-ROWS-DATA: the file ... is missing for arena_autoservice`. Each message goes on with the
    sentence about the needed build. On the installed data the harness passes with 976 assertions and prints
    the grid minima 6 (Equator), 9 (Iceberg) and 3 (Chop Shop).
- Every other oracle that encoded the refusals, hints or the default follows the decisions: AreaConfigChecks
  (matrix and hint literals), NativeMenuChecks (tactical flags and the Equator PFT admission), ParticipantMenuChecks,
  CompactModeUxChecks, PftPortedMapChecks, NativeAreaHookChecks, CustomsAllModesChecks, the NegativeControls of
  AirPitChecks and EquatorMapChecks, and the xunit tests (`ChopShopMapTests`, `IcebergMapTests`, `EquatorMapTests`,
  `MapModeAccessTests`, the five TeamSpawn classes for `BoothExits`, the participant default, `ZombieWavePolicyTests`).
- `tools/ArenaExtractionChecks` runs the twelve real exit names of the six ports through its name loop. The
  loop proves that the isolation blocks each of those names when the controller holds it; it cannot notice a
  rename on MapPort's side, because it feeds the same literal to both sides. The `EXIT-NAMES` pin above does.
- `tools/ChopShopChecks/scenarios/chopshop-zombie.json` starts in the yard centre and expects no dropped
  request ("no usable spawn point" absent); its README section and the other Chop Shop and Iceberg README
  lines say what the rows are now.

The design named (-377, -3.8, 487) as the three-row spot. The production selector counts five there; the
worst cell is (-375, -3.8, 487) with three, and that is what the pin uses.

## Verified

Offline, through `tools/Run-ArenaVerification.ps1` (xunit once, then the harnesses; every step hashes the
live install before and after), on 2026-11-13 for commit `2a085839`:

- Baseline on `master` against the new data, before the change: `ChopShopChecks` red on its seven ids,
  `MapModeAdmissionChecks` red at "EQUATOR-CENTER: real mid-map survivor cannot receive five Tactical
  opponents", and through it `ChopShopTeamBoxChecks`, `IcebergTeamBoxChecks` and `BowlTeamBoxChecks`.
- xunit: 3,903 tests, 3,900 passed, 3 known environment skips (two reward real-baseline cases and installed
  APBS).
- Passed: `ChopShopChecks` (installed data 4 Player and 16 Bot rows; ArenaPickup 521 assertions; eight compiled
  PFT adapter faults), `MapModeAdmissionChecks` (12 compiled faults), `AreaConfigChecks` (345 assertions, real
  BepInEx), `NativeMenuChecks` (1,077), `ParticipantMenuChecks`, `CompactModeUxChecks` (332 and its mutants),
  `ArenaExtractionChecks` (167 assertions, 12 source faults), `PftPortedMapChecks` (340), `TdmMatchMenuChecks`,
  `AirPitChecks`, `NativeAreaHookChecks` (208), `CustomsAllModesChecks`, `AreaMapPreviewChecks` (4,512),
  `DominationAdmissionChecks` (12 compiled mutants, 7 wiring controls), `ParticipantServerChecks`,
  `IcebergChecks`, `BowlChecks`. `EquatorMapChecks` was red on the first run of the pass (its negative controls
  still expected the refusal) and passed after `tools/EquatorMapChecks/NegativeControls.ps1` followed the
  decision.
- Known red, not caused here: `AnnouncementIntegrationChecks`, `tools/PftTacticalKitChecks/Verify-Integration.ps1`,
  and the xunit flake `ArenaDirectStartTests.AlreadyStartingServerIsReusedAndForeignImageIsRefused` (passes on
  rerun).

VERIFY-RERUN: (lead fills in the green runs of `ChopShopTeamBoxChecks`, `IcebergTeamBoxChecks` and
`BowlTeamBoxChecks` and the final run of the branch; the logs of the pass above hold only their red baseline).

NATIVE: run on GPU-2 on 2026-11-13, report
[GPU-SPAWN-ROWS-20261113.md](../reports/2026-11/GPU-SPAWN-ROWS-20261113.md). With the build that was installed
before this change (`0.5.0+3ea7eabb`) and the new rows: Chop Shop Domination placed 6 of 6 pickups from 16
candidates; solo Zombies with the player in the yard centre from the start filled 32 of 32 with no dropped
request; Team DM 4v4 on Chop Shop with the exits in the pockets spawned and respawned beside the exit box
without an extraction. With build `0.5.0+e90d1f72`: with a PitFireTeam squad the Tactical menu offers Iceberg
and Equator and not Chop Shop; solo Tactical ran three rounds on Chop Shop, Equator and Iceberg with the
committed scenario `tactical-solo-ported` (20 steps each, every round live with five opponents, the capture
point resolved in every round: Chop Shop X, Equator X, Iceberg C then B); PitFireTeam Tactical with two
companions went live with three allies against five opponents on Iceberg (three rounds, the third with the
surviving leader in the old centre: 11 usable rows) and on Equator (round 2 with the surviving leader at the
worst spot (-4, 0.1, 15): 6 usable rows, the offline grid minimum). No error in the BepInEx log. The commits
after `e90d1f72` change comments, harnesses and documents only.

## Not claimed

- A PitFireTeam Tactical match to its end, a won round, or a capture-point hold on Equator, Iceberg and Chop
  Shop. The test timing (60 s rounds, the point after 60 s) ends each round when the point appears: the point
  was resolved and announced in every round (`objective: point at ...`, `the capture point is up`), and no hold
  was run. PitFireTeam Tactical on Chop Shop is refused and was checked in the menu only.
- A respawn onto an in-box formation point (Iceberg Bear house (9) and Usec house (11) are the markers inside
  the exit boxes; the selector chose a point outside the box four times in MapPort's run, and MapPort's record
  names one unverified window while the dead body is still the main player).
- Bots at each of the 39 new rows (one scav was seen next to one new row), Team DM on Air Pit and Bay5 with the
  new exits (Chop Shop was run, see `NATIVE:`), and Gun Game and Deathmatch with the larger participant counts
  the rows now allow.
- The two out-of-scope hardening items of decision 8: Arena still arms when the extraction isolation failed
  to install, and whether bots leave through SAIN's extraction depends on the SAIN preset.
- The 1.7 % of Chop Shop mesh cells with fewer than five distant rows (MapPort: 98.3 % have five or more,
  minimum 3). Solo Tactical there is a smaller or later squad by decision 1; PitFireTeam is refused for the same
  reason. The rule is only as good as the installed data.
- Behaviour on the published ArenaMapPort 0.5.0 data (Equator 10 rows, Iceberg 9, Chop Shop none): Arena has
  no run-time row check, so with those files Tactical is admitted on Equator and Iceberg although they cannot
  always place five distant opponents (PitFireTeam: no contest; solo: a partial squad after 45 s). This is
  the release blocker of decision 7: the two 0.5.0 builds share a version string and the whitelist versions are
  advisory, so nothing at run time tells them apart. The offline pins read the installed data only, and
  `SPAWN-ROWS-DATA` fails on the published data.
- The order of the solo squad dispatch against the round-break move (decision 9). The ten measured round
  starts agree with the intended order, the code does not enforce it, and the simulation of the lost race is
  the reviewer's, not a native run.
- The pickup simulations of the survey for the other maps (pool sizes, Domination usable counts); only Chop Shop
  Domination was placed natively.
- Hearing the flares, sound, night and rain at the new exits; the release of ArenaMapPort that carries all of
  this: the rows exist only in an unreleased build.
