# Zombie population benchmark

External, single-operator native-game benchmark. No game DLL is patched or built.
Requires the sibling UiBridge repository's configured `tools/gpu-run` MCP proxy
and a dedicated, idle **SPT-GPU-2** test profile. Run from the ArenaMode root;
`UIBRIDGE_GPU_RUN` can point at another checkout of the same official tool.
Credentials stay in the existing private `.claude.json`/token files.

**Status: ported offline on 2026-11-19, native smoke run pending.** `run.mjs`, the poller and the analyzer
were rewritten for the UiBridge 0.10.1 frame ring from the plugin source and from the operator's stand-in collector
of the 2026-11-19 ceiling measurement, which did run natively. The rewritten collector itself has only run against a
simulated ring (`frame-stats-sim.mjs`, a port of `FrameStats.cs`); nobody has run it against the game yet. The first
native run must be a short smoke check (see "Smoke check") before any figure from it is quoted.

## Requirements

A UiBridge plugin with the 4096-slot ring (`FrameStats._ms/_head/_count`; the source history puts the change on
2026-10-23 in the 0.9.4 line; read against 0.10.1) and the multi-read form of `uibridge_inspect action=read` (plugin 0.9.6).
`run.mjs` fails at the start with a message naming this requirement when the fields are not there, so the older 300-slot
layout (`_samples/_next`, plugin 0.9.1) is not supported any more: its collector read two pages of 300 floats and
reconstructed torn reads, a different problem from the one below.

## What the ring is (read from UiBridge `Core/FrameStats.cs`, 2026-11-19)

- `_ms` is a `float[4096]` of frame durations (`Time.unscaledDeltaTime * 1000`), `_at` the stopwatch time of each. One
  slot is one frame, written once per frame from `Plugin.Update` on the main thread.
- `_head` is the index of the **oldest** kept frame, `_count` how many are kept, the next write goes to
  `(_head + _count) % 4096`. Frames also leave **by age**: before every write, every frame older than 30 s is dropped by
  moving `_head` up and `_count` down; at 4096 kept frames (above 136 fps) the oldest is overwritten the same way.
- So `_head` is not a write cursor. It moves with the frame rate of about 30 s earlier. The write slot
  `(_head + _count) % 4096` moves by exactly one per recorded frame whatever the eviction does, and that is the cursor
  this collector uses.

The stand-in of 2026-11-19 (`native/ceiling/tools/bench.mjs`, not in the repository) used `_head` as its cursor and
read the slots between two head values. That reads the frames that were **evicted** since the last poll: every frame once,
in order, but about 30 s late. The source explains the three oddities the operator saw:

| Observation | Explanation from the source |
|---|---|
| Polls in which the head stands still while `Time.frameCount` advances ("ring holes", 14 to 37 polls per run) | Nothing is due for eviction while the next-oldest frame is younger than 30 s. After a stall of S seconds 30 s earlier there is no frame with a timestamp inside the stall, so the head stands still for about S seconds. |
| Single entries of 2.4 to 8.7 s (and 10.4 s in one Air Pit run) right after such a stall | The one frame that ended the stall, delivered when it turned 30 s old. It is a real frame time of the game, 30 s before the poll that read it. The fixture `fixtures/stand-in-ring-hole.jsonl` holds two such sequences. |
| Recorded frames 0.5 to 12 % above the `Time.frameCount` delta | The head advance is the frame rate of 30 s earlier, the engine counter the rate now. A rate that falls during the run (the horde packs around the player) gives a ratio above 1. |

