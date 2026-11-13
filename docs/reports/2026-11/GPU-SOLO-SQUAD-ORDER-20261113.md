# Solo Tactical squad dispatch order: native runs on GPU-2 (2026-11-13)

Solo Tactical on Chop Shop and Equator with the dispatch-order fix of branch `fix/solo-squad-dispatch-order`
([contract](../../reviews/SOLO-TACTICAL-SPAWN-REVIEW.md), "Dispatch order"), on SPT-GPU-2 (`192.168.178.109`,
`C:\SPT41`, profile `SPT-GPU-2`), driven from the VM with gpu-run, UiBridge 0.9.9 and its MCP proxy. Two cycles:
run A with the first build of the fix, run B with the head of the branch after it was rebased onto
`feature/spawn-rows` `7d47f03b`. Dated evidence, not proof about any other build.

## What was to be shown

The squad's points are picked after `BotCreationData.Create`, at least 25 m from wherever the player stands at
that moment. Before the fix the request went out before the round's body move; now it has to go out after the
move has landed. Natively that is visible in two log lines per round start: the release
("`Tactical round N: requesting the squad, the body move landed (… after the round start)`") and the plan
("`Arena burst spawn plan: … selected 5 from the player at (x, y, z).`", the position the points were measured
from, added to that Debug line by this branch). The race itself (profiles ready inside the request) did not occur
in these runs either: the profiles took 2.4 to 21.8 s. The guarantee for the lost race rests on the offline
checks of the contract; these runs show that the order holds in the game, on both paths, and that the plan is
made from the row the player landed on.

## Build and delivery

