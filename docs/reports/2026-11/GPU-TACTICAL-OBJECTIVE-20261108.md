# Tactical capture point: native runs on GPU-2 (2026-11-08)

Three solo Tactical raids, then two PitFireTeam team matches, on Factory with the development
build of the Tactical objective package ([review](../../reviews/TACTICAL-OBJECTIVE-20261108.md))
on SPT-GPU-2
(`192.168.178.109`, `C:\SPT41`, profile `SPT-GPU-2`), driven from the VM with gpu-run,
UiBridge 0.9.9 and Windows-MCP. Dated evidence, not proof about any other build. The first
raid ran the package as reviewed; the door reset was corrected twice from what the raids
showed (below) and the third raid ran the committed state.

## Build and delivery

- `dotnet build ArenaMode.Client` and `ArenaMode.Server` (Release) on the VM at 15:06
  deployed client and server `0.5.0+f1ba6b95…` (the working tree on top of `f1ba6b95`; the
  product version names the HEAD commit) into `D:\SPT41`. `gpu-run deploy-wait --host host2
  --server` with the two DLLs and the Core DLL: identical on the host after 12 s, the host's
  SPT server was not running and was started (pid 11152, ready after 16 s). `gpu-run start
  --host host2`: main menu after 70 s, `16/16` maschine plugins identical, `2/2` libraries,
  plugin load 40 ok.
- **Core drift (observation, not part of the package):** the installed
  `maschine-Redeploy.Core.dll` was `1.0.20+ade752a` before this session's builds. The
  no-deploy client build at 14:17 (NoDeploy.targets does not cover Core) and the deploy build
  wrote Redeploy's current `main` Core, **`1.0.21+bebf49b0`** (Plate blood state, Skills
  Extended owned body, ladder dismount, Loot In Vicinity owner; committed on Redeploy `main`,
  no public API change), to `D:\SPT41`, and Syncthing mirrored it to every host. ArenaMode
  is built against that Core (`CoreVersionGate` refuses an older one), so the 1.0.20 bytes
  could not be put back without rebuilding against them. All three raids ran with Core
  1.0.21; the user decides whether that stays.
- Second delivery (door reset enumerating through `LocationScene.GetAllObjects<Door>`) at
  15:25, third (enumerating `WorldInteractiveObject` and keeping the `Door`s, one Debug line
  per round start) at 15:34, each with `gpu-run deploy-wait --host host2 --game --force`
  (the game closed, 1 file identical after about 2 s, game restarted, `16/16` identical).

## Raid 1 (15:18, package as reviewed, defaults 60 s / 10 s)

`gpu-run raid --host host2 --mode tactical --map factory --expect-clean ArenaMode`: the
mode row's description read **`Eliminate the squad or hold the point. Endless rounds; death /
timeout loses.`** (the new copy for a map with a layout), `log since start: warnings 0;
errors 0`, ArenaMode clean, location 15.5 s, game started 31.1 s.

- 15:18:55 `Tactical objective: capture point after 60s of combat, hold 10s (both read again
  at every round start); bot objective layer available.`
- 15:19:10 `Arena round door reset: the world's interactive objects are unavailable: Object
  reference not set to an instance of an object` at the round 1 start, and again at every
  round start: `GameWorld.WorldInteractiveObjects()` reads the `World` component, which the
  local game world does not carry. **Finding 1**, fixed in the second delivery.
- 15:19:28 `Tactical round 1 live: 5 active opponents` and `Tactical round 1 objective: point
  at (-6.8, 0.0, 10.0), 4 opening lane(s), bots on the objective layer.` (one of five lanes
  had no NavMesh within 4 m and was dropped, as designed). `uibridge_bots` 12 s later: all
  five bots on the BigBrain layer **`Arena Combat Domination`**, four moving, spread
  between (-4.7, 36.9) and (27.6, 35.9).
- 15:20:28 `the capture point is up` (60 s after the live line). `uibridge_bots`: four bots
  inside the 4 m ring at (-6.4 … -8.6, 9.3 … 11.1), not moving; the HUD status line read
  **`01:48 Round 1 0-0 250,000 ₽ 5 left POINT enemy 79%`**.
- 15:20:45 `Tactical round 1: the capture point was held for 10s by the enemy squad.`,
  `Tactical round 1 lost — 0-1`. The enemy capture ends a round.
- Round 2 (15:21:16, point at (-32.1, -2.7, 30.0), 5 lanes): the player was teleported onto
  the point at activation; HUD **`POINT contested`** with the squad on the point too; the
  player then died (`Arena death #1`, 15:22:35: the godmode of round 1 does not survive the
  body swap of a round start) and the round was lost by the death, as before the package.
- Rounds 3 and 4 ran with the test values `ObjectiveAfterSeconds 15` and
  `ObjectiveHoldSeconds 3`, set through `uibridge_config_set persist=false` (the .cfg kept
  60 / 10; the mode read the new values at the next round start, as the per-round reading
  intends). Round 3: the enemy squad held the point 3 s after activation. **Round 4**
  (15:23:56, point at (-32.1, -2.7, 30.0)): the player stood on the point before activation,
  alone; 15:24:13 `Tactical round 4: the capture point was held for 3s by the player side.`,
  `Tactical round 4 won — 1-3, 244,000 roubles.`, announcer clip `round_won`. The friendly
  capture ends a round as a win.
