# Chop Shop native acceptance - 2026-11-12

Host: SPT-GPU-2 (`192.168.178.109`, `C:\SPT41`), test profile `SPT-GPU-2`
(`6ab7db1c...`), 1920x1080, SPT 4.1.6, EFT 0.16.9.5. Driven from this machine
through UiBridge `0.9.9+92b998a` and gpu-run of UiBridge `main` (`1a81d87`, which
learned ArenaMode's solo Tactical/Zombies texts "PFT optional" of 2026-11-09 the
same day; without that a Zombies raid fails the mode readback). Branch under
test: `feature/chop-shop`. Contract and offline checks:
[CHOP-SHOP-INTEGRATION-20261112.md](../../reviews/CHOP-SHOP-INTEGRATION-20261112.md).
Raw evidence (scenario reports, stills, log exports, two menu screenshots) is
under the ignored `artifacts/chop-shop-20261112/gpu/`.

Installed beside the builds and unchanged by this work: ArenaMapPort client
`0.5.0+b7876d3` (the unreleased `feature/autoservice` build; the published 0.5.0
carries the same version string and has no Chop Shop), Redeploy.Core
`1.0.22+b1d69e1` (hash compared before and after each build), PitFireTeam
`1.1.1`.

## Build and delivery

Two development builds were deployed. Both were built with
`tools/AirPitChecks/NoDeploy.targets` in a separate clean build worktree, the
stamps were checked, and the three DLLs were copied into `D:\SPT41` (Syncthing
mirrors them to the hosts), followed by `gpu-run deploy-wait --host host2
--server --game`: "3 file(s) identical on the host, server stopped, game
restarted, build 16/16 identical" (VM build = host disk = loaded process). The
results below name the build they were taken with.

| Build | Commit | Client SHA-256 | Used for |
|---|---|---|---|
| 1 | `d843e4c2` (commits A to D) | `7a1cad31a52a53b0cf0bdb0cc704292f3fa176b84eaaa94d369b7581ba00097b` | menu, DM, TDM 4v4 and 10v10, Domination, Zombies (idle death, yard-centre stall), PMC control, second navmesh survey |
| 2 | `0f14cdb9` (watchdog 15 s, failed-dispatch backoff) | `4bc0e1836d21d59b65e9348c86f384e7a4f32c9105ba98bb403f0d8b5da2bfb1` | Zombies with the backoff, TDM at drive scale 1.0, DM, Domination, TDM 4v4 (profile comparison) |

| Build | BigBrainCompat SHA-256 | Server SHA-256 |
|---|---|---|
| 1 | `ad80b4d951ac15ed73657979a3bfd524fa3b4f7bb486ba8b749adbc44dc8179e` | `f9c565b0b3d645dcdeb0c65e1a15f987d2335a195b6617b5bcef3e658e8536ea` |
| 2 | `70fd55910f94a05f12ac170705df9ac318a1f9fe3d05bb1140ec0793d1f2c39d` | `ee00c0a3ef36c8fc0fd3205b8d754f1a5670cc31d8f5bd0b5850021ef14ec08f` |

The previous installed files were kept in
`D:\SPT41-Backups\arena-chop-shop\20261112-142217` (before build 1) and
`...\20261112-144911` (before build 2). The installed build at the end of the day
is rebuilt from the merged master head with the same production code as build 2;
its stamp and hashes are recorded in [HANDOFF.md](../../HANDOFF.md), not here.

### Migration read-back (build 1, first start)