- The shared Redeploy checkout `D:\SPT41\Development\Redeploy` carried another session's uncommitted Core 1.0.23
  work (`CoreVersion.props` bumped). A client built against it is stamped `RedeployCoreBuiltAgainst = 1.0.23` and
  refuses the installed Core 1.0.22. So the builds ran in `D:\SPT41\Development\_squadorder-build\`: an ArenaMode
  worktree and a Redeploy worktree at the installed Core's commit `b1d69e1`, side by side so that the client's
  `..\..\Redeploy` reference resolves there. `dotnet build` of `ArenaMode.Client` and `ArenaMode.BigBrainCompat`
  (Release, `-p:SPTPath=D:\SPT41`, `tools/AirPitChecks/NoDeploy.targets`, a non-existent `SPTUserMods`); the
  live install hashed before and after each build: unchanged. Stamp checked: `RedeployCoreBuiltAgainst = 1.0.22`.
- Run A: client and BigBrainCompat **`0.5.0+73370e37`** (the spawn-row branch at `ab16c8f3` plus the fix;
  client SHA-256 `99b56e9d…`), installed at 10:38 over `0.5.0+e90d1f72` (previous files
  `D:\SPT41-Backups\solo-squad-order\20261113-104013`); `gpu-run deploy-wait --host host2 --game`: identical
  after 2.1 s, game restarted, `16/16` maschine plugins and `2/2` libraries identical (VM build = host disk =
  loaded), startup line `maschine-ArenaMode 0.5.0 loaded (Redeploy.Core 1.0.22.0; build/MVID 8cd7843d-…)`.
- Run B: the branch was rebased onto `7d47f03b`, which had merged `master`'s extraction hardening; the fix's
  files (`TacticalMode.cs`, `SoloTacticalSquadDispatch.cs`, `BurstSpawnService.cs`) are identical in both builds.
  Client and BigBrainCompat **`0.5.0+ba06ac49`** (client SHA-256 `fc3a45db…`, BigBrainCompat `7790b1b0…`)
  installed at 12:14 (previous files `D:\SPT41-Backups\solo-squad-order\20261113-121431`); `deploy-wait`
  (identical after 10.1 s) and `gpu-run start --host host2` (main menu after 85 s, `16/16` identical, no
  ArenaMode warning at startup). Server stays `0.5.0+e90d1f72`: server and shared sources differ from it only in
  comments. Core `1.0.22+b1d69e1` and ArenaMapPort `0.5.0+160523d` unchanged.
- GPU-1 had paused the `SPT41` Syncthing folder on its side for a Codex test (Syncthing `remoteState: paused`)
  and received neither build.

## Settings and scenario

`Tactical/BuySeconds 5` and `Tactical/RoundSeconds 60` were set with `uibridge_config_set persist=false` before
each cycle (the `.cfg` kept 15 and 240; UiBridge guards every later save of the file). Scenario:
`tools/ChopShopChecks/scenarios/tactical-solo-ported.json` with one added step, a `bots` read (`aliveOnly`,
`minCount 5`, `onFail continue`) right after `player-round3`, because the file reads the opponents only in rounds
1 and 2. Run as `gpu-run scenario <copy> --host host2`, then with `--param map=EQUATOR`. All four runs: 21 steps
ok, 10 expectations ok (five opponents live in each round, `no usable spawn point` and `no partial squad
dispatched` absent, no error), cleanup leave ok.

## Results

The release time is from the round start to the release line; "plan from" is the position in the plan line,
with the Player row of `base.json` it equals exactly; "closest live" is the nearest opponent in the `bots` read
taken the moment the round went live (the bots had been active and moving for about a second). Round 1 is
always a survivor (the raid-start position, then the round-break swap); a round after the player's death is the
held respawn, released by `OnLocalRespawn`.

| Run | Map | Round | Move | Released after | Plan from (Player row) | Points | Plan after release | Closest live |
|---|---|---|---|---|---|---|---|---|
| A | Chop Shop | 1 | swap | 0.094 s | (-409.50, -3.72, 495.57) west-all | 11 preferred | 21.8 s | 22.2 m |
| A | Chop Shop | 2 | swap | 0.062 s | (-337.28, -3.80, 502.69) usec | 13 preferred | 9.1 s | 29.1 m |
| A | Chop Shop | 3 | held respawn | 0.065 s | (-409.31, -3.74, 487.04) bear | 11 preferred | 5.3 s | 27.4 m |
| A | Equator | 1 | swap | 0.167 s | (-4.35, 0.04, -39.09) south-all | 9 + 5 distant | 16.1 s | 28.2 m |
| A | Equator | 2 | held respawn | 0.071 s | (-4.90, 0.08, 45.44) all | 10 + 2 distant | 9.7 s | 29.4 m |
| A | Equator | 3 | held respawn | 0.067 s | (4.97, 0.07, -39.09) bear | 9 + 5 distant | 2.7 s | 27.3 m |
| B | Chop Shop | 1 | swap | 0.093 s | (-409.31, -3.74, 487.04) bear | 11 preferred | 17.3 s | 24.5 m |
| B | Chop Shop | 2 | swap | 0.064 s | (-338.45, -3.80, 490.47) all | 12 preferred | 5.8 s | 26.9 m |
| B | Chop Shop | 3 | held respawn | 0.071 s | (-409.50, -3.72, 495.57) west-all | 11 preferred | 6.7 s | 22.7 m |
| B | Equator | 1 | swap | 0.092 s | (-4.35, 0.04, -39.09) south-all | 9 + 5 distant | 14.0 s | 28.4 m |
| B | Equator | 2 | swap | 0.058 s | (6.52, 0.08, 48.59) usec | 10 + 3 distant | 6.6 s | 24.0 m |
| B | Equator | 3 | held respawn | 0.070 s | (4.97, 0.07, -39.09) bear | 9 + 5 distant | 2.4 s | 30.6 m |

- **Order:** in all twelve round starts the request went out after the move had landed, on both paths: after
  "`Arena round respawn complete`" for the swap (same millisecond or 2 ms later), after the respawn event for a
  player who died. No release by the cap, no "did not land", no "no body move" line.
- **Plan position:** every plan was measured from the Player row the player had just landed on (70.9 to 88.4 m
  from where the round found them), never from the spot they left. Where the old spot can tell the two apart, the
  squad used rows that a plan from the old spot would have excluded: Chop Shop run A round 2 used
  `bot-lasthero-10` (10.7 m from the old spot), round 3 `bot-core-07` (17.4 m), run B round 2 `bot-fill-03`,
  `bot-lasthero-10` and `bot-fill-01` (13.7, 15.4 and 16.1 m), run B round 3 `bot-core-07` (18.3 m). (The row of
  a bot is the nearest Bot row to where it stood at the read, 0.3 to 8.7 m away.)
- **Distances below 25 m at the live read** (22.2, 22.7, 24.0 and 24.5 m): those bots were moving at the read
  (BigBrain's Domination layer). For two of them the nearest Bot row (`bot-fill-04` 18.6 m, `bot-fill-03` 17.8 m
  from the plan position) cannot be where they spawned, because the production selector returns no row under
  25 m from the plan position; they had walked in from a farther row. The other two stood 1.9 and 3.1 m from rows
  26.4 and 26.1 m from the plan position. The contract names this limit: the guarantee is the 25 m floor at
  planning time.
- Run B round 2 on Chop Shop ended by a test-hook death 0.7 s after it went live, as the scenario intends; round 1
  of run A on Equator ended by the player's death (the bots won), so its round 2 is a held respawn.

Log excerpt, run B, Chop Shop round 3 (held respawn after the test-hook death in round 2):

```text
12:18:57.136 Arena death #1 — respawn after optional killcam.
12:19:08.141 Tactical round 3: kit 'sovereign' for 396,000, 14,000 left; the squad is requested once the held respawn has landed.
12:19:08.225 Tactical round 3: requesting the squad, the body move landed (0.071s after the round start).
12:19:08.227 Tactical round 3: requested 5 opponents; spawn service planned 5.
12:19:14.920 Arena burst spawn plan: zone BotZone_Main, requested 5, profiles 5, points 11 preferred + 0 distant, selected 5 from the player at (-409.50, -3.72, 495.57).
12:19:15.819 Tactical round 3 live: 5 active opponents, 0 already eliminated.
```

## Logs

Warnings of both cycles besides Unity's "referenced script is missing" and LODGroup lines: the expected
`Tactical objective: the point appears after 60s and needs a 10s hold, but the round timer ends after 60s` (once
per raid that set the point up; a consequence of the test's 60 s rounds) and one ArenaMapPort physics line
(`impact sound skipped for SoundType=Books`). No error. The game process of run A ended at 11:38 with the normal
patch teardown, after both raids and outside this session.

## Acceptance

- **Run B (complete):** before the first raid the test profile, `com.maschine.ArenaMode.cfg`,
  `xyz.pit.fireteam.cfg` and the 61 files under `SPT_Runtime\user\mods\pitFireTeam-ServerMod` were copied and
  hashed. Afterwards: the profile differs in `characters/pmc/Health/UpdateTime` and
  `characters/pmc/Hideout/sptUpdateLastRunTimestamp` only; the PitFireTeam files and config are byte-identical;
  `com.maschine.ArenaMode.cfg` differed in one line, `Match/GameMode` `teamdeathmatch` → `tactical` (the mode the
  scenario's menu drive selects), set back through the running game and then byte-identical to the copy on the VM
  and on GPU-2 (SHA-256 `97d2855e…`); the two `persist=false` values were set back to the `.cfg` values in the
  running game. Loaded Arena plugins equal the deployed files (UiBridge MVID check, `16/16`).
- **Run A (partial):** no copy was taken before it. Against SPT's automatic backup from before the raid
  (`profiles/backups/2026-11-13_09-12-39`, folder names in UTC) the profile differs in the same two timestamps
  only, and no PitFireTeam file changed after 10:40. `com.maschine.ArenaMode.cfg` was written at 10:42:36 by the
  menu drive; its previous content is unknown, and at 12:04 another process on the VM wrote the file again
  (Syncthing `modifiedBy` SPT-VM), so for run A the configuration criterion is not proven.

## Installed afterwards

Client and BigBrainCompat `0.5.0+ba06ac49`, server `0.5.0+e90d1f72`, Core `1.0.22+b1d69e1` (see
[HANDOFF.md](../../HANDOFF.md)). The build worktrees stay at `D:\SPT41\Development\_squadorder-build\` (ArenaMode at
`ba06ac49`, Redeploy at `b1d69e1`) until the branch is merged.