- Round 5 and 6 lost by the enemy capture; `uibridge_log level=warning source=ArenaMode` over
  the whole raid: **0 lines**. The server logged `/arenamode/rewards/begin` at 15:18:55 and no
  refusal.
- Left with `gpu-run leave` (the first call died with an unhandled Node error in gpu-run, the
  second call left the raid normally: DISCONNECT, the result screens, MainMenu).

## Raid 2 (15:28, door reset through `LocationScene.GetAllObjects<Door>`)

Six rounds (0-6, test values 15 s / 3 s). No door reset line at any round start and no
error either: `LocationScene.GetObjects<T>` answers from a dictionary keyed by the exact
registered type (`WorldInteractiveObject[]`), so `GetAllObjects<Door>` returned nothing and
the reset shut nothing. Read with `uibridge_inspect`: the doors at (8.8, 2.1, 34.0) and
(32.2, 2.1, 34.0) had `InitialDoorState Shut` and stood `Open` across the round 5 and 6
starts. **Finding 2**, fixed in the third delivery (enumerate `WorldInteractiveObject`,
keep the `Door`s, log the result of every round start). Marker photo at round 4: the
player 6 m south of the point looked at a brick wall with the ring's near arc on the floor
(HUD `03:31 Round 4 0-3 490,000 ₽ 5 left POINT free`); the beam and label stood behind the
wall, so the picture shows the ring only.

## Raid 3 (15:37, committed state)

- 15:38:20 `Tactical round 1: door reset saw 77 door(s), shut 0, skipped 22.` (the 22 are the
  doors the map starts open and the locked ones).
- 15:38:39 round 1 live, point at (4.7, 0.0, -35.1), 5 lanes; 15:38:54 up; 15:39:07 held 3 s
  by the enemy squad, round lost.
- **15:39:28 `Tactical round 2: door reset saw 77 door(s), shut 1, skipped 23.`**: a door a
  bot opened in round 1 was shut at the round 2 start; the one the map starts open stayed
  open. The door reset works.
- `uibridge_command BeginInteracting` during the round 2 staging was swallowed by
  `com.maschine.ArenaMode.inputblock` (the staging block, as designed), so the player's own
  door opening was not needed; the bots' door served.
- Left with `gpu-run leave` (normal). No warnings or errors from ArenaMode in any of the
  three raids.

## After the commit

The package was committed as `e1dfc344` and client and server were rebuilt from it and rolled
out at 15:47 (`0.5.0+e1dfc344` on `D:\SPT41`, `gpu-run deploy-wait --host host2 --server --game
--force`: 3 files identical after 34 s, server restarted, game restarted, `16/16` identical), so
the installed product version names the commit that holds the code of the third raid.

## PitFireTeam team arena (18:34 to 18:47, committed state `0.5.0+e1dfc344`)