Two consequences for the earlier measurement, derived from the source and **not** re-measured: its "combat" window was
the frames from about 30 s before the combat phase to 30 s before its end (so it included the warmup; the ~300 ms
frames that recur at about 14.5 s into the Dauerlast "combat" would then be frames from the start of the 15 s warmup, which
is what a 30 s retention predicts),
and the 2.4 to 10 s entries are hitches of the game some 30 s before the poll, not artifacts of the recording (what
stalled the game is not known; ModProfiler's 30 s intervals did not show them, and that difference is open). The
verdicts of the ceiling measurement compare cells measured the same way and do not depend on this; the figures are
windows of about 150 s or 240 s of steady horde either way. See `docs/reports/2026-11/GPU-ZOMBIE-CEILING-20261119.md`.

## How a run works

1. Read ArenaMode `AGENTS.md` and UiBridge's overview/raid/shooting playbooks.
2. Record loaded binaries, hardware, graphics, original configuration and profile
   in an **ignored** artifact folder. Check that no other user owns the host.
3. Use official `gpu-run config-pin` ledgers (or `uibridge_config_set persist=false`) and the same explicit
   `--host host2 --lane <your-lane>` for settings/start/leave/restore. Set
   `$env:ZOMBIE_BENCH_LANE = '<your-lane>'` to the same label for this harness
   (default remains `zombiebench-20261013`). Each MaxAlive needs a fresh raid. Keep kit, graphics, shooter percentage
   and spawn interval fixed. For a sustained population, use a wave larger than the cap (`WaveStartSize` 200, `WaveGrowth` 0).
4. Immediately after official `gpu-run raid`, run `prepare.mjs output.json` to
   enable godmode and infinite ammunition with weapon condition preserved.
   A loaded magazine weapon is required; the test profile may be unarmed.
5. `node tools/ZombiePerformanceChecks/run.mjs [harness|dauerlast] output.jsonl seconds [route.json] [--profiler] [--path-budget]`
   controls an **already started** raid. Without a mode word it is the harness route, as before.
   - **harness (route)**: 30 s warmup, then walking, native head-directed gunfire and a relocation every 55 s along a
     route of observed `uibridge_places` spawn records (checked against this map; reuse the same route file for
     comparisons). Secondary protocol, 240 s in the ceiling measurement.
   - **dauerlast**: the player stands at the route anchor (or where the Arena spawned him) and fires at the nearest
     visible infected; 15 s warmup, 150 s in the ceiling measurement. Primary protocol.
   - `--profiler`: ModProfiler headless capture with 30 s exports around the run. `--path-budget`: DeveloperMode for
     about 13 s after a Dauerlast to read the `Zombie planning jobs` counters.
   No synthetic kills or spawns. The collector polls every 0.35 s (stand-in: 0.7 s), see the comment at `POLL_WAIT_SECONDS`
   in `run.mjs`: figures are comparable between cells of this collector, not directly with the ceiling report's. The
   stand-in's `bots-all` and `final-population` records are not ported (corpses are not measurable; `bots` every 20 s
   gives the alive count).
6. `node tools/ZombiePerformanceChecks/analyze.mjs output.jsonl 32 summary.json` reports the combat interior: statistics,
   30 s windows (cumulative frame time, never an average of percentiles), spikes above 100 ms, population, kills, shots,
   weapon and the integrity verdict. The second argument is the alive cap used for the near-cap time. For early evidence
   files without a recorded duration a fifth positional argument supplies it; a conflicting override is rejected.
7. Leave via official `gpu-run leave`, verify the protected profile, restore configuration ledgers in reverse order and
   confirm all test hooks are off. **Do not leave the live raid running during lengthy analysis.**

The runner deliberately leaves godmode/ammo enabled until protected exit so the player cannot die during the handoff to
the leave recipe. They end with the raid. The combat loop reapplies godmode about every two seconds because UiBridge
binds its kill guard to the health controller, which a new wave kit can replace.
On failure, finish that cleanup instead of merely stopping the shell process.
Do not replace the saved profile to conceal failed protection.

### Traps of the protocols

- **The no-kill watchdog.** `Zombie/StuckWaveTimeoutSeconds` (installed value 100) clears the horde when the player
  makes no kill for that long. A Dauerlast in which the player stands in a pocket and kills little loses its population
  (the first Factory-32 Dauerlast of 2026-11-19 fell from 32 to 9 alive). Set `StuckWaveTimeoutSeconds` to 0 for the raid
  (`uibridge_config_set persist=false`) or keep the player killing.
- **Route spacing on small maps.** The route wants places more than 24 m apart; Air Pit lists fewer than three such spawn
  points and `uibridge_places kind=spawn` returned none there. `run.mjs` relaxes the separation to 12, 6 and 0 m; a
  Dauerlast then runs with whatever it finds, or with no anchor at all (the player stands where the Arena spawned him,
  logged as `anchor` with `index: null`). The harness needs three places and stops without them.
- **The kit weapon is random per raid** (MP-153, AKS-74, SKS, RD-704, AK-103, HK 416A5, M4A1, ...) and changes the
  result: runs with automatic rifles and 350 to 455 shots were 3 to 7 ms slower at p95 than runs with a few dozen shots
  at the same population. The summary records `weapon`, `shotCount` and `kills`; for a comparison between two values
  repeat each cell and read the spread before reading a difference (the same cell differed by up to 7 ms).
- Arena kits (`OwnGear` false), the same shooter share and `Match/GameMode` zombie for every cell; restore the cfg and
  verify its SHA-256 afterwards.

## Measurement

Every poll is one record: `Time.frameCount`, `_head` and `_count` in one main-thread multi-read, then the slots from the
previous write cursor to this one (pages of at most 200), then the three fields again (`ring-poll.mjs`). The reader
(`ring-analysis.mjs`) takes the record's cursors and proves what it can:

