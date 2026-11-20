# Zombie escalation after the plateau (2026-11-19)

Contract for what Zombies do once the fade of [DIFFICULTY-MODEL-20261118.md](DIFFICULTY-MODEL-20261118.md)
has run out. Planned in two stages, in the owner's order: special waves first, the alive-limit growth only
after a native measurement. This document describes **stage Z1 as built** (special waves, their text
announcement, the per-wave composition line, the menu row at Reduced) and records the alive-limit growth
(stage Z2) as **measured on 2026-11-19 and not built**: the ceiling stays 32.

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
- **The user's shooter share wins at its exact values.** At exactly 100 every wave is already all shooters, so
  **no special wave is announced at all**: `ZombieWaveThemeFor` returns `None` for every wave (an earlier build still
  announced NEXT: ALL SHOOTERS there, a landmark that marks nothing; changed on 2026-11-19, together with the
  decider's doc comment, the xunit rows, the harness tables and the mutants). With `ShooterPercent` 0 there is never
  a Shooters wave (that wave is an ordinary Maximum wave) but Runners stays: it still removes the slow infected of
  wave 5, and from wave 12 on, when the faded slow share is 0 anyway, it equals an ordinary wave at that setting,
  which is accepted. Values 1 to 99 get both. The two share functions are unchanged (their exact-0/100 returns come
  before the schedule, and at 100 the slow roll is never consulted), so the announcement and the draw agree by
  construction; a test checks the decider against the shares for every setting 0..100 and waves 1..60.
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

## The profile pool held every reloaded profile twice (found and corrected on 2026-11-20)

The Zombie waves did not run out of profiles. The game stored every profile that its backup loader reloaded twice,
and the second copy could not be used. This section replaces an earlier reading of the same measurements (below).

- **Mechanism** (decompile, `BotProfileClient` and `BotProfileBackuploader`). The client builds its loader as
  `new BotProfileBackuploader(this, LoadProfiles, LoadedByBackup)`. `LoadProfiles` already appends the server's
  answer to the pool (`_simpleProfiles.AddRange`), and `DoLoad` then hands the same profiles to the callback
  `LoadedByBackup`, which appends them again. Every profile that arrives through the loader is therefore in the pool
  twice, as the same object. The raid-start preload and the miss path of `CreateProfile` store their answer once.
  A draw takes a random matching entry and removes one copy. When the second copy is drawn, `BotCreationData.Create`
  finds the profile's id in the static `_generatedHash` (never cleared), logs `generate profile with same id` through
  `Debug.LogError` and leaves the profile out. The creation data comes back with Count 0, without an exception and
  without a request to the server. In Arena that is `Arena burst spawn: no profiles for <role> — request dropped.`
  and, for Zombies, a failed dispatch with its pause (2.1 to 2.3 s between a drop and the next spawn in the
  measurement). No such `generate profile with same id` line is in any saved BepInEx log; whether the game's
  `Debug.LogError` reaches that log at all was not checked, so its absence shows nothing either way (TRAPS.md).
- **The correction.** `ArenaMode.Client/Bots/ArenaBackupProfileDuplicatePatch.cs` is a Harmony prefix on
  `BotProfileClient.LoadedByBackup(List<Profile>)`. In the exact active Arena raid (`ArenaSession.IsActive` first,
  then `ArenaSession.IsExactActiveRaid` and the game's own `BotCreatorClient._creatorProfile` being this client) it
  replaces the callback's list (`ref List<Profile> __0`) by the entries the pool does not hold yet, compared by object
  reference (`ArenaMode.Client/Bots/BackupProfileDuplicatePolicy.cs`, `NotYetPooled`). It applies to every mode, not
  only to Zombies. It fails open: any exception is a Warning once per raid and the native call runs unchanged. It is
  not a loading-time exception: a reload that finishes before the raid start is confirmed runs as in the game.
  `Plugin.cs` enables it after `Bots.ArenaTacticalFreshGearPatch.EnablePatch();` and `ArenaRespawnDriver.cs` resets
  it after `Bots.ArenaTacticalOpening.Reset();`. Nothing else was built: no change to the `ZombieProfilePlan` targets,
  the wave policy, the alive limit or the server.
- **Side effect on the loader.** The loader's stock count (`CacheSimpleProfilesChecker`, every 30 s) counted entries,
  so it counted both copies. It now sees the real stock and orders a pair again when the real stock is more than 3
  below its target, where it used to wait until the doubled count was.
- **Native result (SPT-GPU-2, 2026-11-20, Factory, Zombies, Maximum, Arena kits, 32 alive, no player kills, the same
  method as the night before).** Run A, build `0.6.0+62de29e4`, waves of 50 growing by 20, shooter share 20: waves 1 to
  12 complete in 12.7 minutes, arrivals per second per wave next to the night before (build `0.6.0+0efe818f`, dropped
  requests in brackets):

| Wave | Size | Theme | Seconds | Run A: per second (dropped) | Night before: per second (dropped) |
| --- | --- | --- | --- | --- | --- |
| 1 | 50 | None | 17.6 | 2.78 (0) | 2.80 (0) |
| 2 | 70 | None | 31.3 | 2.23 (0) | 2.22 (0) |
| 3 | 90 | None | 35.4 | 2.54 (0) | 2.50 (0) |
| 4 | 110 | None | 44.6 | 2.47 (0) | 1.91 (6) |
| 5 | 130 | Runners | 53.7 | 2.42 (0) | 0.68 (64) |
| 6 | 150 | None | 57.9 | 2.59 (0) | 0.72 (70) |
| 7 | 170 | None | 67.0 | 2.54 (0) | 0.61 (98) |
| 8 | 190 | None | 71.3 | 2.67 (0) | not reached |
| 9 | 210 | None | 80.2 | 2.62 (0) | not reached |
| 10 | 230 | Shooters | 89.8 | 2.56 (0) | not reached |
| 11 | 250 | None | 94.1 | 2.66 (0) | not reached |
| 12 | 270 | None | 103.1 | 2.62 (0) | not reached |

  Run A had 1952 arrivals, no `no profiles` line and no `burst spawn of ... failed` line. All 17 loader reloads (1530
  profiles) read `N of N`: every profile was already in the pool, so each would have been a second copy without the
  patch. Run B (same build, waves of 60 without growth, `Zombie/ShooterPercent` 100): 9 waves, 530 arrivals, none
  dropped, 6 reloads all `N of N`. Run C (build `0.6.0+fb8bea72`, the reviewed code, waves of 50 growing by 20):
  waves 1 to 5 at 2.80, 2.23, 2.54, 2.46 and 2.41 per second, 450 arrivals, none dropped, 4 reloads all `N of N`, no
  Error line. The numbers, the frame times and the limits are in
  [the report](../reports/2026-11/GPU-ZOMBIE-PROFILE-POOL-20261120.md).
- **What the earlier reading got wrong, each statement superseded.** (1) "A spawn request for an empty pair comes
  back without a profile": the pair was not empty, the drawn entry was a second copy (that the second copies exist is
  measured: every reloaded profile was already in the pool when the callback ran; that the game rejects one when it is
  drawn is read from the decompile, its own log line for it was never seen; with the correction no request was dropped
  in three raids); a real miss asks the server at once and gets a batch. (2) "The server refills only about once a minute, the
  loader sends at most every 50 s": the 50 s gate belongs to the loader's own `Update`; `AddProfileForBackup` has no
  such gate, and a real miss asks the server at once. (3) "The loader adds about one profile per pair per
  30 s and never reaches the target": the collapse of `100 / num3` to 0 clamps every `Limit` to 1, but SPT rounds every
  requested pair up to `presetBatch`, so the clamp changes nothing on SPT. (4) "It is supply": the profiles were there,
  twice. (5) "A themed wave runs dry sooner because it draws one difficulty": that explanation rested on the supply reading
  and falls with it; the difference between the two builds in the waves of 40 (one run per build; the numbers are in
  [the acceptance report](../reports/2026-11/GPU-OPENING-ROUNDS-ACCEPTANCE-20261120.md), section "Zombies: the
  profiles of the drawn difficulties run out") is not explained and was not examined again. (6) "A
  higher alive limit asks more of the same supply": there is no supply limit to ask more of. The options (a) to (d)
  that the earlier text listed answered a supply limit that does not exist, and none of them was built.
- **What stays true.** `BotSpawner.AddToTargetBackup` is first-write-wins (`BotProfileBackuploader.cs:257-263`): a
  later call for the same (role, difficulty) is ignored, so a pool target cannot be raised mid-raid; it is set once at
  match start by `ZombieProfilePlan.BackupTargets`. The loader's `Limit` clamp described in (3) exists, only with no
  effect on SPT. A real miss goes through `BotProfileClient.CreateProfile`, asks for three and awaits; the SPT server
  generates `Math.Max(presetBatch, Limit)` per condition and returns all of them (30 for each infected role in the
  installed `bot.json`, 15 for a PMC, 45 for assault), after which `AddProfileForBackup` queues one more load. The
  loader allows at most two loads at a time, and `_loadsProcess` is not decremented when a load throws (two failed
  loads would stop the loader for the raid; the miss path keeps working). Composition-only themes need no pool change:
  a Runners wave needs the `normal` pair (present whenever the shooter share is below 100, which is where a Runners
  wave applies) and a Shooters wave the `hard` pair (present whenever it is above 0, where it applies below 100); a
  test pins it for every shooter setting. The measured numbers of the earlier runs stay what they were; only their
  attribution changed.
- **Still open.** No played match: the runs had no player kills, so the rate is bounded by the method at about 32 per
  11.6 s. One raid per configuration. The PMC modes were not run with the patch (the mechanism is the same code
  path; in the Tactical raids of the night before the PMC pool was fed by the miss path, not by the loader). Memory of
  unused profiles was not measured (2130 profiles requested in raid against 1952 arrivals in run A). And the audio
  exception seen in run A (5 of 55 mass retirements, none in runs B and C and none in the older logs) is open: the
  patch touches no audio code, but whether it needs the throughput the correction makes possible is not known (report,
  "Seen on the way"). For the offline harness of the prefix see `tools/BackupProfileDuplicateChecks`.

## The alive-limit growth (measured on 2026-11-19, not built)

Owner decision (b) was that from wave 12 the alive limit rises step by step to a ceiling **measured natively before the
number is fixed**. The measurement was made on 2026-11-19 on SPT-GPU-2 and the decision is: **the ceiling stays 32, which
is today's default; the growth is not built.** Nothing of it is in the code: no constants, no `ZombieAliveLimit`, no
per-wave limit in `ZombieWavePolicy`, no change of the native bot cap in `ArenaBotPatches.cs` (it still reads
`ZombieMaxAlive + 2 + companions`), no hint in the menu row. The full record, with both passes, the integrity notes and
where the raw data lies, is [GPU-ZOMBIE-CEILING-20261119.md](../reports/2026-11/GPU-ZOMBIE-CEILING-20261119.md).
The growth stays unbuilt because of the measured frame-time ceiling of 32. It does not depend on the profile supply:
an earlier version of this document said a higher limit asks more of the same supply, and that was wrong (the supply
was not limited, see the section above).

**Setup.** Build `0.6.0+72273354`, Maximum step, shooter share 20 %, `WaveStartSize` 200, `WaveGrowth` 0, Arena kits,
godmode and infinite ammunition, a fresh raid per cell. Two protocols: Dauerlast (the player stands, 150 s) and the
harness route (240 s). Thresholds fixed **before** the measurement: primary Dauerlast p95 at most 16.67 ms; secondary
harness-route p95(N) at most `max(16.67 ms, p95(32, same session) + 1.5 ms)`; a candidate passes only if it holds both.

**Result**, Factory, p95 frame time in ms (mean fps in brackets). The windows are the frames the stand-in collector
delivered, shifted by about 30 s from the labelled phase, and its coverage criterion was time against wall clock, not
the complete frame count the plan asked for; see "How the frame times were collected" in the report:

| alive | Dauerlast runs | harness-route runs |
|---|---|---|
| 32 | 13.49 (96), 13.68 (92) | 13.32 (101), 16.05 (88) |
| 36 | 14.25 (93), 15.58 (87), **21.20** (74) | 15.26 (86), **18.52** (77) |
| 40 | **17.54** (74), **21.24** (71) | **18.26** (76) |
| 48 | **25.60** (57) | **21.82** (64) |
| 56 (control) | **22.67** (62) | not run |

Controls (Dauerlast): Labs 32 / 36 / 40 = 14.28 / 10.88 / 13.93; Air Pit 32 / 36 / 40 = 15.12 / 13.77 / **17.37**.

**Reading.**

- 40 and 48 miss both thresholds; 40 misses again in its repeat.
- 36 holds in three runs and misses in two repeats (one per protocol), a spread of up to 7 ms between identical cells.
  Runs with many shots (automatic kit weapons, 350 to 455 shots) are the slow ones, runs with few shots the fast ones;
  the kit weapon is random per raid and could not be controlled, so the spread is part of the result. A value that
  passes or fails depending on the run is not a ceiling.
- 32 holds in every run, with little margin under heavy fire (16.05 ms in the harness run with 443 shots).
- Syncthing traffic on the host during the windows was recorded and does not explain the misses (among the Factory 36
  windows the two repeats had the least sync traffic and gave the worst 36 results).
- With a ceiling of 32 the planned rule would only raise limits that a player lowered below the default, which is the
  opposite of what that player asked for.

**Open for the owner, nothing designed or built:** whether a softer criterion is acceptable (40 alive is about 71 to
74 fps mean with p95 17.5 to 21 ms on that host), or another escalation after wave 11 is wanted that costs no frame time.

**What was considered (kept as a record).** Maximum only; from wave 12; +4 per wave; limit =
`max(setting, min(ceiling, ceil(1.5 x setting)))`, never above the slider maximum 96; a `ZombieAliveLimit(configured,
ceiling)` seam with the ceiling constant behind a one-argument overload; grown on top of `config.MaxAlive` (the live
source of the base stays the `ZombieMaxAlive(_step, ...)` line, so the `alive-limit-ignores-step` mutant keeps its
meaning); the native bot cap following the limit (otherwise it clips the growth silently: 34 at a setting of 32) with a
pin that `limit + 2 + companions` stays within the server floor of 104; no menu hint. The measurement plan was Factory at
32, 40 and 48 alive, one small ported map, 56 as the known-bad control, with the thresholds above.

## Native acceptance of stage Z1 (run on 2026-11-20)

Run on SPT-GPU-2 with the build `0.6.0+0efe818f`; the record with every figure is
[GPU-OPENING-ROUNDS-ACCEPTANCE-20261120.md](../reports/2026-11/GPU-OPENING-ROUNDS-ACCEPTANCE-20261120.md).

- **Shown:** the composition lines of waves 5 and 10 with waves of 6 (`easy 0, normal 6, hard 0 (size 6, theme
  Runners)` and `easy 0, normal 0, hard 6 (size 6, theme Shooters)`), every other wave `theme None`; the callouts
  `NEXT: ALL SHOOTERS` and `WAVE 10: ALL SHOOTERS` and the status line in screenshots (the 21 characters are not
  cut; the fading callout over a bright background is hard to read); the Runners texts as the operator's reads;
  the Reduced control (ten waves `theme None`, `max alive 24`); a shooter share of 100 (waves 5 and 10 ordinary, all
  `hard`) and of 0 (wave 5 Runners, wave 10 ordinary); the menu row `24 (setting 32, Reduced)` in a screenshot and
  the raid recipe reading it.
- **Found:** from wave 4 on the default-size waves dropped requests, and waves 5 to 7 filled at 0.6 to 0.7 infected per
  second.
  That night's attribution (a profile supply that runs out) was superseded on 2026-11-20: the cause was every reloaded
  profile being pooled twice, see "The profile pool held every reloaded profile twice". With the correction the
  default-size waves 1 to 12 filled at 2.2 to 2.8 per second without a dropped request (run A of that section), so
  wave 10 at the default size was seen on that build.
- **Not run on that night:** wave 10 at the default size (reached on 2026-11-20 with the correction), a match with
  kills at the default size (still not played), the shooters' hit rate against the player, PitFireTeam with a
  companion, the optional Crowd pass, the whole-profile comparison. The status line
  shows the match clock on both sides (`03:24 WAVE 10 (ALL SHOOTERS) | 6 LEFT | 03:24`, in a screenshot); the build
  before does the same (one read by the orchestrator), so the examples in the table above leave the leading clock
  out.

The plan the run followed:

All values with `uibridge_config_set ... persist=false` before the raid (they die with a restart of EFT; the three
sliders go through the recipe options `--zombie-max-alive/--zombie-wave-start/--zombie-wave-growth`, which write and
restore the cfg), on GPU-2, `uibridge_help topic=overview` first. The fast skipper is the stuck-wave watchdog:
godmode on, no kills; a wave of N at most the alive limit takes about `0.2 x N + StuckWaveTimeoutSeconds`.

| Goal | Settings | Time |
| --- | --- | --- |
| Waves 5 and 10 and their announcement | Maximum, ShooterPercent 20, WaveStartSize 6, WaveGrowth 0, MaxAlive 8, IntermissionSeconds 8, SpawnIntervalSeconds 0.2, StuckWaveTimeoutSeconds 4, run on to wave 11 | about 2.5 min |
| Shares over a larger sample | the same with WaveStartSize 60, MaxAlive 32 | longer |
| Reduced control | the same with Step Reduced: no theme text | 3 min |
| Full-size shooter wave and the pool (the plan of the night before: wave 10 was not reached then; run A of the pool section reached it with the correction) | defaults, StuckWaveTimeoutSeconds 5, IntermissionSeconds 2 | wave 10 after about 8 min |

What to read: the composition line per wave in the client log at Debug (`gpu-run log`): wave 5 `easy 0, normal N,
hard 0 (..., theme Runners)`, wave 10 `easy 0, normal 0, hard N (..., theme Shooters)`, wave 11 about 40 % hard at
shooter 20 (80 % at 40: the share is `round(setting x 2)`), the callouts in screenshots at the intermission start and
the wave start (fit of the 21-character text), `HudStatusLine` through the F9 panel, and for the pool the
`Arena profile build budget: N bot profiles built over F frame(s)` lines during wave 10. Acceptance: shooters' hit
rate against the player at wave 10 (the fallback above), PitFireTeam with one companion (no warning in the barrier,
one after it), shooter 0 (wave 10 ordinary) and shooter 100 (waves 5 and 10 ordinary, no warning at all), the menu row read through
`uibridge_snapshot` while F12 switches the step (`24 (setting 32, Reduced)` and back) and a screenshot of the longest
text, the whole-profile before/after comparison and the loaded hashes. The watchdog skipper invalidates reward capture
(`ArenaRewardCapture.Invalidate`); a run must not count on Zombie rewards. A dated report
`reports/2026-11/GPU-ZOMBIE-WAVES-20261119.md` records the run and what was not run.

## Where it is pinned

- `ZombieCompositionChecks`: the calendar table, the decider against both share functions for every shooter
  setting and wave, the draw buckets of waves 5 and 10 (`faded[4] = 0`, shooter share 0 at wave 5 and 100 at wave 10
  except at the exact settings), and the faults on each rule (interval, wave 0, Reduced, order, slow kept, shooters
  kept, shooters not all hard, shooters at 0, runners at 100, the decider's drops: Shooters and Runners at 100, Shooters
  at 0).
- `ZombieWavePendingChecks`: the callout sequence of waves 1 to 10, the status line, one warning per wave, shooter 0
  and 100, Reduced and a later live step, the zero-length intermission, the PitFireTeam barrier, the composition line
  (counts, once per wave, reset per wave, rejected dispatches, theme); `ZombieSoloChecks`, `ZombieTeamChecks` and
  `ZombieRecoveryChecks` compile or run the same `ZombieMode.cs`/`ZombieWavePolicy.cs`.
- `TdmMatchMenuChecks`: the row text for both steps and the settings 4, 20, 24, 25, 32, 96, the live step in both
  directions, the frozen step when armed, the cell geometry, the slider keeping the setting, no config write.
- xunit: `ArenaDifficultyPolicyTests`, `ZombieRoleMixTests`, `ZombieProfilePlanTests`.
- The profile pool correction: xunit `BackupProfileDuplicatePolicyTests` for the reference comparison; the prefix itself is
  Harmony against EFT types and was checked natively (above); for an offline harness see `tools/BackupProfileDuplicateChecks`.
- Existing expectations changed because the decided behaviour changed them: waves 5 and 10 in
  `ZombieCompositionChecks` and the `(5, 20, 10)` and `(20, 50, 40)` rows of `ArenaDifficultyPolicyTests`; on
  2026-11-19 the decider's expectation at a shooter share of 100 (xunit, `ZombieCompositionChecks`, the shooter-100 case
  of `ZombieWavePendingChecks`: no theme at all instead of "Shooters kept").