Two matches `gpu-run raid --host host2 --mode tactical --map factory --squad 2 --expect-clean
ArenaMode` with test1 and test2 (PitFireTeam 1.1.1, roster test1 to test4), first to three rounds,
3 versus 5, on the build the commit `e1dfc344` produced (`16/16` plugins and `2/2` libraries
identical between `D:\SPT41`, the host's disk and the process; Core `1.0.21+bebf49b0`). The mode
row read **`First to 3 round wins; hold the point. On death, spectate...`** both times (the team
copy for a map with a layout), ArenaMode clean at both starts, zero ArenaMode warnings over the
first match and one over the second: the known pursuit diagnostic `Team Arena native awareness
rejected ... native-goal-setter-refused` ([log audit](../2026-10/LOG-SOURCE-AUDIT-20261018.md)).

### Match 1 (18:35 to 18:40, lost 2-3; defaults 60 s / 10 s, then 20 s / 5 s)

- 18:35:04 `Tactical objective: capture point after 60s of combat, hold 10s (both read again at
  every round start); bot objective layer available.`, `Tactical team Arena: first to 3; waiting
  for 2 PitFireTeam companions`.
- Round 1 live 18:35:19 (`3 allies versus five opponents`), `door reset saw 77 door(s), shut 0,
  skipped 22`, point at (-32.1, -2.7, 30.0), 5 lanes. `uibridge_bots` 12 s later: test1 and test2
  on **`pitTeam.FollowerPatrol`** beside the player, all five opponents on **`Arena Combat
  Domination`** spread along the lanes. Point up 18:36:19; the five ran from the lanes to the
  point; 18:36:37 `the capture point was held for 10s by the enemy squad`, `round 1: defeat — ...;
  score 0-1, continuation RebindNextRound`, toast `Round 1 — the enemy held the point`, HUD `01:32
  3v5 Round 1 0-1 DEFEAT next round in 5s`.
- Round 2 live 18:36:55, `door reset saw 77 door(s), shut 2, skipped 22` (two doors the squads
  opened in round 1), point at (-6.8, 0.0, 10.0); the BEAR squad stood on four lane points in a
  line across the midpoint ((21.9, 10.3), (18.9, 19.9), (23.6, -0.4), (26.0, -8.5)), not moving,
  until the point came up at 18:37:55; 18:38:10 held 10 s by the enemy squad, 0-2.
- `uibridge_config_set persist=false` at 18:37: `ObjectiveAfterSeconds 20`, `ObjectiveHoldSeconds
  5` (the .cfg kept 60 / 10; read at the next round start). Round 3 live 18:38:23 (`shut 1`),
  point at (-32.1, -2.7, 30.0); the player was teleported onto the point with godmode (the
  teleport's own reachability check refused the path once, the doors just shut lie on it;
  `reachable=false`); the point came up at about 18:38:43; **18:38:51 `the capture point was held
  for 5s by the player side`, `round 3: victory — ...; score 1-2`**. The two followers respawned
  for round 4 at the player's position, on the point.
- Round 4 live 18:39:03 (`shut 0`), point at (35.6, 4.5, 35.7), up 18:39:23; the player teleported
  onto it (no godmode on the new body), **18:39:43 held 5 s by the player side, 2-2**; HUD at the
  round 5 start `05:20 3v5 Round 5 2-2 Allies 3/3 Enemies 5/5 POINT free`, team points `600:0`.
- Round 5 live 18:39:56 (`shut 2`), point at (-20.8, 0.0, 22.8), up 18:40:16; 18:40:34 held 5 s by
  the enemy squad, **2-3, `continuation EndMatchLost`**; 18:40:40 `Tactical team Arena ended 2-3
  after 5 round(s) with native status MissingInAction`, follower spawn lease 4 revoked, `Tactical
  ended: 2-3 over 5 round(s), 0 roubles left`. Server: `/arenamode/rewards/begin` at 18:35:04
  without a refusal (the team mode with the point declares `KillsOnly`), `/arenamode/rewards/capture`
  and `/arenamode/raid-end` at 18:40:41, `Protected Arena end completed ...; rewards are verified
  separately`, the profile snapshot restored, PitFireTeam's post-raid services suppressed as
  before. `gpu-run menu`: ExitStatus → KillList → SessionStatistics → MainMenu in 15 s.

Observation, round 5: from 18:40:17 to 18:40:25 three opponents stood 2.2 to 2.9 m from the
point's centre on their lane points ((-20.8, 0.3, 20.6), (-20.7, 0.0, 19.9), (-21.4, 0.0, 20.2)),
not moving, while the HUD read `POINT free`; the enemy hold began at about 18:40:29 and completed
at 18:40:34. Occupancy needs a clear line from the centre to the body (the same check as
Domination's `DominationRuntime`), so a lane that ends inside the ring but behind cover does not
count until a bot steps around it. Consistent with the contract; noted, not changed.

### Match 2 (18:42 to 18:46, lost 0-3; the test values 20 s / 5 s still in memory)

Started 1 min 50 s after the first match's raid-end, without the PitFireTeam settling refusal.

- Round 1 live 18:42:49 (`shut 0`), point at (35.6, 4.5, 35.7) near the player spawn, up 18:43:09;
  the squads fought around it (the player lost a leg and head HP without godmode), 18:44:38 held
  5 s by the enemy squad, 0-1.
- **Round 2, the PMC down:** live 18:44:54, `uibridge_hook action=kill` at 18:44:59: `Arena death
  #1 — held for protected team transition`, `Arena kept the PitFireTeam squad active after the
  local player was eliminated`, `Team Arena post-death pursuit active for round epoch 2: allies 2,
  opponents 5`, spectator on test2. `uibridge_bots` right after: **all seven bots on `Arena Combat
  Pursuit`**, none left on the objective layer; the HUD read `02:24 3v5 Round 2 0-1 Allies 2/3
  Enemies 5/5 YOU ARE DOWN POINT in 11s`, so the point's clock kept running for the followers; the
  point came up at 18:45:14 while the pursuit fought; 18:45:50 `round 2: defeat — the complete
  player team was eliminated; score 0-2` (elimination decided; nobody held the point).
- Round 3 live 18:46:00 (`shut 0, skipped 24`), point at (4.7, 0.0, -35.1), up 18:46:20; 18:46:29
  held 5 s by the enemy squad, **0-3, EndMatchLost**, `Tactical ended: 0-3 over 3 round(s)`; server
  begin at 18:42:39 and capture at 18:46:36 as in the first match. `gpu-run menu` back to the
  main menu; EFT left there.

## Not covered

- Bay 5, Air Pit, Bowl (the non-Factory placement path with the team-box anchor).
- A point refused by the floor or path checks (Warning, round by elimination).
- The reward settlement of a KillsOnly Tactical run at the next normal server start (all three
  begins were requested and not refused, both team captures posted; no kill was made in any match).
- A follower capture without the PMC on the point (the followers stood on the point only beside
  the player), and a round the followers win while the PMC is down.
- The beam and label in a picture (the ring was seen; the camera stood behind a wall).