The client config `General/Maps` gained `arena_autoservice` and
`OptionalMapsRevision` went 5 to 6 (read back with `uibridge_config` and from the
host's cfg file). The server `config.json` got `optionalMapsRevision` 6 and a new
`arena_autoservice` row (enabled, `botMax` 8, `maxBotPerZone` 8, `spawnZones`
`BotZone_Main`, `burstMinAliveBots` 4). Both lists were the exact Iceberg default
before.

## Results

| Run (build) | Result |
|---|---|
| Menu (1) | Tactical selected: tiles THE LAB, BOWL, FACTORY, CUSTOMS, THE LABYRINTH, AIR PIT, BAY 5; no CHOP SHOP, ICEBERG or EQUATOR. `gpu-run raid --mode tactical --map-from-menu --map "CHOP SHOP"` fails before any raid: `map tile "CHOP SHOP" is not offered`. Zombies selected: gold CHOP SHOP tile at MapPort's anchor (the label spans about x 975..1108 at y 457 on 1920x1080), clear of CUSTOMS, EQUATOR, INTERCHANGE and of the Arena panel (which ends at x 416). Picked: title "CHOP SHOP", MapPort's banner as thumbnail, "PLAY AREA - WHOLE MAP", the hint "Few distant spawns: no Tactical / squad Zombies" in one line without an ellipsis (the review's doubt about its width is settled), the time choice row as on Iceberg. Mode descriptions "Survive escalating waves; PFT optional." and "Hold 1 point to earn team score." were seen. |
| Deathmatch, default 6 (1, 2) | **Passed** (`chopshop-dm`, 13 steps, 5 expectations): raid in 28 s (location 3.8 to 5.1 s, game started 15 to 32 s), local spawn at the native west player marker (-409.5, -3.7, 495.5), 4 bots of both factions alive after 30 s (SAIN combat and PatrolFollower layers), pickups 6/6 placed from 7 candidates, crowd attached and reactions claimed (kinds `0x3E`; reaction Start at round start), no formation, team-box or Domination line, no dropped spawn request, 0 errors. |
| Team DM 4v4 (1) | **Passed** (`chopshop-tdm`, 21 steps, 4 expectations): `Chop Shop Team DM: restored 20 box markers (USEC east / BEAR west)`; `team-box capacity: USEC 10/10 safe connected places, 4 required`, BEAR the same; `Chop Shop Team DM formation ready: 4v4, USEC east / BEAR west`; local BEAR at west-10 (-405.94, -3.72, 486.42) = Usec house (15); bot spawns and arrivals with "actual" equal to the point (east-06, east-10, west-03, west-04, west-05); MapPort `[SpawnGates] released (ArenaMode Chop Shop TDM countdown finished): ... drove 23 part(s) to the open pose in 4.0 s (scale 0.50), 2 park carver(s) in place, 4 switch(es)`; `Chop Shop Team DM gates open: all twenty formation paths revalidated; 5.00s since release (MapPort GatesOpen)`; at that moment 4 USEC bots in the east pocket (x -339..-354) and 3 BEAR bots in the west pocket; 20 s later 7/7 in the field (x -360..-389); routes from the player to the capture point (48.4 m) and the donor (49.3 m) complete; test-hook kill, death, `Chop Shop team player respawn: arena-autoservice-tdm-v1-west-05, (-409.40, -3.74, 477.39)` = Usec house (10), player read back there. 0 errors. Build 2 again (default drive scale): passed. |
| Team DM 10v10 (1) | **Passed** (same scenario, `participants=20`, `formationLine=Team DM formation ready: 10v10`): capacity 10/10 with 10 required on both sides, gates open 4.02 s after release, 10 USEC bots in the east pocket and 9 BEAR bots plus the local BEAR in the west pocket, 18 or more bots later in the field, respawn at west-05. 0 errors. |
| Team DM 4v4 at drive scale 1.0 (2) | **Passed.** MapPort `Spawns/SpawnGateDriveScale` set to 1.0 through the bridge with `persist=false` (the cfg kept 0.5; set back afterwards): MapPort `drove 23 part(s) to the open pose in 8.0 s (scale 1.00)`, `drive frames: 958 frame(s) in 8.0 s, mean 8.4 ms, max 27.6 ms`; ArenaMode `gates open: all twenty formation paths revalidated; 9.00s since release`. |
| Domination 4v4 (1, 2) | **Passed** (`chopshop-dom`, 14 steps, 5 expectations): `Domination points verified: X=(-369.290, -3.805, 499.490)` (the light root's own point, projected 5 mm), formation 4v4, gates open 5.00 s, `Domination objective routes from both opened team boxes verified`, `Domination ownership: X=neutral` then `X=USEC` 54 s after the start, bots on the "Arena Combat Domination" layer, HUD "4v4 BEAR 0 : 48 USEC \| First to 300 points \| 08:58 left" and "X USEC", MapPort's timer monitor on the wall showing the same 08:58, scoreboard sprites loaded by MapPort's Monitors. Pickups: **5 of 6 placed** in every attempt (see "Known limits"). 0 errors. |
| Solo Zombies, idle player (1) | An idle player without godmode died 24 s after the first infected arrived; `Zombie solo survival ended on local death; respawn held through protected exit`, protected native end, as designed. |
| Solo Zombies, godmode (1) | 32/32 infected within 35 s from the east pocket spawn (`points 6 preferred + 0 distant`). With the player teleported to the yard centre (-379, -3.8, 495): `points 0 preferred + 0 distant, selected 0` and `Arena burst spawn: no usable spawn point for <role> - request dropped` (Warning) **133 times in 30 s**, each after a profile request. |
| Solo Zombies, godmode (2) | **Passed** (`chopshop-zombie`, 18 steps, 3 expectations). With the failed-dispatch backoff of 2 s: **18 dropped requests in 36 s** (one every 2.1 s) with the player in the yard centre; the refill resumed 1.1 s after the player was back at the pocket (`infectedAssault arrived`), 32/32 again, 0 errors. |
| PMC control (1) | **Passed** (`chopshop-pmc`): ordinary PMC raid, bots Low, 3 Scavs on native PatrolAssault layers, **no ArenaMode log line and no error** from the raid's start to its end (`uibridge_log source=ArenaMode`: 0 lines), left through the "Chop Shop East Gate" exfil (survived). Arena maps are not offered to Scavs ("This location is not available for Scavs"). |
| Domination started 5 s after the previous Arena raid (1) | Refused with `Arena raid-start hook failed: InvalidOperationException: PitFireTeam post-raid finalization is still settling; retry shortly`; the load then hung until gpu-run's cut-off. This is the known settle time between two Arena raids (about 40 s), not a Chop Shop matter. Repeated 45 s after the menu: passed. |

## Findings that changed the build

Both were found with build 1 and fixed in `0f14cdb9`, then re-run with build 2.

**The gate watchdog.** The match flow polls the opening of the gates once a
second. At drive scale 0.5 MapPort reported the gates open 4.0 s after the release
and ArenaMode revalidated the formation at 5.00 s; at drive scale 1.0 the drive
took 8.0 s and the revalidation came at 9.00 s. The ordinary ten-second watchdog
would leave two polls after an eight-second drive, and a missed poll ends
the protected match. Chop Shop got a 15 s watchdog; the contract's earlier "2 s of
margin" was a number about the drive, not about the polls. Build 2 passed at both
scales (5.00 s and 9.00 s).

**The yard-centre spawn stall.** With the player in the yard centre no Bot row
lies beyond the 25 m floor, so every Zombies dispatch failed after its profile
request and the wave retried on its pacing cadence: 133 dropped requests in 30 s
(build 1). A failed dispatch reported with its time now holds the next attempt of
the wave for two seconds (`ZombieWavePolicy.ReportFailedDispatch(ref state, now)`,
`FailedDispatchBackoffSeconds` 2): 18 dropped requests in 36 s (build 2). The slot
is still retried and nothing is written off; the 25 m floor and the rows are
unchanged. This is a general Zombies change, not a Chop Shop case.

## Known limits seen natively

- **Domination places 5 of 6 pickups.** Log: `placed=5/6; ... refusals=[another
  pickup is within 3m=..., control point or its 2m clearance=2]`. The Bot rows
  core-01 (5.0 m from the capture point) and core-02 (2.4 m) lie inside the 6 m
  objective clearance; five rows remain for six pickup kinds, and the Health
  pickup is the one left out. It happened in every attempt.
- **A dead patch in the yard centre.** About 4 % of the yard (MapPort's mesh
  measurement) has no Bot row beyond the 25 m floor. Zombies use the backoff
  above; Deathmatch and Gun Game drip more slowly there (one attempt per 3 s) by
  the same rule.

Both would go away with more Bot/BotPmc rows in ArenaMapPort's data for this map.
ArenaMode does not edit that data; it is a possible ArenaMapPort follow-up.

## The two navmesh surveys

Both are teleport dry runs (`uibridge_teleport dryRun`, `NavMesh.SamplePosition`,
room to stand, path) in an ordinary PMC raid with the gates opened by MapPort,
display precision 0.1 m.

- **First survey (before the final literals).** The capture point
  (-369.29, -3.80, 499.49) is accepted at the requested point (snap below 0.05 m),
  with complete paths of 42.2 m from the west spawn and 37.1 m from Usec house (4).
  All seven Bot rows are reachable from both pockets (west 25.6 to 57.3 m, east
  26.9 to 67.5 m); core-03 has 44.4 m (10 corners) and 39.9 m (9 corners). All 20
  markers pass navmesh, zone and room-to-stand; three east markers (Usec house
  (2), (3), (4)) were refused only by the tool's straight-line wall heuristic
  (`crosses_geometry`), because the sweep meets the yard ramp's own top; the
  navmesh paths are complete (80.9 to 86.3 m). The west roof is walkable at
  y 1.7 to 1.9. Result: box height -4.8 .. -1.0.
- **Second survey (build 1, grid on 2.5 m at y -1.6, surfaces between the floor and
  y 0.4).** East footprint: curbs at -3.3 along the east wall (x -333.6..-334.0,
  z 482.9..501.1) and at the north-east corner (-333.6, -3.1, 511.0), and a step at
  the west wall (-343.6, -3.0, 488.1), (-343.5, -2.5, 489.5) are connected to the
  pocket floor (complete paths from Usec house (4), 9.0 to 15.1 m). Separate
  islands (`path_partial` from Usec house (4) and from the donor in the yard): a
  strip along the north edge (-340.5, -1.7, 511.4), (-341.7, -2.0, 511.5),
  (-337.5, -2.8, 512.0), (-338.5, -0.8, 511.7); a ledge along the west wall at
  -0.3 (x -343, z 494.5..501); (-339..-343, -0.4, 473.3..473.7) south of the box.
  West footprint: only the floor, a curb (-403.8, -3.3, 482.2) and an unstandable
  shelf at the south end (x -408..-411, z 474..475, "no room to stand"); the roof
  at 1.7 to 1.9. Consequence: inside the band -4.8 .. -1.0 but unreachable are the
  north-edge strip at y -1.7 .. -2.0 (x -340.5 .. -341.7, z 511.2 .. 511.5) and the
  west shelf. Everything at -0.8 and above is outside the band. The band was not
  narrowed: 0.3 m above the step would be the only room for it.

## Profile and configuration

- Profile: snapshots before and after one Arena Team DM raid (build 2): of 5,370
  leaves exactly one differs, `characters/pmc/Hideout/sptUpdateLastRunTimestamp`;
  the two other profiles and the PitFireTeam server files are byte-identical
  (SHA-256 list). Across the whole session the remaining differences are those of
  the one ordinary PMC control raid (Experience 200 to 300, skill and counter rows,
  session date) and a PitFireTeam migration marker that raid wrote. No reward was
  earned: every Arena match was left before its result.
- Config: gpu-run changed `Match/GameMode` and `Map participants/arena_autoservice_team`
  (6 to 8). `GameMode` was set back through the bridge; the protected participant
  key was set back by a cfg edit before the final start. MapPort's drive scale was
  never saved.

## Not run

- PitFireTeam on this map (both PFT modes are refused).
- Gun Game (it takes the Deathmatch path).
- A match played to its result and its reward.
- Deathmatch or Gun Game with more than 6 participants (up to 23 bots from 7 rows).
- Crowd reactions heard and monitors read by a person: the log shows reactions
  claimed and sent, the screenshot shows the timer monitor.
- Daytime, weather, performance over a long session.
- The DynamicMaps overlay (MapPort ships no folder for this map).
- The killcam (switched off in the test configuration); Redeploy's respawn ran in
  every Team DM death.
