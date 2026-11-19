# Zombie escalation after the plateau (2026-11-19)

Contract for what Zombies do once the fade of [DIFFICULTY-MODEL-20261118.md](DIFFICULTY-MODEL-20261118.md)
has run out. Built in two stages, in the owner's order: special waves first, the alive-limit growth only
after a native measurement. This document describes **stage Z1 as built** (special waves, their text
announcement, the per-wave composition line, the menu row at Reduced) and records **stage Z2 as planned**
(the alive-limit growth), which is not built.

Read [ZOMBIE-MODE-REVIEW.md](ZOMBIE-MODE-REVIEW.md) before touching the wave policy or its watchdog and
[ZOMBIE-FLOW-20261115.md](ZOMBIE-FLOW-20261115.md) for the composition this builds on.

## The problem

At Maximum the slow share is 0 from wave 11 (the rhythm is at most 40 and the fade at least 40), the
shooter share sits at its 2x cap from wave 11 and the alive limit is the setting. From wave 11 on nothing
but the wave length escalates: wave 12 and wave 40 are the same fight with different counts.

## Special waves (built, Maximum only)

- **Schedule.** Every fifth wave from wave 5 is special. Odd multiples of five are **ALL RUNNERS**, even
  multiples **ALL SHOOTERS**: wave 5 runners, 10 shooters, 15 runners, 20 shooters, and so on. Pure policy
  in `ArenaDifficultyPolicy` (`ZombieScheduledTheme` is the calendar, `ZombieWaveThemeFor` what applies),
  no state and no random number.
- **Maximum only.** Reduced is "somewhat less of everything" with a flat shooter share and never fades, so
  the plateau does not exist there and an all-shooter wave would contradict its promise. Reduced keeps its
  flat mix and every Reduced expectation of the earlier contracts.
- **What a theme changes.** The per-wave slow and shooter shares and nothing else: not `ZombieRoleMix` (the
  roll order role, shooter, slow is unchanged, 0 % and 100 % stay exact), not the call line
  `ZombieRoleMix.Next(...)`, the role weights, the wave size, the alive limit, the profile plan, the backup
  targets, the spawn path, recovery or the watchdog. A Runners wave draws only `normal` (fast melee), a
  Shooters wave only `hard`. No new role and no new difficulty: `infectedTagilla` is a boss on its own brain
  and `BurstSpawnService.SpawnInfected` rejects it on purpose, and `impossible` throws in the native
  constructor, so neither is a theme.
- **The user's shooter share wins at its exact values.** With `ShooterPercent` 0 there is never a Shooters
  wave (that wave is an ordinary Maximum wave); with 100 there is never a Runners wave. Values 1 to 99 get
  both. `ZombieWaveThemeFor` drops exactly those two themes and the two share functions apply the schedule
  after their exact-0/100 early returns, so the announcement and the draw agree by construction; a test
  checks the decider against the shares for every setting 0..100 and waves 1..60. At 100 the Shooters wave is
  still announced although every wave is already all shooters; that is accepted (it is what the user chose,
  announced as the landmark it is).
- **Effect at the default settings (shooter 20, Maximum).**

| Wave | Without themes | With themes |
| --- | --- | --- |
| 4 | slow 35 % of melee, shooters 26 % | unchanged |
| 5 | slow 10 %, shooters 28 % | **ALL RUNNERS**: slow 0, shooters 0 (all fast melee) |
| 10 | slow 5 %, shooters 38 % | **ALL SHOOTERS**: every infected `hard` |
| 11 | slow 0, shooters 40 % | unchanged: the release after a Shooters wave |
| 15 | slow 0, shooters 40 % | ALL RUNNERS |
| 20 | slow 0, shooters 40 % | ALL SHOOTERS |

- **Shooters are harsher than the first design said.** `InfectedAttackLayer.GetSlowShootingZombieDecision`
  shoots from place when the enemy is visible and closer than 15 m, but with a line of fire at any other
  distance it shoots **while moving** (`attackMoving`); 15 m only separates standing fire from fire on the
  move, it is not an engagement limit. They still move at walking speed, and the holster pistol exists for
  all four infected roles. Wave 10 therefore means a full wave of pistol shooters closing in under fire. The
  first native run reads this (below); fallback if wave 10 turns out unfair: the first Shooters wave later
  (wave 15), one constant of the schedule.
- **Crowd.** With the optional Zombie Crowd on, shooters stay on the native cast (the recall skips them), so a
  stranded shooter is handled by individual recovery and the watchdog only. One optional Crowd-on pass of
  the wave-10 run belongs to the acceptance.

## The announcement (built, text only)

