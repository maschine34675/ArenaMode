# Zombie alive ceiling: native measurement on SPT-GPU-2 (2026-11-19)

Record of the two measurement passes that decided whether the Zombie alive limit may grow above the default of 32
(stage Z2 of [ZOMBIE-ESCALATION-20261119.md](../../reviews/ZOMBIE-ESCALATION-20261119.md)). Evidence for the build and
the settings named below, not proof about any later build.

**Decision of the orchestrator, 2026-11-19: the ceiling stays 32, which is today's default. The alive-limit growth is not
built.** 40 and 48 miss both thresholds; 36 holds in three runs and misses in two repeats, so it is not a ceiling; even 32
has little margin under heavy fire. Whether a softer criterion or another escalation after wave 11 is wanted is left to the
owner and is not designed here.

## Build, host and settings

| Item | Value |
|---|---|
| Host | SPT-GPU-2 (Ryzen 7 9800X3D, RTX 4070 Ti, as in the earlier Zombie measurements), idle before each pass: no EFT, no server, no other session |
| ArenaMode client and server | `0.6.0+72273354` (hashes of both passes equal) |
| Redeploy.Core | `1.0.24+67f8303` |
| UiBridge plugin | `0.10.1+8f58ec77` |
| ModProfiler | `1.4.0+d178d55e` |
| ArenaMapPort | `0.6.0+322e3a2d` |
| Plugins | all 16 maschine plugins loaded, host disk = VM |
| Installed settings | `Difficulty/Step` Maximum, `ShooterPercent` 20 |
| Set per run with `uibridge_config_set persist=false` | `WaveStartSize` 200, `WaveGrowth` 0, `OwnGear` false (Arena kits), `MaxAlive` N before each raid; from the second Factory-32 Dauerlast on also `StuckWaveTimeoutSeconds` 0 (installed 100) and ModProfiler headless |
| Player | godmode and infinite ammunition (condition kept), a fresh raid per cell |
| Cap line | `Arena bot cap set to N+2` in every raid (34, 42, 50, 58 for N = 32, 40, 48, 56) |

## Protocols and thresholds (fixed before the measurement)

- **Dauerlast (primary):** the player stands where the route anchor put him and fires at the nearest visible infected;
  150 s of frame times in 30 s windows (Air Pit 4 complete windows).
- **Harness route (secondary):** the walking route with relocations every 55 s, 240 s.
- Primary threshold: Dauerlast p95 at most **16.67 ms**. Secondary: harness-route p95(N) at most
  `max(16.67 ms, p95(32, same session) + 1.5 ms)`. A candidate passes only if it holds both.
- Frame times came from the UiBridge ring through the operator's stand-in collector (`bench.mjs`), not from the
  repository's `run.mjs`, which does not run on 0.10.1. See "How the frame times were collected".

## First pass (18:25 to 19:41): 32, 40, 48, 56 on Factory, controls on Labs and Air Pit

One run per cell. fps is the mean over the window, ms values are frame times; "max entry" is the largest single frame
in the window. ModProfiler's own spikes are listed under "Integrity notes", not in this table.

| run | protocol | s | alive (polls) | fps | mean ms | p95 ms | p99 ms | max entry ms | frame-s vs wall | kills/shots |
|---|---|---|---|---|---|---|---|---|---|---|
| Factory 32 | harness | 240.1 | 32 (57) | 101.1 | 9.89 | 13.32 | 14.96 | 76.4 | 238.6 vs 240.1 | 14/86 |
| Factory 32 | Dauerlast | 150.2 | 32 (35) | 96.2 | 10.40 | 13.49 | 16.04 | 295.9 | 149.1 vs 150.2 | 19/61 |
| Factory 40 | harness | 240.7 | 40 (56) | 75.5 | 13.25 | 18.26 | 22.36 | 65.3 | 239.3 vs 240.7 | 35/521 |
| Factory 40 | Dauerlast | 151.4 | 40 (35) | 73.7 | 13.56 | 17.54 | 20.89 | 304.0 | 150.7 vs 151.4 | 44/414 |
| Factory 48 | harness | 241.5 | 48 (57) | 63.5 | 15.75 | 21.82 | 25.72 | 172.1 | 240.0 vs 241.5 | 61/492 |
| Factory 48 | Dauerlast | 151.3 | 46-48 (34) | 57.0 | 17.55 | 25.60 | 29.29 | 305.6 | 149.4 vs 151.3 | 83/408 |
| Factory 56 (control) | Dauerlast | 150.5 | 56 (34) | 61.8 | 16.17 | 22.67 | 31.44 | 805.7 | 148.6 vs 150.5 | 2/66 |
| Labs 32 | Dauerlast | 150.3 | 31-32 (37) | 93.9 | 10.65 | 14.28 | 18.72 | 8243.5 | 157.0 vs 150.3 | 33/192 |
| Labs 40 | Dauerlast | 150.4 | 40 (36) | 91.6 | 10.91 | 13.93 | 18.98 | 8740.9 | 157.6 vs 150.4 | 6/302 |
| Air Pit 32 | Dauerlast | 152.1 | 32 (35) | 85.4 | 11.71 | 15.12 | 25.51 | 2654.3 | 151.0 vs 152.1 | 17/536 |
| Air Pit 40 | Dauerlast | 150.9 | 40 (35) | 76.5 | 13.07 | 17.37 | 31.88 | 2442.9 | 149.4 vs 150.9 | 16/483 |