- the slots were not overwritten while they were copied: `n + (w1 - w0) <= 4096`;
- the cursor advanced exactly as far as the engine frame counter did (tolerance 0). In the UiBridge source `Plugin.Update`
  adds the frame first and drains the inspector queue last, in the same call, so the cursor and `Time.frameCount` of one
  read belong to the same frame; no frame can be "added just before or after the read". One slot per frame is the ring's
  contract, so this fails when the plugin did not record a frame, when two polls were joined wrongly, or when the cursor
  arithmetic is not what the source says. A tolerance of one frame would let an error of one frame per poll (about 2 percent
  at 85 fps) pass both this gate and the 3 percent wall-clock gate. Should the smoke run show deltas other than 0, read the
  poll records; do not widen the tolerance without an argument from the source;
- no wrap is guessed: polls 4096 or more engine frames apart cannot be joined, the poller skips the read and a new segment
  starts. A failed poll loses no data (the next one covers its interval) but the run is no longer a qualified pass: any
  capture error fails `runIntegrity`. A poll is bounded by its start time (the label is the phase at the start, the
  interval ends at the cursor of the first read).

Analyze the interior of the combat phase, excluding setup, warmup and the final drain: the frames the first
combat-labelled poll delivered are dropped, the interval ends with the last combat-labelled poll. Never average window
percentiles. Missing frames count as infinitely slow for the conservative P95 criterion.

**What the integrity rule can prove with this ring.** A whole-run pass needs: the requested workload duration (250 ms
tolerance), complete phase coverage (first and last sample within 1.5 s of the phase), matching process and map, no
capture error, one segment and no discontinuity, and two checks the stand-in could not make:

- `frameTimeMatchesWall`: the recorded frame durations of the analyzed interval add up to the wall time between its first
  and last poll within 3 %. This is the check the operator could make; it fails for a missing or duplicated stretch.
- `frameCountMatches`: no poll showed a cursor advance different from the engine frame counter advance. The stand-in
  could not pass it (ratios 0.5 to 12 % above 1, see above); with the write cursor it can fail, and it is what shows that
  the ring is the stream of consecutive frames the analysis assumes.

What it cannot prove: that the game rendered no frame that `Plugin.Update` missed (the counter and the ring are both
updated every frame by the same loop), or that a long frame was a stall of the game and not of the clock. `qualifiedPass`
alone does not prove a sufficient gameplay workload: check the actual near-cap population, enough successful shots and
kills, movement and repeatability separately.

## Automated checks (offline, no game)

```powershell
node --test tools/ZombiePerformanceChecks/ring-analysis.test.mjs tools/ZombiePerformanceChecks/ring-poll.test.mjs tools/ZombiePerformanceChecks/run-integrity.test.mjs tools/ZombiePerformanceChecks/run-summary.test.mjs
```

The poller and the reader run against `frame-stats-sim.mjs`, a port of `FrameStats` (float32 slots, eviction by age,
capacity overwrite) driven by scripted frame times: the reconstruction must equal the frames the simulated game rendered,
across ring wraps, a falling frame rate, a rate above 136 fps, an 8 s hitch and a failed poll. A test also runs the
stand-in's head-as-cursor algorithm against the same simulation and shows the late frames and the still head, and a
fixture cut from the real stand-in data (`fixtures/stand-in-ring-hole.jsonl`, 12 polls of run C-airpit-32) shows the same
shape in the recorded data. `Verify-Collector.ps1` runs them (a no-game step registered in `tools/verification-steps.txt`, so
`-ChangedSince` selects it); `Verify.ps1` / `Verify-NativeContract.ps1` of this folder check the compiled performance patches,
not the collector.

## Smoke check (pending, native)

Before quoting a figure from this collector: one raid on SPT-GPU-2, a 60 s Dauerlast at the default population, and then
`analyze.mjs` must show `integrity.frameCountMatches` and `integrity.frameTimeMatchesWall` true, no discontinuity and
`frameCountRatio` (recorded frames of the interval over `engineFrames`, the `Time.frameCount` advance of the same interval)
within 2 % of 1; with the exact cursor it should be 1. A failing `frameCountMatches` means the cursor reading of
the source is wrong somewhere; do not fix the tolerance, read the poll records. Open for that run: whether the 2.4 to
10 s hitches reappear at the time they happen (with the new cursor they are no longer 30 s late) and what causes them.

Raw JSONL/profile/log evidence can contain private identifiers. Keep it ignored;
commit only reviewed aggregate results and hashes. `compare-profile.mjs` reports
changed paths without printing profile values.