Existing mechanisms only: the HUD text callout (`ArenaMatch.ReportTextCallout`, 44 pt, 1.6 s, never gated
by the announcer) and the status line. No sound and no announcer cue: the cues also drive the lighting
director and the crowd reactions, and the announcer is off in Zombies by default. Logging is Debug only.

| When | Where | Text |
| --- | --- | --- |
| first evaluated tick of the intermission before a special wave | callout, once per wave | `NEXT: ALL RUNNERS` / `NEXT: ALL SHOOTERS` |
| that intermission | status line | `WAVE 5 (ALL RUNNERS) IN 7s` |
| the special wave starts | callout (replaces `WAVE n`) | `WAVE 5: ALL RUNNERS` / `WAVE 10: ALL SHOOTERS` |
| during the wave | status line | `WAVE 5 (ALL RUNNERS) | 112 LEFT | 03:41` |

Ordinary waves keep exactly `WAVE n` and their status line. The label is at most 12 characters (the score
panel line already carries wave, count and clock) and a harness pins that.

- The theme is computed once right after the policy result (`themedWave` is the running wave, or the next
  one during an intermission) and read by the wave-start callout, the status line and the warning.
- **PitFireTeam.** The status-line change sits before the `_team.WaveCleared(); return;` exit, the warning
  callout **after** it: the squad is held in the restore barrier (its own status line, the countdown not
  started), so a warning raised there would show while the player is being restored and be gone before the
  countdown. It fires on the first evaluated tick after the barrier. A harness pins no warning during the
  barrier and exactly one after it.
- **Limits, documented and not worked around.** `CalloutElement.Show` replaces its text (no queue): a kill
  callout in the same 1.6 s overwrites the warning, and with an intermission under 1.6 s (PitFireTeam forces
  only 0.1 s) the wave-start callout replaces it. A zero-length solo intermission crosses the break inside one
  call, so no warning can precede the wave; the wave-start callout and the status line are then the first
  signal. The longest text (`WAVE 10: ALL SHOOTERS`, 21 characters at 44 pt in an 800 px cell) and the status
  line at wave 10 with a streak (it wraps in the score panel without a rival line) are read in the native
  screenshots.

## The composition line (built)

The bodies carry no difficulty a reader of the game can see (`uibridge_bots` has Role but no difficulty, and
slow and fast melee share the layer), so the native acceptance could not have told the waves apart. `ZombieMode`
counts the **accepted** dispatches of a wave by difficulty (a rejected dispatch returned its slot and is drawn
again, so it is not counted) and writes one Debug line per wave, when the policy's own spawn count reaches the
wave size (`SpawnedThisWave`, not the sum of the draws). That is the only trigger: the policy counts a slot only
together with a draw the tally sees, and a wave cannot end before the count reaches the size, so a wave-end flush
would be unreachable (an earlier version had one; the review found no test could reach it):

`Zombie wave 5 composition: easy 0, normal 50, hard 0 (size 50, theme Runners).`

It is reset per wave and written once. A slot that fails asynchronously (profile or placement) or is returned by
individual recovery is drawn again: the first draw stays in the tally (the sum can exceed the size) and the line
is written when the policy count is reached, so it states the **requested** mix, not the bodies that arrived.
The arrived mix of wave 10 (arrivals per pair, time to fill) is only measurable natively and stays a gate of
stage Z1: do not treat this line as proof of it (`ZombieProfilePlan.BackupTargets` sizes the pools from the
plain shooter setting, so an all-shooter wave at a low setting asks for more hard profiles than were preloaded).
This line is what the native acceptance reads (`gpu-run log`). The first design printed
the slow share from `ZombieCompositionPolicy`, which none of the harnesses that compile `ZombieMode.cs` links or
stubs; the tally needs no such type.

## The menu row (built)

ALIVE AT ONCE shows the **effective value first at Reduced when the cap bites**: with a setting above 24,
`24 (setting 32, Reduced)`; with 24 or less it is the plain setting. At Maximum it is the plain setting, as
before (no growth hint: the wave may never reach it, and the row would promise what a small wave size cannot
deliver). The other two rows are unchanged. The slider stays the editor of the **setting**: the view never feeds
the shown text into the slider, showing it writes nothing, and the cfg keeps the slider value.

- The step is `ArenaSession.Difficulty ?? Settings.DifficultyStep.Value`: the step frozen at arm when the raid
  is armed (an F12 edit afterwards changes nothing for that raid), the live setting otherwise, which is what the
  arm freezes next. The menu refreshes every 0.2 s, so a change in F12 reaches the row at once; the text is
  assigned only when it changed.
