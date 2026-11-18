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
