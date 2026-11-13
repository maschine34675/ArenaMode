# Spawn-row follow-up, native runs - 2026-11-13

Host: SPT-GPU-2 (`192.168.178.109`, `C:\SPT41`), test profile `SPT-GPU-2`
(`6ab7db1c...`), 1920x1080, SPT 4.1.6, EFT 0.16.9.5. Driven from this machine
through UiBridge `0.9.9+92b998a` and gpu-run of UiBridge `main`. Branch under
test: `feature/spawn-rows`. Contract and offline checks:
[SPAWN-ROWS-FOLLOWUP-20261113.md](../../reviews/SPAWN-ROWS-FOLLOWUP-20261113.md).
Raw evidence (scenario reports, stills, log exports, one menu screenshot, two
bridge-log extracts) is under the ignored `artifacts/spawn-rows-20261113/gpu/`.
All times are host local time (UTC+01:00).

Installed beside the builds and unchanged by this work: ArenaMapPort
`0.5.0+160523d` (the unreleased build with the spawn rows and pocket exits of
2026-11-12; the published 0.5.0 carries the same version string and has neither
the rows nor Chop Shop), Redeploy.Core `1.0.22+b1d69e1` (hash compared before and
after the build), PitFireTeam `1.1.1`.

## Builds

| Build | Commit | Client SHA-256 | Used for |
|---|---|---|---|
| A | `3ea7eabb` (master as installed on 2026-11-12, no change of this work) | `7f3acef7580a886c35a85d157d1743fdbecf0f1e8414550e03f204ed771acc79` | Chop Shop Domination, Zombies from the yard centre, Team DM with the pocket exits |
| B | `e90d1f72` (the Tactical lifts, hints, default 8) | `ec25657dc00298a68a09b3560c15467fb5f4cf0f067941872a3a59462a08d30c` | menu with a squad, solo Tactical on three maps, PitFireTeam Tactical on two, the dispatch-order measurement |

| Build | BigBrainCompat SHA-256 | Server SHA-256 |
|---|---|---|
| A | `fb281181c5a111d81ba8c7bb72d9543ca88c076e980858978c95090a3e1d2e43` | `187440bec0d680884757ae4842334965ef79827c4b04c2e43542363a16a69f56` |
| B | `42df33ac782cfd23c0f4b90cc6b3c4c7df026915679fe7cb080a4bbf195e3fb9` | `e8a4414279c1dcc963ad7cf47172e605a7655552f74de53cb0096d0eacdf6385` |

Build B was built with `tools/AirPitChecks/NoDeploy.targets` in a separate clean
build worktree, the three stamps were checked, the DLLs were copied into
`D:\SPT41` and rolled out with `gpu-run deploy-wait --host host2 --server --game`
(VM build = host disk = loaded process). The files of build A were kept in
`D:\SPT41-Backups\arena-spawn-rows\20261113-093617`. The hashes of build B are the
ones printed when it was installed; its files are no longer installed. The
commits after `e90d1f72` up to `bf8072d6` change comments, harnesses and documents
only. The merge of `master` that followed (`8d4797dd`, extraction hardening) adds
production code that was in neither build. This work installed nothing after
build B: at 10:38 the session working on `fix/solo-squad-dispatch-order` installed
its own test build over it (client and BigBrainCompat `0.5.0+73370e37`, which
contains this branch up to `ab16c8f3`) and restarted the game on the host at
10:41. What is installed is recorded in [HANDOFF.md](../../HANDOFF.md), not here.

## Results