Windows: Factory and Labs Dauerlast 5 windows of 30 s, Air Pit Dauerlast 4 (the last was incomplete), harness 8.
Population sampled 34 to 57 times per protocol; it held exactly N except Factory 48 Dauerlast (46 to 48) and Labs 32 (31 to 32).
Air Pit admits Zombies solo on this host and is the only small ported map that was run.

Verdicts of the first pass:

- Primary holds: Factory 32 (13.49), Labs 32 (14.28), Labs 40 (13.93), Air Pit 32 (15.12). Misses: Factory 40 (17.54, by
  0.87 ms), Factory 48 (25.60), Factory 56 (22.67), Air Pit 40 (17.37).
- Secondary, limit `max(16.67, 13.32 + 1.5)` = 16.67 ms: Factory 40 misses with 18.26 ms (by 1.59), Factory 48 with 21.82.
- Factory 40 and 48 fail both thresholds.
- Cost per extra infected on Factory Dauerlast: +0.40 ms from 32 to 40, +0.50 ms from 40 to 48 (about 0.25 ms in the
  06.10. measurement of [GPU-ZOMBIE-PROTOTYPE-RESULTS-20261006.md](../2026-10/GPU-ZOMBIE-PROTOTYPE-RESULTS-20261006.md)).
- Dropping the first 30 s changes no verdict (Factory 40 17.30, 48 26.10, Air Pit 40 18.02).
- Reference check: the first 30 s window of Factory 32 (114 fps, 8.75 ms mean, p95 9.65) matches the 17.10. and 19.11.
  figures (110.7 fps, 9.0, 9.8). Over 150 s with the player standing still the mean rises to about 11.2 ms as the horde
  packs around him.
- Path budget counters (`[dev] Zombie planning jobs`, read about 13 s after each window with DeveloperMode on): `oldest`
  never exceeded 3 frames at any N, so it does not discriminate; `pending` 0 to 19; deferrals per frame at the end of the
  window 7.1 at 40, 5.0 at 48, 0 at 56. Instantaneous values only, not the design's p95 wait.
- Corpses on the ground were not measurable (`uibridge_bots aliveOnly=false` lists registered bodies only).

## Second pass (19:47 to about 20:50): candidate 36 and variance repeats

Same build, same settings; the cfg hash matched the first pass's backup at the start. D = Dauerlast (150 s), H = harness
route (240 s). Alive was exactly N except D-06 (31 to 32) and D-10 (35 to 36).

