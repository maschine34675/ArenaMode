# Difficulty model: one step, fixed strength, scaling only in Gun Game and Zombies

2026-11-18. Contract for the change that replaces the pressure system. It supersedes
[DURATION-DIFFICULTY-REVIEW.md](DURATION-DIFFICULTY-REVIEW.md) and the proposal in
FUTURE-WORK.md ("Difficulty controls in the native game menu").

## Direction

The maintainer's decision of 2026-11-18: the human is superior to the bots anyway, so the
default is the highest difficulty, with one option for somewhat less of everything. Scaling
over time only makes sense in Gun Game and in Zombies. Four further decisions of the same
day: Gun Game scales by time, Tactical gets no build-up (round one is measured natively and
answered with the starting money if needed), protected PitFireTeam raids keep opponent gear
relative to the real profile, and Zombies keep 32 infected from wave one and escalate
through their composition.

## The setting

`[Difficulty] Step`, an enum with two values, `Maximum` (default) and `Reduced`. It is a
BepInEx setting with an F12 row; the F9 panel shows the frozen value read-only. It is global,
not per mode.

The value is read once on the arm path, sent to the server as the optional `difficulty`
field of the raid-start request (`"maximum"` or `"reduced"`), stored in the server's session
entry, echoed in the answer and compared by the client like `participants`. The client keeps
the same value in `ArenaSession` until the raid ends. Every consumer reads the frozen value,
never the live setting, so the AI level and the gear band of one raid always come from the
same step. A request without the field means `Maximum`; an unknown value is refused like an
unknown hostility.

## The policy

`ArenaMode.Shared/ArenaDifficultyPolicy.cs` is a pure static table, linked into client,
server and tests. It answers, for a step and a progress value between 0 and 1:

| Lever | Maximum | Reduced |
| --- | --- | --- |
| PMC level | impossible | hard |
| Scav level (also the Labyrinth boss slot) | hard | normal |
| Gear level | the map's `botTierEndLevel` (default 66) | 60 % of it, never below `botTierStartLevel` (default 40) |
| PMC bonus over `PmcFloor` in Deathmatch and Gun Game | 4 | 2 |
| Legacy refill cooldown factor | 0.5 | 0.75 |
| Bot speed | the user's multipliers as set (default 1.5) | half of the surplus over 1.0 (default 1.25) |
| Position reports (`HuntPlayer` sweep interval) | as configured | interval x 1.5 |

Progress is 1 in Deathmatch, Team Deathmatch, Domination and Tactical (solo and with
PitFireTeam): every value above holds from the first spawn request. Nothing in these modes
reads a clock or the kill streak any more.

The levels are table entries, not literals at the call sites. Whether `impossible` is the
harder PMC row under SAIN is measured natively (see "Native measurements"); the table is the
one place to change if `hard` turns out harder.

`PmcFloor` keeps its meaning, including 0 = no forced PMC share. The bot speed settings stay
user settings and the Fast Bots mutator stays the switch; the step only scales the surplus.
The step applies to every bot Arena boosts, so Team Deathmatch and Domination stay even and
PitFireTeam followers move with the same factor. Hard-coded agility, the Team Deathmatch
awareness refresh, the Tactical halving of the sweep interval and Deathmatch `FreeForAll`
are not part of the step.

## Gun Game

Progress is elapsed time over the map's `botTierRampMinutes` (default 15), one ramp for the
AI level, the PMC bonus and the gear:

- Gear level runs from `botTierStartLevel` (default 1) to the step's gear level, on the
  server's wall clock since the raid went live, as today.