| Run (build) | Result |
|---|---|
| Chop Shop Domination 4v4 (A) | **Passed** (14 steps). `Domination points verified: X=(-369.290, -3.805, 499.490)`, gates open after 5.00 s, `Arena pickup placement attempt 1: ... placed=6/6; alternatives=7; ... refusals=[another pickup is within 3m=1]` with 16 candidates per pickup, Health at the donor row (-367.44, -3.805, 492.11). On 2026-11-12 the same code placed 5 of 6 (five usable rows for six pickup kinds, Health left out). The limit is gone with MapPort's rows; no code changed. |
| Chop Shop solo Zombies, player in the yard centre from the start (A) | **Passed** (`chopshop-zombie`, 15 steps). Player at (-379, -3.8, 495) under godmode; every plan line reads `points 5 preferred + 0 distant, selected 1`; 32 plans, 32/32 infected alive after 35 s; after the move to the east pocket the 32 were still alive (no new fill was needed there) and there was no `no usable spawn point` line. On 2026-11-12 this spot had no row beyond 25 m and every request was dropped (133 in 30 s before the backoff, 18 in 36 s with it). |
| Chop Shop Team DM 4v4 with the exits in the pockets (A) | **Passed** (`chopshop-tdm`, 21 steps). `restored 20 box markers`, capacity 10/10 on both sides, gates open after 5.01 s, pickups 6/6 from 16 candidates. Local BEAR first spawn on west-08, 0.4 m outside the West Gate's exit box (a 4 x 3 x 4 m box at (-406.5, -3.7, 478.5)), respawn on west-05 (-409.40, -3.74, 477.39), 0.9 m outside it. The player was alive and in the raid after both, and the two stills show no extraction prompt. No scenario step asserts that, and the player never stood inside the box. |
| Menu with a PitFireTeam squad of two, Tactical selected (B) | The offered map tiles are ICEBERG, BOWL, EQUATOR, AIR PIT and BAY 5; there is no CHOP SHOP tile (`pft/menu-pft-tactical-no-chopshop.png`, mode shown as `3v5 PFT`; the two companions were set with `gpu-run squad`). Solo, the scenario runs reached all three maps through their Tactical tiles. |
| Solo Tactical, Chop Shop (B) | **Passed** twice: the first scenario version (23 steps) and the committed `tactical-solo-ported` (20 steps, 10 expectations). Every round `live: 5 active opponents`; plans 11, 13, 11 and 13, 11, 12 preferred rows; `objective: point at (-369.3, -3.8, 499.5), 5 opening lane(s), bots on the objective layer`. Nearest opponents 27.3 m (round 1) and 33.3 m (round 2). |
| Solo Tactical, Equator (B) | **Passed** with `tactical-solo-ported` (20 steps). Plans `9 preferred + 5 distant`, `10 + 2`, `9 + 5`; objective at (1.3, 0.3, 4.2), 5 lanes; nearest opponents 28.1 m and 31 m. The first scenario version failed at its own step 9 on this map (it waited for the round timer, but the bots killed the player in round 1; godmode had been set before the round-1 body swap and most likely did not carry over to the new body); the log of that run shows three rounds live with five opponents as well. The scenario was reworked after that. |
| Solo Tactical, Iceberg (B) | **Passed** with `tactical-solo-ported` (20 steps). Plans `5 preferred + 12 distant`, `9 + 6`, `6 + 11`; the objective changed between rounds (C, then B). |
| PitFireTeam Tactical, Iceberg, leader plus two companions (B) | Three rounds went live: `Tactical team Arena round N live: 3 allies versus five opponents`. Rounds 1 and 2 from the Player row (`6 preferred + 11 distant`). For round 3 the surviving leader was teleported into the old centre rectangle (teleport point (10.6, 3.1, -70.0); he stood at (9.9, 3.2, -69.7) when the round went live), the area that had four distant rows before: `points 11 preferred + 0 distant, selected 5`, round live with five. No no-contest. |
| PitFireTeam Tactical, Equator, leader plus two companions (B) | Round 1 `13 preferred + 0 distant`, live with three allies versus five. For round 2 the surviving leader stood at the worst spot of the map (-4, 0.1, 15): `points 6 preferred + 0 distant, selected 5`, followed by the round-2 start. The offline grid minimum for Equator is the same six. |

Errors in the BepInEx log during these raids: none. One expected warning in
every Tactical raid: the test timing (`RoundSeconds` 60 with the capture point
after 60 s) makes a hold impossible, and Arena says so
(`Tactical objective: the point appears after 60s ... no hold can complete`).

## Solo squad dispatch order

The review asked whether the squad's rows are picked against the player's
position before or after the round-break move (the squad is requested first in
the code). Per round start the log gives the request
(`Tactical round N: requested 5 opponents`), the body change
(`Arena round respawn complete`, or Killcam's `main player changed` for a
respawn after death) and the pick (`Arena burst spawn plan`).