| run | prot | alive | fps | mean | p95 | p99 | max entry | kills/shots | weapon | integrity |
|---|---|---|---|---|---|---|---|---|---|---|
| D-01 Factory 32 | D | 32 | 91.7 | 10.91 | 13.68 | 18.07 | 6952 (ring artifact) | 6/67 | ADAR 2-15 | time ok, 1 capture error |
| D-02 Factory 36 | D | 36 | 92.7 | 10.79 | 14.25 | 17.00 | 319 | 0/70 | MP-153 | time ok |
| D-03 Factory 40 | D | 40 | 71.1 | 14.06 | 21.24 | 26.32 | 301 | 45/353 | RD-704 | time ok |
| D-04 Factory 36 | H | 36 | 85.7 | 11.67 | 15.26 | 18.86 | 137 | 22/95 | SR-25 | time ok |
| D-05 Factory 36 #2 | D | 36 | 86.6 | 11.55 | 15.58 | 20.45 | 306 | 12/184 | HK 416A5 | time ok |
| D-06 Factory 32 | H | 31-32 | 88.3 | 11.33 | 16.05 | 20.44 | 246 | 24/443 | AK-103 | time ok |
| D-07 Air Pit 36 | D | 36 | 89.9 | 11.13 | 13.77 | 27.88 | 2581 (artifact) | 0/69 | Mosin | time ok |
| D-08 Labs 36 (optional) | D | 36 | 107.4 | 9.31 | 10.88 | 16.75 | 306 | 1/46 | SVDS | time ok |
| D-09 Factory 36 (repeat of D-02) | D | 36 | 73.9 | 13.53 | 21.20 | 26.92 | 325 | 31/408 | AKS-74 | time ok |
| D-10 Factory 36 (repeat of D-04) | H | 35-36 | 77.1 | 12.97 | 18.52 | 23.38 | 173 | 39/455 | AK-103 | time ok |

"time ok": recorded frame time equals wall time within 3 %. 12 raids in this pass, 10 produced data; all ended with `gpu-run leave` exit 0, no deaths.

Verdicts for 36 (the same-session 32 harness run D-06 has p95 16.05 ms, so the secondary limit is 17.55 ms):

| run | verdict |
|---|---|
| D-02 | primary holds (14.25) |
| D-05 | primary holds (15.58) |
| D-09 | primary **misses** (21.20, by 4.53 ms) |
| D-04 | secondary holds (15.26 against 17.55) |
| D-10 | secondary **misses** (18.52 against 17.55, by 0.97 ms; against the first-pass 32 harness of 13.32 the limit is 16.67 and it misses by 1.85) |
| Air Pit 36 (D-07) | holds the primary (13.77) |
| Labs 36 (D-08) | holds the primary (10.88) |

Spread between identical cells: Factory 32 Dauerlast 13.49 and 13.68 (0.2 ms apart); Factory 40 Dauerlast 17.54 and 21.24
(3.7 ms apart, misses both times); Factory 36 Dauerlast 14.25, 15.58 and 21.20 (6.95 ms); Factory 32 harness 13.32 and 16.05;
Factory 36 harness 15.26 and 18.52. Air Pit 36 is 13.77 against 15.12 at 32 and 17.37 at 40; Labs 36 is 10.88 against 14.28
at 32 and 13.93 at 40.

Per 30 s window (Factory Dauerlast, fps and p95 ms): D-02 103/13.83, 92/12.08, 93/12.19, 82/16.78, 93/11.79; D-03 102/13.48,
72/17.58, 70/18.43, 52/26.62, 60/21.52; D-09 100/14.22, 76/16.47, 76/17.20, 57/27.20. ModProfiler's own 30 s intervals,
which do not go through the ring, show the same ordering: D-02 95 to 85 fps, D-03 81 to 52 fps, D-09 81 to 51 fps.

## Reading

- 40 and 48 miss both thresholds; 40 misses again in its repeat (21.24 ms).
- 36 holds in three runs (14.25 and 15.58 Dauerlast, 15.26 harness) and misses in two repeats (Dauerlast 21.20 ms, harness 18.52 ms).
  A value that passes or fails depending on the run is not a ceiling.
- 32 holds in every run with little margin under heavy fire (16.05 ms in the harness run with 443 shots).
- The kit weapon is random per raid and could not be controlled (below), so the spread is part of the result.
- With a ceiling of 32 the planned rule `max(setting, min(ceiling, ceil(1.5 x setting)))` would only raise limits that a
  player lowered below 32, which is the opposite of what that player asked for.
- Open to the owner: accept a softer criterion (40 alive is about 71 to 74 fps mean with p95 17.5 to 21 ms on this host)
  or pick an escalation after wave 11 that costs no frame time. Nothing of either is designed or built.

## Weapon and shot confound

The kit weapon is random per raid (MP-153, AKS-74, AK-74N, SKS, RD-704, AK-103, HK 416A5, M4A1, ADAR 2-15, SR-25, Mosin,
SVDS); kills range from 0 to 83 per run. Runs with few shots were light: MP-153 at 70 shots gave 14.25, Mosin at 69 gave
13.77, SVDS at 46 gave 10.88, ADAR at 67 gave 13.68. Automatic rifles with 350 to 455 shots gave 18.5 to 21.2 at 36 and 40:
AKS-74 408 shots 21.20, AK-103 455 shots 18.52, RD-704 353 shots 21.24. It does not explain everything: Factory 40 with an
AKS-74 at 414 shots gave only 17.54, and Factory 56 (SKS, 2 kills) being faster than 48 is probably this confound. Weapon
and shot count are not separable from N in these runs; for a comparison between two values, repeat each cell.