- The long text moves that one cell to 12 pt, right aligned, left 110 and width 210 of the 320 px panel (it ends
  at the slider's right edge) and the plain value restores 14 pt, centred, left 262, width 58. Names
  (`Zombie value MaxAlive`) and the row geometry are unchanged. The fit of the 24 characters is assumed from the
  geometry and read in a native screenshot.
- **gpu-run reads this value.** `tools/gpu-run/lib/arenaMenu.mjs` compared the leading integer with the slider it
  had just written to the cfg, so `--zombie-max-alive 32` at Reduced would have failed before the raid. It now
  reads the number after `(setting` when present (`zombieSettingOf`; UiBridge repository, same step).
- The F12 descriptions of `Zombie/MaxAlive`, `Zombie/ShooterPercent` and `Difficulty/Step` name the Reduced cap
  and the special waves.

## The profile pool is not changed (a finding, no remedy built)

The all-shooter wave goes through the native miss path almost entirely; this is stated here so nobody counts on
the pool.

- `BotSpawner.AddToTargetBackup` is first-write-wins (`BotProfileBackuploader.cs:257-263`): a later call for the
  same (role, difficulty) is ignored, so a pool target cannot be raised mid-raid; it is set once at match start
  by `ZombieProfilePlan.BackupTargets`.
- The loader's top-up collapses. `CacheSimpleProfilesChecker` computes `int num4 = 100 / num3;` after summing
  the pending loads; with more than 100 profiles wanted that is **integer division and 0**, and every pair's
  load is then clamped to 1. The default targets are 12 pairs x 25 = 300, so the loader adds about one profile
  per pair per 30 s (the first check 60 s after creation, and only when the deficit exceeds 3) and never reaches
  the 25 per pair the plan assumes. At wave 10 the Hard pool is close to empty.
- A miss goes through `BotProfileClient.CreateProfile`, which requests a batch; SPT rounds a request up to
  `presetBatch`, 30 for each of the four infected roles in the installed `bot.json` (the rounding rule itself is
  assumed to apply unchanged to infected). Wave 10 at the default size (230 infected, all `hard`) therefore means
  roughly four misses for assault, two for pmc and one each for civil and laborant, about 8 x 30 profiles at about
  2.9 ms each, spread over frames by the profile build budget (default 3 ms per frame). One miss costs the awaiting
  spawn slot about a third of a second; nothing else stalls. Bundle loads for the pistol and ammunition of each new
  hard profile add to that (assumed, not run).
- Composition-only themes need no pool change: a Runners wave needs the `normal` pair (present whenever the
  shooter share is below 100) and a Shooters wave the `hard` pair (present whenever it is above 0), exactly the
  complement of the two dropped themes; a test pins it for every shooter setting.
- If the miss storm hurts in the native run, the options are owner decisions, not built: (a) accept the miss path;
  (b) cap `BackupCountForWaveStartSize` so that pairs x count is at most 100 (12 pairs x 8 = 96 is loaded in full;
  this touches the pinned `ZombieProfilePlan` checks); (c) pre-build the Hard pool in the intermission before the
  themed wave. A larger target is not a remedy: it is ignored when a target exists and only enlarges the collapsed
  sum.

## The alive-limit growth (planned, not built)

Owner decision (b): from wave 12 the alive limit rises step by step to a ceiling that is **measured natively
before the number is fixed**. Nothing of it is in the code: no constants, no `ZombieAliveLimit`, no per-wave limit
in `ZombieWavePolicy`, no change of the native bot cap in `ArenaBotPatches.cs` (it still reads
`ZombieMaxAlive + 2 + companions`), no hint in the menu row.

Decisions recorded for stage Z2:

- Maximum only; from wave 12; +4 per wave; limit = `max(setting, min(ceiling, ceil(1.5 x setting)))`, never above
  the slider maximum 96 (a low setting was chosen for performance, a high one never shrinks).
- A `ZombieAliveLimit(configured, ceiling)` seam with the ceiling constant behind a one-argument overload, so every
  table test passes an explicit ceiling and survives the drop-in of the measured number.
- Grown on top of `config.MaxAlive` (the live source of the base stays the `ZombieMaxAlive(_step, ...)` line), so
  the existing `alive-limit-ignores-step` mutant still dies; a seam that recomputed the base from the step would
  make that mutant equivalent.
- The native bot cap follows the limit (it clips the growth silently otherwise: 34 at a setting of 32), with a pin
  that `limit + 2 + companions` stays within the server floor of 104 and that the slider maximum equals the hard
  limit. No menu hint.

**Measurement plan** (Factory at 32, 40 and 48 alive, one small ported map, 56 as the known-bad control): a fresh
raid per value, `Zombie/MaxAlive` = N through `uibridge_config_set persist=false`, `WaveStartSize` 200,
`WaveGrowth` 0, the same kit, route and shooter share, godmode and infinite ammunition,
`ZombiePerformanceChecks/run.mjs` with `analyze.mjs` and the quiet Dauerlast protocol of 2026-10-17. Threshold:
p95 frame time over the combat interior at most **16.67 ms** (60 fps) with complete frame coverage, **and** at most
`max(16.67 ms, p95(32 in the same session) + 1.5 ms)` as the relative guard against a harness whose own baseline
already misses (32 measured 17.1 to 17.8 ms there). Also recorded: the maximum frame and the spike list around wave
changes, the path-budget counters (`OldestWaitFrames`, at most 1.5x those at 32), the corpses left on the ground and
the `Arena bot cap set to` line. One control pass on Labs (the 13.10 data show maps disagreeing: Labs 28 missed where
Factory 28 passed) and one on the small ported map (Chop Shop, solo: a yard of about 1,900 m2 is a different problem
than Factory). The highest passing N becomes the ceiling; if none passes above 32 it is 32 (growth inert) and only
the special waves ship.

## Native acceptance of stage Z1 (not run)

All values with `uibridge_config_set ... persist=false` before the raid (they die with a restart of EFT; the three
sliders go through the recipe options `--zombie-max-alive/--zombie-wave-start/--zombie-wave-growth`, which write and
restore the cfg), on GPU-2, `uibridge_help topic=overview` first. The fast skipper is the stuck-wave watchdog:
godmode on, no kills; a wave of N at most the alive limit takes about `0.2 x N + StuckWaveTimeoutSeconds`.

| Goal | Settings | Time |
| --- | --- | --- |
| Waves 5 and 10 and their announcement | Maximum, ShooterPercent 20, WaveStartSize 6, WaveGrowth 0, MaxAlive 8, IntermissionSeconds 8, SpawnIntervalSeconds 0.2, StuckWaveTimeoutSeconds 4, run on to wave 11 | about 2.5 min |
| Shares over a larger sample | the same with WaveStartSize 60, MaxAlive 32 | longer |
| Reduced control | the same with Step Reduced: no theme text | 3 min |
| Full-size shooter wave and the pool (above) | defaults, StuckWaveTimeoutSeconds 5, IntermissionSeconds 2 | wave 10 after about 8 min |

What to read: the composition line per wave in the client log at Debug (`gpu-run log`): wave 5 `easy 0, normal N,
hard 0 (..., theme Runners)`, wave 10 `easy 0, normal 0, hard N (..., theme Shooters)`, wave 11 about 40 % hard at
shooter 20 (80 % at 40: the share is `round(setting x 2)`), the callouts in screenshots at the intermission start and
the wave start (fit of the 21-character text), `HudStatusLine` through the F9 panel, and for the pool the
`Arena profile build budget: N bot profiles built over F frame(s)` lines during wave 10. Acceptance: shooters' hit
rate against the player at wave 10 (the fallback above), PitFireTeam with one companion (no warning in the barrier,
one after it), shooter 0 (wave 10 ordinary) and shooter 100 (wave 5 ordinary, no warning), the menu row read through
`uibridge_snapshot` while F12 switches the step (`24 (setting 32, Reduced)` and back) and a screenshot of the longest
text, the whole-profile before/after comparison and the loaded hashes. The watchdog skipper invalidates reward capture
(`ArenaRewardCapture.Invalidate`); a run must not count on Zombie rewards. A dated report
`reports/2026-11/GPU-ZOMBIE-WAVES-20261119.md` records the run and what was not run.

## Where it is pinned

- `ZombieCompositionChecks`: the calendar table, the decider against both share functions for every shooter
  setting and wave, the draw buckets of waves 5 and 10 (`faded[4] = 0`, shooter share 0 at wave 5 and 100 at wave 10
  except at the exact settings), and the faults on each rule (interval, wave 0, Reduced, order, slow kept, shooters
  kept, shooters not all hard, shooters at 0, runners at 100, the decider's two drops).
- `ZombieWavePendingChecks`: the callout sequence of waves 1 to 10, the status line, one warning per wave, shooter 0
  and 100, Reduced and a later live step, the zero-length intermission, the PitFireTeam barrier, the composition line
  (counts, once per wave, reset per wave, rejected dispatches, theme); `ZombieSoloChecks`, `ZombieTeamChecks` and
  `ZombieRecoveryChecks` compile or run the same `ZombieMode.cs`/`ZombieWavePolicy.cs`.
- `TdmMatchMenuChecks`: the row text for both steps and the settings 4, 20, 24, 25, 32, 96, the live step in both
  directions, the frozen step when armed, the cell geometry, the slider keeping the setting, no config write.
- xunit: `ArenaDifficultyPolicyTests`, `ZombieRoleMixTests`, `ZombieProfilePlanTests`.
- Existing expectations changed because the decided behaviour changed them: waves 5 and 10 in
  `ZombieCompositionChecks` and the `(5, 20, 10)` and `(20, 50, 40)` rows of `ArenaDifficultyPolicyTests`.