| Raid (build B) | Round | Body change after the request | Plan after the request |
|---|---|---|---|
| Iceberg | 1 | 0.28 s | 10.6 s |
| Iceberg | 2 | 0.08 s | 7.7 s |
| Iceberg | 3 (after death) | 0.09 s | 7.6 s |
| Equator | 1 | 0.28 s | 9.8 s |
| Equator | 2 | 0.08 s | 4.8 s |
| Equator | 3 (after death) | 0.07 s | 1.2 s |
| Chop Shop | 1 | 0.26 s | 7.8 s |
| Chop Shop | 2 | 0.07 s | 7.0 s |
| Chop Shop | 3 (after death) | 0.06 s | 1.9 s |
| Chop Shop, measurement raid | 3 (survivor) | 0.08 s | 1.8 s |

In all ten the body had moved before the rows were picked. The measurement raid
shows it by the count as well: the survivor stood in the worst cell of the yard
(-375, -3.8, 486.5), which has three usable rows, when round 2 ran out; the plan
for round 3 read `points 13 preferred + 0 distant`, the count from the east
Player row he was moved to. One second after that squad went live its members
stood 22.3, 23.1, 24.3, 43.8 and 57.0 m from him (they had started to move); in
round 2 of the same raid, after a death, 27.4, 47.1, 52.8, 54.3 and 57.9 m. These
distances were read live with `uibridge_bots`; the measurement raid has no export
of its own in the evidence folder (its log lines are at the start of the export
of `tactical-solo-equator-final`).

This is a measurement, not a guarantee: nothing in the code orders the pick
after the move, and profiles that are ready within less than 0.1 s would be
planned against the old position. The contract records it as a known limit of
solo Tactical on every map, and the reorder is filed as a separate task.

## Observations

- The respawn view in the Chop Shop pockets is partly covered by the green
  smoke of ArenaMapPort's exit flare, which now stands in the pocket
  (`tdm-4v4-chopshop/tdm-after-respawn.png`). It is MapPort's effect at MapPort's
  position; nothing in ArenaMode draws or moves it.
- A participant count that was saved per map is not migrated: on this profile
  `Map participants/arena_autoservice_solo` stays 6 while the description now
  says `Default value: 8`. Only a fresh configuration gets eight.
- In the committed scenario a survivor of round 1 on Chop Shop was placed on the
  Player row he already stood on (-409.5, -3.7, 495.5 before, 495.6 after); on
  Equator he moved 12 m to the other row of the same pocket; in the measurement
  raid he was moved 41 m. All three are Player rows.

## Clean-up

- Test profile compared leaf by leaf with the copy taken before build B
  (5,400 leaves): two differences, `characters/pmc/Health/UpdateTime` and
  `characters/pmc/Hideout/sptUpdateLastRunTimestamp`. The two other profiles and
  the 45 PitFireTeam server files are hash-identical.
- `Match/GameMode` was put back to `teamdeathmatch` through the bridge. The test
  timings (`Tactical/BuySeconds` 5, `Tactical/RoundSeconds` 60) were set with
  `persist=false` and never reached the cfg file. Against the copy taken in the
  morning the cfg differs only in three description lines the new build wrote.
- The PitFireTeam squad group was cleared after the squad runs (0 of 4).
- The comparison copies (profile, cfg, hash list of the PitFireTeam files) and the
  logs of the offline verification are under the ignored
  `artifacts/spawn-rows-20261113/verify/`.
- The game on the host was left running in the menu. It had not been running
  before this work; the process that runs now was started at 10:41 by the other
  session's deploy.

## Not run

- PitFireTeam Tactical to the end of a match, a won round, or a capture-point
  hold on the three maps (the test timing excludes a hold).
- A lost dispatch race (profiles ready before the move); it did not occur.
- Solo Tactical from the three-row cell of Chop Shop with the plan taken there
  (the survivor was moved before the plan, see above).
- Team DM on Bay 5, Air Pit, Bowl and Iceberg with the new exits; ArenaMapPort's
  own record has two of them.
- Anything on the published ArenaMapPort 0.5.0 data.