## Syncthing record

Every window had some host sync activity, and the VM was scanning or preparing throughout (its offline builds). Host 2's own
Syncthing REST is not reachable, so "host needing items" is the VM's view of the host's completion. Before/after states and
the most host 2 was still missing in any window (at most 6.0 MB, in the 36 windows D-02 and D-04) were recorded per run. The
two repeats D-09 and D-10 had the least sync traffic of the Factory 36 windows (at most 13 to 17 KB) and gave the worst 36
results, so sync does not explain the misses. (Labs 36, D-08, needed at most 1.3 KB and gave the best 36 result, which agrees.)

## Integrity notes

- **Ring holes**: polls in which the collector's cursor stood still while `Time.frameCount` advanced, 0 to 37 per run in the first pass and
  14 to 31 in the second. The time coverage held anyway.
- **Entries above 2 s**: 2.4 to 10.4 s single entries in Labs 32/40 and Air Pit 32/40 (first pass), D-01 (6952 ms) and D-07
  (2581 ms), and in the invalid first Factory-32 run. Labs 32 and 40 had frame time against wall off by 4 to 5 % because of them (157.0 and 157.6 s against
  150.3 and 150.4 s); every other run was within 3 %. p95 with and without them is the same.
- **Frame-count ratio**: recorded frames were 0.5 to 12 % above the `Time.frameCount` delta (1.0046 to 1.1253 in the first
  pass). Unexplained at the time; "complete coverage" held by time, not by frame count.
- **One capture error** (D-01, during the ModProfiler scan).
- Large single entries and ModProfiler spikes of 100 ms or more. The first-pass figures (Factory 40 Dauerlast 304 ms,
  Factory 48 Dauerlast 306, Factory 56 806) are **ring entries** that sit near a ModProfiler spike of that size, not
  ModProfiler's own numbers: the 304 ms of Factory 40 is the entry about 14.8 s into the window that the 30 s retention
  explains as the start of the warmup, and the 119 and 214 ms (`BotOwner.UpdateManual`, `AICoreController.Update`, gc) were
  matched to ring entries at about 92.6 s and 109.3 s; 150 gc for Factory 48, 260 gc and `ProfileBuildBudget` 227 for
  Factory 56. ModProfiler's own spikes are those of the second pass: D-05 246 and 262, D-07 153, 230 and 216 (the last two
  `ProfileBuildBudget`), D-08 244 (`ProfileBuildBudget` 222), D-09 267.
- **Loading stalls at the start of a run**: reading the raw files after the review, almost every run holds one entry of
  4 to 13 s in its first polls (D-10 12.9 s, Air Pit 40 11.4 s, D-09 12.1 s), most of them in polls labelled warmup, and
  the first-pass runs also hold 7.3 s (Factory 56), 8.2 s (Labs 32), 8.7 s (Labs 40) and 10.4 s (Air Pit 32). With the
  30 s retention a poll delivers frames that are about 30 s old, so these are frames of the raid start. The table's max
  entry for Factory 56 (805.7) and Air Pit 32 (2654.3) is smaller than the 7.3 s and 10.4 s in their raw files, so those
  two entries lie outside the analysed window; the Labs 32 and 40 entries lie inside it (frame time against wall above).
  The position of the others relative to their window was not re-checked. What stalled the game is not known.
- **Invalid first Factory-32 Dauerlast** (kept as `dauer-run1-anchor0-watchdog100*`): the player stood in the spawn pocket
  with 2 kills, the 100 s no-kill watchdog fired and the population fell from 32 to 9. Repeated at route place 3 with
  `StuckWaveTimeoutSeconds` 0; all later raids used that. The Factory-32 harness ran with 100 and the watchdog did not fire.
- **Air Pit needed three attempts**: the 24 m route spacing finds fewer than three spawn points and `uibridge_places
  kind=spawn` returns none there; two raids produced no data. The player stands where the Arena spawned him.
- One run per cell in the first pass; run-to-run variance was measured only in the second.

## How the frame times were collected, and what the ring really is

