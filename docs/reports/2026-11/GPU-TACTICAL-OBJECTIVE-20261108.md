# Tactical capture point: native runs on GPU-2 (2026-11-08)

Three solo Tactical raids on Factory with the development build of the Tactical objective
package ([review](../../reviews/TACTICAL-OBJECTIVE-20261108.md)) on SPT-GPU-2
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

## Not covered

- The PitFireTeam team arena (companions, the squad leaving the point when the PMC is down):
  no companions were invited on GPU-2 in this cycle.
- Bay 5, Air Pit, Bowl (the non-Factory placement path with the team-box anchor).
- A point refused by the floor or path checks (Warning, round by elimination).
- The reward settlement of a KillsOnly Tactical run at the next normal server start (the
  begin was requested and not refused; no kill was made).
- The beam and label in a picture (the ring was seen; the camera stood behind a wall).