- The client reads the same ramp length from the per-map server configuration it already
  fetches and uses it with its match clock for the PMC level (hard until progress 0.8, then
  the step's PMC level), the scav level (normal until 0.5, then the step's scav level) and
  the PMC bonus (rounded progress x the step's bonus).
- The kill streak no longer feeds anything. A completed ladder changes nothing: progress
  stays where the clock is.

`PressureRampMinutes` (12 minutes, client only) is gone, so the two unsynchronised clocks of
the old model become one ramp length.

## Protected PitFireTeam raids

Unchanged on the server: opponent gear follows SPT's native profile-relative level, and the
APBS adapter stays out. The step still decides the opponents' AI level, bot speed and
position reports. Followers stay at the level PitFireTeam requires.

## Zombies

The step does not touch `MaxAlive`, wave sizes or the spawn interval at `Maximum`; 32 alive
from wave one stays. Escalation comes from the composition:

- `Maximum`: the melee slow share keeps the opening 75/50/25 % and the 40/20/30 rhythm, but
  fades by 5 percentage points per wave from wave 4 on and never below 0 (35, 10, 15, 20, 0,
  0, 5, 0, ...), so the horde turns all-fast. The shooter share grows by a tenth of the
  configured share per wave, up to twice the configured share and never above 100 %; exactly
  0 % and 100 % stay exact.
- `Reduced`: the composition of 2026-11-15 without the fade, a flat shooter share, and at most
  24 infected alive (the lower of the setting and 24).

Nothing else of [ZOMBIE-FLOW-20261115.md](ZOMBIE-FLOW-20261115.md) changes: role weights,
quotas, recovery, planning budget, no health or damage scaling.

## Removed

- Settings `ScaleDifficulty`, `PressureRampMinutes`, `PressureStreakPercent`,
  `PressurePmcBonus`, `MaxBotDifficulty`, their F12 rows and the F9 toggle. One-time
  migration through a revision marker: a saved `ScaleDifficulty = false` or a saved
  `MaxBotDifficulty` below `impossible` selects `Reduced`.
- The kill-streak term and the time term outside Gun Game.
- The match-clock channel of timed Team Deathmatch and Domination: the three hooks of
  `ArenaBotDifficultyClockPatch`, the `TimedWave` request type, the server's wire-clock
  branch and the readiness gate in the prewarm. With a fixed band a pooled profile is as good
  as a fresh one. The prewarm then covers exactly the PMC levels the step can request. The
  raid-start preload stays skipped for Team Deathmatch and Domination until loading time has
  been measured; only its justification is rewritten.
- Server: `botTierStartLevel` and `botTierRampMinutes` act only in Gun Game. `botTierEndLevel`
  is the `Maximum` gear level of the map. `scaleBotTier = false` stays the opt-out to native
  levels.

## What must hold

1. In Deathmatch, Team Deathmatch, Domination and Tactical the requested AI level, the PMC
   share and the gear band are the same at second 0 and at any later time, with any kill
   streak, and equal the table for the frozen step.
2. The band of the first bot request of a raid already is the fixed band; if early requests
   arrive before the session counts as live, the band must still apply to them.
3. The client's AI level and the server's band of one raid come from the same step; a
   setting changed during the raid changes neither.
4. Gun Game: progress 0 gives today's opening, progress 1 gives the step's values, and
   client and server use the same ramp length.
5. Protected PitFireTeam raids write no band and no APBS permit, at either step.
6. Zombies at `Maximum` never spawn fewer simultaneous infected than v0.6.0 allowed, and
   0 % and 100 % shooters stay exact at every wave.
7. Normal raids are unaffected: every reader stays behind the active-session gate.
8. An old saved configuration loads without a warning and without changing any other value.

## Native measurements before the numbers are final

- `impossible` against `hard` under SAIN 4.5.1 with identical gear: decides the PMC row of
  `Maximum`.
- Tactical solo, round one: the 250,000 starting money against five opponents at the
  `Maximum` band.
- The weakest stock kits in Deathmatch and Team Deathmatch against `Maximum`.
- Whether the native raid timer of the map ends the raid before `botTierRampMinutes`
  (ArenaMapPort maps list 15 minutes): decides whether the Gun Game ramp must be shorter.

## Verification

A new `ArenaDifficultyPolicyTests` pins the table and the Gun Game curve. The server tests
pin the fixed band per mode and step, the Gun Game ramp, the protected bypass, the opt-out
and the arm field (echo, replay, reset). `tools/DurationDifficultyChecks` becomes the harness
for the client side: fixed values regardless of time and streak, the Gun Game curve, the
frozen step, each with a fault that must fail. Harnesses that stub the old pressure API are
adapted. The native acceptance and the measurements above are recorded in
`docs/reports/2026-11/`.

## Implementation notes

Written on 2026-11-18 from the code of `feature/difficulty-model`. Everything here comes from
source, offline harnesses and xunit; nothing has run in a native raid yet. Where the code does
not do what the text above says, the note is marked **Correction** or **Not implemented**; the
decisions in "Direction" are untouched.

- **Where the step is frozen.** Client: `ArenaLocalRaidStartedPatch` reads
  `[Difficulty] Step` once on the arm path, next to the requested mode and after the settings
  sync, and passes the same value to the arm request and to `ArenaSession.MarkArmed`.
  `ArenaSession.Difficulty` (nullable) holds it and is cleared wherever the participants are
  cleared; `ArenaDifficulty.Step` reads it and falls back to the live setting only when no
  raid is armed or active (the F9 line, harnesses). Server: the router validates `difficulty`
  before any side effect, `ArenaSessionService` stores it in the session entry at arm, resets
  it at the four places that reset the participants, and includes it in the replay equality, the
  answer, the replay answer and `/status`. `GetDifficulty` and `TryGetBotTierContext` read it.
  The client compares the echo like `participants` and refuses the arm on a mismatch, which is
  also what an older server (no echo) produces, so client and server must be updated together.
- **Only the step is frozen on the server.** `botTierStartLevel`, `botTierEndLevel` and
  `botTierRampMinutes` are read from the live map configuration on every request, as before. The
  client receives the ramp length in its per-map configuration JSON (fallback 15 minutes) and
  reads it when it needs it. A configuration changed during a raid could therefore move the two
  sides apart in Gun Game; freezing them in the session entry is the remedy if that matters.
  The two clocks also have different origins: the server counts from the local-start prefix,
  the client from the match creation, so in Gun Game the gear can be a few percent of the
  ramp ahead of the AI level. Both are deferred: a shared origin needs a protocol field and a
  native timing measurement (HANDOFF, open work).
- **How early requests get the band.** The raid-start bot preload is sent after
  `/client/match/local/start` has been answered, and `ArenaStartLocalRaidPatch` moves the
  session to InRaid in that request's prefix, so no bot request of an armed raid can arrive
  before the session counts as live. The server therefore added no band outside the exact
  session binding. The first request sees elapsed time of about the loading time. Outside
  Gun Game that no longer matters because the band is fixed; Gun Game opens near the start
  level. This is read from the decompiled client and the patch order, not confirmed in a log.
  Requests from other sessions, a protected PitFireTeam raid and `scaleBotTier = false` stay
  native. An armed session that is not yet InRaid also stays native; a test pins that, so a
  wrong ordering would show as native-level profiles and not as a silent guess. The first
  native raid must confirm the order (first `GenerateBotWave` after the local-start prefix).
- **Gear band.** `ArenaDifficultyPolicy.GearLevelAt` computes it. The end level is clamped to
  79 (`MaxGearLevel`) first and Reduced is 60 % of the clamped value, rounded half away from
  zero, never below the start level. The Gun Game ramp rounds with `Math.Round` default
  (half to even), so at end level 66 the midpoint is 34 at Maximum and 20 at Reduced.
  **Correction:** the table row and the Reduced description above write "`botTierStartLevel`
  (default 40)". The default start level is 1 (`ArenaModeConfig.BotTierStartLevel`). With the
  shipped defaults the Reduced gear level is round(0.6 x 66) = 40, which is where the 40 came
  from; with a different start level the floor is that start level.
- **Team DM and Domination.** Their requests are ordinary requests with the fixed band. The
  server's request-size cap for finite matches is gone together with the clock, so such a
  request now generates the native reserve (for example 15 profiles) instead of one. The
  contract removed the cap; its latency has not been measured (see below).
- **Prewarm pairs.** `ArenaDifficultyPolicy.PrewarmLevels(active, mode, tacticalSquadIsPmc,
  step)` decides what `PrewarmPmcProfiles` backs up, as PMC levels:

  | Mode | Maximum | Reduced |
  | --- | --- | --- |
  | Deathmatch, Team DM, Domination, Tactical with a PMC squad | impossible | hard |
  | Gun Game | hard | hard |
  | Zombies, Tactical with a Scav squad, no active session | none | none |

  **Correction:** the text above says the prewarm covers exactly the PMC levels the step can
  request. `PmcRequestLevels` does list them (Gun Game at Maximum: hard and impossible), but
  the prewarm warms only the opening level in Gun Game: the server builds a profile with the
  band of the moment it is asked. The step's level is requested fresh once progress reaches
  0.8. **Second correction (2026-11-19):** that was not enough. The opening prewarm is not the
  only pooled gear (see "Stale pooled gear in Gun Game" below). The Tactical squad setting is read live. The prewarm has no readiness gate and does
  nothing outside an active session.
- **Preload skip.** The raid-start preload of Team DM and Domination is still skipped
  (`SkipsProfilePreload`); only its comments changed. Loading time is not measured.
- **Migration marker.** `[Difficulty] DifficultyRevision` (hidden in the F12 menu, current
  value 1) makes the migration run once. The five retired entries are no longer bound, so
  BepInEx neither warns about them nor rewrites them; their lines stay in the `.cfg` file
  because BepInEx 5.4.23 keeps unbound lines. Its orphaned-entry list is internal, so
  `ArenaDifficultyMigration.StepFromConfigText` reads the `[Difficulty]` section from the file
  text. A saved `ScaleDifficulty = false`, or `MaxBotDifficulty` equal to easy, normal or hard,
  selects Reduced and writes an Info line; an unparseable `MaxBotDifficulty` keeps Maximum. If
  the file cannot be read the revision is not recorded, so the next start retries. The
  parser is covered by `ArenaDifficultyMigrationTests`; `tools/AreaConfigChecks` runs it
  against a real BepInEx `ConfigFile` (`ConfigFile.Save` writes its orphaned entries back, so
  the retired lines survive the Binds that save before the read), with a mutant that disables
  the migration.
- **Speed and reports.** The user's multiplier is capped at 1.5 first and only then is the
  surplus scaled, so Reduced with a setting of 99 gives 1.25. The sweep interval is the per-map
  value, then the Tactical halving, then the step's factor.
- **Stale pooled gear in Gun Game (2026-11-19).** A bot's gear is fixed when its profile is
  generated, and the client serves a spawn from the pool of already generated profiles
  (`BotProfileClient._simpleProfiles`) before it asks the server. SPT rounds every requested
  (role, difficulty) pair up to `presetBatch` (15 for a PMC), the raid-start preload fills the
  pool and the backup loader refills it, so one request yields a batch of one moment. Measured
  natively (dev build 6a2865f7, Gun Game on arena_bay5, Maximum, 8 participants): 150 profiles
  were generated at second 0, every PMC of the first 9 minutes had level 2..7 (expected centre
  at minute 9: 41); bots of minute 11 had 37..50; the first impossible batch was generated at
  minute 12.95 (levels 53..58) and fed every spawn of minutes 15..20 (levels 49..63, expected
  61..71). The AI level switch, the build-up percentage and the raid timer were correct. The
  same staleness existed in v0.6.0, where the removed clock patch solved it only for timed
  Team Deathmatch. Fix: `ArenaGunGameFreshGearPatch`, a prefix on
  `BotProfileClient.CreateProfile(BotCreationData, CancellationToken, bool)`. Before a PMC
  demand (pmcUSEC, pmcBEAR) in the exact active Arena raid, with `scaleBotTier` on and the
  mode Gun Game, it removes the unused pool entries of exactly the demanded (role, difficulty)
  whose level is below `current - 5` (`ArenaDifficultyPolicy.GunGameStaleGearLevels`,
  `IsGunGameGearStale`); `current` is `ArenaDifficulty.CurrentGunGameGearLevel`, the policy's
  ramp at the frozen step over the map's `botTierStartLevel`, `botTierEndLevel` and
  `botTierRampMinutes`. The native miss path then asks the server for a fresh batch at the
  current band. The 5 levels: the server band is centre +/-5 (`Spread` in
  `ArenaBotTierScalePatch`), so a pooled profile below centre - 5 is below anything the server
  would generate now. The first slack, 10, was measured natively (arena_bay5, Maximum, 8
  participants, 2026-11-19): 19 purge lines, no warning; the opening profiles (level 3..11)
  were served until about minute 4.6, from minute 6 to 12 the sampled bots were 13 to 18
  levels below the centre of the sampling minute, and after the end of the ramp (minutes 15.5
  to 18.4, 32 bots) the levels were 55..65 against a band of 61..71, because profiles
  generated shortly before the end stay fresh under a slack of 10 and feed the spawns for
  minutes. With 5, every pooled profile below the end level - 5 is dropped after the ramp and
  new bots land in the step's band like in the fixed modes. A fresh batch is not stale on
  arrival as long as the server's clock is not behind the client's (the server counts from
  the local-start request, the client from the match start; natively the first fresh profile
  was above the client's centre); the 30-second limit below bounds the cost if it is. Per (role, difficulty) at most one purge every 30 seconds
  of unscaled time (a server that answers below the client's expectation, for example a
  configuration edited during the raid, costs one batch per 30 seconds, not one per spawn);
  the state is per world. One Debug line per purge; an exception keeps the native behaviour
  and warns once per raid. **Not covered:** Scav roles (their gear barely follows the level,
  their batch is 45), profiles that are already assigned to a bot, other difficulties, the
  backup loader and the request itself; and a pooled profile can still sit up to 5 levels
  below the centre (the lower edge of the band). Checks:
  `GunGameFreshGearChecks` (the real patch against host stubs, 53 mutants) and
  `ArenaDifficultyPolicyTests`. Slack 5 has not run natively yet; a native Gun Game raid must show PMC levels in the
  band throughout and 61..71 after the end of the ramp.
- **Gun Game progress on the client** is `ArenaMatchService.Current.Elapsed` over the ramp
  minutes of `GameWorld.LocationId` (the armed map if no world exists yet); without a match or
  on an exception it counts as 0. The refill wait factor is not ramped in Gun Game: it applies
  from the first request.
- **Settings during a raid.** Changing the step in F12 or the web page during an armed or
  active raid changes nothing for that raid; the F9 panel cannot change it either.
- **Zombies.** Implemented in the final fix pass. `ArenaDifficultyPolicy.ZombieSlowPercent`,
  `ZombieShooterPercent` and `ZombieMaxAlive` hold the numbers. The fade is 5 points times
  (wave - 3) from wave 4 on, subtracted from the rhythm of 2026-11-15 and floored at 0. The
  shooter share is `round(configured x (1 + min(1, 0.1 x (wave - 1))))`, never above 100 %,
  and exact at 0 and 100. `ZombieMode` freezes the step at match start (from the armed
  session) and passes it to every draw; Reduced lowers `_config.MaxAlive` to
  `min(setting, 24)`. The native bot cap of the raid is still computed from the setting
  (`ArenaBotPatches`, a harmless over-estimate at Reduced). The opening preload and the backup
  targets are wave 1 and every possible pair, which are the same at both steps. **Correction:**
  the HuntPlayer sweep interval of the table is not applied in Zombies
  (`ArenaDifficultyPolicy.ReportStep`): the Zombie section names no report change, and the
  horde has its own hunt nudge.
- **Harnesses.** `GunGameFreshGearChecks` runs the pool purge; `DurationDifficultyChecks` pins the client reader, the prewarm list and the
  frozen step with mutants; `BotMovementChecks` pins the halved speed surplus. The xunit
  tests `ArenaDifficultyPolicyTests`, `BotTierScalePatchTests` (partial files
  `BotTierDifficultyBandTests` and `BotTierDurationTests`) and `ArenaAreaServerTests` (partial
  file `DifficultyArmTests`) pin the table, the band, the absence of any clock and the arm field.
  The skippable APBS integration test needs `ARENA_APBS_TEST_ROOT` and is run natively by
  `tools/ApbsCompatibilityChecks/Verify.ps1`.
- **Latency of the full reserve.** In addition to the four measurements above: the server
  generation time of a Team DM or Domination wave request now that the one-profile cap is
  gone, in a 20-minute match.