`tools/ZombiePerformanceChecks/run.mjs` read `_frameStats._samples/_next`, which UiBridge 0.10.1 does not have (4096 slots,
`_ms/_head`), and aborted at once. The operator wrote a stand-in (`bench.mjs`, `analyze-bench.mjs`, not in the repository),
which treats `_head` as the write cursor and reads the slots between two head values.

**Reading of the UiBridge source after the measurement (2026-11-19, `Core/FrameStats.cs`; derived from the source and the
simulated ring of the offline tests, not re-measured natively):** `_head` is the index of the **oldest** kept frame and moves
by age eviction (frames older than 30 s) and by capacity overwrite; the write slot is `(_head + _count) % 4096`. The stand-in
therefore read the frames that had been evicted since its last poll: every frame once and in order, but about 30 s late. This
explains the three oddities above (a head that stands still after a stall 30 s earlier, then one long entry; recorded frames
above the engine's frame delta because the frame rate of 30 s ago was higher than the one now) and has two consequences:

- the "combat" window of each run covers about the 30 s before the combat phase to 30 s before its end (so for the Dauerlast
  it contains the 15 s warmup and, for the harness, the whole 30 s warmup), and the ~300 ms entries that recur about 14.5 s
  into the Dauerlast window are what a 30 s retention predicts for the start of the warmup;
- the 2.4 to 10 s entries are frame times the game really had about 30 s before the poll that delivered them, not artifacts of
  the recording. What stalled the game is open (ModProfiler's own intervals show no such stall).

The decision does not rest on this: all cells were collected the same way, every cell is a window of 150 s or 240 s with the
horde present, the ordering of the cells agrees with ModProfiler's own 30 s intervals (which do not use the ring), and the
misses of 36, 40 and 48 are run-to-run effects far above the shift. The figures above are quoted as measured and must be read
as "about 150 s (240 s) of horde, shifted by about 30 s from the labelled phase". The collector in the repository was ported to the real
ring semantics on the same day (`tools/ZombiePerformanceChecks/README.md`); that port has not run against the game yet.

## Restore proof

Profile `6ab7db1c64f04b565cf78be9.json`: after the first pass the hash changed from 72aece31 to 81220fcf, after the second pass
from 81220fcf to 43bdfbdc, host and VM identical each time. A deep JSON diff shows exactly two differing values per pass,
`Hideout/sptUpdateLastRunTimestamp` and `Health/UpdateTime`; inventory, money, skills and quests are unchanged, and the other
profile files are hash-identical before and after. The ArenaMode cfg changed only `Match/GameMode` (deathmatch to zombie)
for the raids and was set back with `uibridge_config_set` before EFT closed; its SHA-256 (`f9af829f...`) equals both backups
on host and VM. EFT was closed with `uibridge_quit`, the SPT server stopped with `gpu-run server stop` ("server not running,
game not running"), all `persist=false` values ended with the EFT process, nothing was installed or edited in any game
folder, and the client DLL is still `0.6.0+72273354`. Backups: `D:\SPT41-Backups\zombie-ceiling\20261119-182506\` and
`...\20261119-194705\` on the host.

## Where the raw data lies

In the git-ignored working folder `artifacts/opening-rounds-20261119/native/ceiling/` of the worktree
`ArenaMode-opening`: one subfolder per run (`A-factory-32` ... `C-airpit-40`, `D-01` ... `D-10`) with `dauer.jsonl` or the
harness file, summary JSON, console output, `log-evidence.txt` (cap and match-start lines), `prepare.json`, the
`gpu-run raid`/`leave` output and, in the second pass, `D-modprofiler-meta`; beside them `FINAL-MESSAGE.md`,
`FINAL-MESSAGE-2.md` (the operator's final messages, cross-checked by the orchestrator against independent recomputations from
the raw files to two decimals), `D-summary.txt`, `table-rows.md`, `all-runs.json` and the operator's `tools/`. **The raw files
are not in the repository** and contain private identifiers; this report quotes what matters. The one tracked excerpt is
`tools/ZombiePerformanceChecks/fixtures/stand-in-ring-hole.jsonl` (12 ring polls of the Air Pit 32 run, a few kilobytes of
frame times).

## Not measured

Waves beyond the opening of a real match (the runs hold the population at N from a wave of 200), corpses on the ground, a
second small ported map, an escalation that costs no frame time, and the effect of a different shooter share. The
alive-limit growth was not built, so none of its code was measured.
