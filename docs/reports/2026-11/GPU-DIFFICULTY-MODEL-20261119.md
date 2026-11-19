# Difficulty model: native acceptance and measurements on GPU-2

2026-11-18 evening to 2026-11-19 night. Evidence for
[DIFFICULTY-MODEL-20261118.md](../../reviews/DIFFICULTY-MODEL-20261118.md). This is a dated
record of what was run on which build; the source decides what the current build does.

## Build and host

- SPT-GPU-2, EFT 0.16.9.5, SPT 4.1.6, SAIN 4.5.1, BigBrain 1.5.0, PitFireTeam 1.1.1, UiBridge 0.10.1.
- Branch `feature/difficulty-model`. First pass on client and server `0.6.0+6a2865f7`, the second
  Gun Game pass and the speed line on `0.6.0+2ba01386`, the third Gun Game pass on
  `0.6.0+72273354`. Dev install of the two DLLs only
  (no-deploy build, copied by hand); Redeploy.Core `1.0.24+67f8303` byte-identical before and
  after. Loaded = host disk = VM build for all 16 `maschine-*` plugins at every start.
- Test values were set with `uibridge_config_set persist=false`; the server configuration was
  not edited. Raw notes, value dumps and scenarios: `artifacts/difficulty-native-20261118/`
  (gitignored). Each subfolder holds the operator's closing message
  (`operator-final-message.md`); figures that are only there or in `orchestrator-notes.md`
  (build checks, the first raid, the Domination log) were not written to a data file.

## Old configuration

The installed `.cfg` held `ScaleDifficulty = true` and `MaxBotDifficulty = impossible`. The
first start logged no warning and no migration line, `Difficulty › Step` read `Maximum`,
`DifficultyRevision` read 1 and is hidden. A diff of the `.cfg` against the copy taken before
the install shows the `[Difficulty]` section replaced (new entry, marker, the five old values
kept as unbound lines) and nothing else changed by the plugin (`GameMode` was written by the
raid recipe).

## Acceptance per mode

All on Factory unless named, step set before the raid. "Level" is `Profile.Info.Level` of the
bots read in the raid, "AI" their `BotDifficulty`.

| Raid | Expected | Observed |
| --- | --- | --- |
| Tactical solo, Maximum, squad 1 (two raids) | first opponent `impossible`, level 61..71 | request line `(impossible)`; the first opponents had level 70 (live 16 s after the session went active) and 63 |
| Tactical solo, Reduced, squad 1 | `hard`, level 35..45 | request line `(hard)`, level 38 |
| Deathmatch, Maximum, 16 participants | PMC `impossible`, scav `hard`, 12 PMC + 3 scav, 61..71 | PMC `impossible` 62..71, scav `hard` 65..70, 12 + 3, same two minutes later |
| Deathmatch, Reduced, 16 participants | PMC `hard`, scav `normal`, 10 PMC + 5 scav, 35..45 | PMC `hard` 39..45, scav `normal` 39..44, 10 + 5 |
| same raid, step set to Maximum after 60 s | nothing changes | 15 later bots still `hard` / `normal` at 39..45; in a second Reduced raid the F9 line still read `Reduced` after the same switch |
| Team Deathmatch, Maximum, 4v4, 5 min | all `impossible`, 61..71 | 15 bots `impossible` 63..71; 7 of 7 alive 14.7 s after the session went active and at the one later poll; 8 replacements dispatched |
| Domination, Maximum, 4v4 | all `impossible`, 61..71 | 7 bots `impossible` 64..71; 7 of 7 after 30 s, 5 alive at one later poll with two replacements admitted two seconds after it; the client log shows 0.2 to 1.2 s of preparation per replacement |
| Zombies, Maximum | limit 32 | start line `max alive 32, ... difficulty maximum`; 32 alive from the third poll (about half a minute in) and held; 110.7 fps, mean 9.0 ms, p95 9.8 ms |
| Zombies, Reduced | limit 24 | start line `max alive 24, ... difficulty reduced`; never above 24 in 90 s; 119.3 fps, p95 8.7 ms |
| Tactical with one PitFireTeam companion, Maximum | AI by the step, gear relative to the profile | request `5x pmcBEAR (impossible)`; opponents level 3..10 with the profile at level 1; companion `hard` |
| Ordinary PMC raid, bots low | no Arena | no arm, no session, native HUD; bots level 5..10, native mixed AI levels; left by a real extraction |

The operators read the ArenaMode log source at warning level for the six raids from Deathmatch
to Zombies, the PitFireTeam raid and the three Gun Game passes: no warning, no error. The
Tactical, speed and ordinary raids were not checked for it line by line (their raid result
lines reported no error). The server answered the bot requests of the
Team Deathmatch start in 0 to 2 ms; the request-size cap that went with the match clock was not
missed in these two short raids (no long raid with a full reserve per request was run).

Bot speed (first build, Deathmatch with 12 participants, all against the player), four
20-second observations per step (bodies sampled every 250 ms, 44 bot observations each): the 90th percentile of the per-bot top speed was 8.49 m/s at Maximum
and 7.06 m/s at Reduced (ratio 1.20, the expected 1.5 : 1.25). The medians (7.80 and 4.64)
are dominated by how many bots happened to sprint in a window and are not evidence either way.
The Debug line of the first build printed the settings (`walk x1.5` in a Reduced raid); the
second build prints the effective factors: `walk x1.25, sprint x1.25 (difficulty reduced,
settings x1.5 / x1.5)` in a Reduced raid and `walk x1.5, sprint x1.5 (difficulty maximum, ...)`
at Maximum.

The F9 panel shows `Difficulty <step>` under "Your details", below the scoreboard: visible
without scrolling in a small match, below the visible part once the scoreboard is long. The
first Deathmatch raid (16 participants) was read without scrolling and showed no such line;
the later raids found it after scrolling (`Maximum`, `Reduced`, and in Gun Game with the
build-up percentage).

The raid timer of Gun Game on Bay 5 read 3537 s after the first minute and 2397 s at minute
20: ArenaMode's 60 minutes apply on a ported map, not the map's own 15.

## `impossible` against `hard` (PMC row)

One Tactical opponent per step, both with the SAIN personality GigaChad; every scalar the game
applied was read through a generated scenario: 838 EFT `Settings.FileSettings` values, 102
SAIN file settings, SAIN difficulty, profile and personality groups, 1093 in all. 27 differ;
without names, level, weapon and role, these remain:

| Value | impossible | hard |
| --- | --- | --- |
| SAIN `VisibleDistance` | 275 | 250 |
| SAIN `VisibleAngle` | 180 | 170 |
| SAIN `AimForHeadChance` | 50 | 33 |
| SAIN `STRAFE_SPEED` | 1.0 | 0.8 |
| SAIN `DifficultyModifier` / `PowerLevel` | 1.75 / 325 | 1.5 / 295 |
| EFT `MAX_DIST_TO_PERSUE_AXEMAN` / `..RUN_PERSUE..` | 78.7 / 131.2 | 33.9 / 56.5 |
| EFT `TIME_TO_FORGOR_ABOUT_ENEMY_SEC` (also SAIN `ForgetEnemyTime`) | 38.3 | 62.9 |
| SAIN `TimeBeforeSearch` | 6.06 | 5.52 |

Every EFT aiming, shooting, scattering, look, hearing and grenade value and all 44 SAIN
difficulty-modifier fields are identical. `impossible` is the stronger row where they differ,
except that it forgets an enemy sooner and waits a little longer before it searches. Three
more differing values say nothing clear (the square root of the modifier, `HoldGroundDelay`
1.21 against 1.29, `PercentageBeforeExtract`). One bot per step; the two also differed in
side, level and weapon. The PMC row of `Maximum` stays `impossible`. A
time-to-death comparison was dropped after one sample (28 s, a shotgun at 12 m): weapon and
line of sight dominate it.

## `hard` against `normal` (scav row)

One scav squad member per step. SAIN attaches no `BotComponent` to these scavs, so only the 838
EFT values were read; 27 differ. `hard` aims harder (`HARD_AIM_CHANCE_100` 80 against 50,
tighter close scatter, less cover penalty), sees further (137 m / 75 degrees against 129 / 70),
hears better and throws grenades more precisely; one hearing dispersion value is worse. One
bot per step, with different weapons and levels. The scav row stays. The server band reaches scavs too (level 70 and 39).

## Stock kits against Maximum bots

Deathmatch, five opponents, aim tracking on chest or head, real shots counted until the death
record. The operator had to teleport next to a bot for every target. Small and uneven samples
(4 to 9 valid targets per kit; many "chest" kills were credited to the head), so this shows
an order of magnitude, not rates. Neither a Tactical round with the first buy nor Team
Deathmatch was played for it:

| Kit (price, ammunition damage/penetration) | Rounds to kill, chest-aimed | Head-aimed |
| --- | --- | --- |
| drifter (180k, .366 EKO 73/30, single fire) | 1 to 17, median 1.5, n=6 | 1, 6, 17 |
| spartan (default kit, M855A1 49/44) | 1 to 7, n=6; the three confirmed chest kills 7, 6, 2 | 1, 1 |
| vanguard (214k, BS 45/54) | 1, 4, 7 | 2 (after 16 uncounted rounds at the same bot), 14 |
| reaper (555k, MAI AP 53/58, reference) | 1, 4, 6 | 1 |

Six further engagements are not in the table: the target left sight, the shots hit geometry or
a round reset intervened (8 to 40 rounds without a kill). No kit failed on a target that
stayed in view, and the reference kit was not visibly better in this sample. The 250,000
roubles of Tactical's first buy phase were left as they are.

## Gun Game ramp

First pass (`6a2865f7`), Bay 5, 8 participants, step Maximum; all bots were PMCs:

| Minute | Expected centre | Levels seen | AI |
| --- | --- | --- | --- |
| 0.4 | 3 | 2..7 | hard |
| 3.2 | 15 | 2..7 | hard |
| 5.9 | 27 | 2..7 | hard |
| 9.2 | 41 | 2..7, one at 46 | hard |
| 11.0 | 49 | 37..50 | hard |
| 13.0 | 57 | 43..50 (`hard`), 53..58 (`impossible`) | switch after minute 12 |
| 15.4 to 20.0 | 66 | 49..63 | impossible |

The AI switch and the F9 line (`Maximum, build-up 3 %`, 41 %, 88 %, 100 %) were right. The gear
was not: 150 profiles were generated in the first seconds and served for nine minutes, later
batches of 15 were 1 to 3.5 minutes old when used, and the last batch before the end of the
ramp fed every spawn afterwards (up to seven minutes old). The profile ids carry their
generation time, which is how the age was read. By the code, v0.6.0 served Deathmatch and Gun
Game from the same pool; that was not measured.

Second pass (`2ba01386`): `ArenaGunGameFreshGearPatch` drops unused pooled PMC profiles of
the demanded pair that are more than 10 levels below the current gear level. 19 purge lines,
no warning. Bots of level 3..11 spawned until about minute 4.6; from minute 6 to 12 the bots
read were 13 to 18 levels below the centre of the minute they were read in (that figure
includes the time a bot had already lived); after the end of the ramp 32 bots read at minutes
15.5 to 18.4 were at 55..64, because profiles generated shortly before the end still counted
as fresh. Mean frame time 8.4 ms, single frames of 49 to 95 ms.

Third pass (`72273354`, slack 5 = the server's band half-width), same raid; 137 arrivals in 20
minutes, 132 joined to their level and to their spawn time from the life-event ledger (a
selection of the minute buckets; all of them are in the operator's message and in
`bots-joined.csv`):

| Spawn minute | Bots | Level min / median / max | Level minus centre at spawn | AI |
| --- | --- | --- | --- | --- |
| 0-1 | 11 | 2 / 5 / 7 | -0.9 .. +4.2 | hard |
| 2-3 | 7 | 7 / 8 / 10 | -6.5 .. -1.8 | hard |
| 3-4 | 6 | 8 / 18.5 / 21 | -6.8 .. +3.9 | hard |
| 5-6 | 7 | 19 / 20 / 21 | -6.5 .. -2.8 | hard |
| 8-9 | 5 | 33 / 34 / 35 | -4.8 .. -2.1 | hard |
| 11-12 | 10 | 44 / 46 / 50 | -6.0 .. -1.7 | hard |
| 12-13 | 3 | 55 / 57 / 61 | +0.3 .. +5.0 | impossible |
| 14-15 | 4 | 58 / 61 / 62 | -4.8 .. -1.5 | impossible |
| 15-16 | 4 | 61 / 65.5 / 71 | -5 .. +5 | impossible |
| 16-20 | 19 | 61 .. 71 | -5 .. +5 | impossible |

The last bot with an opening level (8) spawned at minute 3.2. From then on no bot spawned more
than 6.8 levels below the centre of its spawn moment (the largest deviation upwards was 6.2),
and from minute 15 every new bot was in 61..71. The AI level switched between minute 11.99 and 12.1. 40 purge lines from minute 1.3 to
15.0 (lowest dropped level rising from 1 to 58), none afterwards, no warning. Seven bots alive
at five of six checks (six once, between a death and its replacement). Frame time 8.4 to 8.7 ms
mean throughout; single frames of 50 to 63 ms occurred with and without a purge nearby, as in
the second pass (the alive counts and frame times are in the operator's message only).

## What was not run

- Zombie waves beyond the first: the slow-share fade and the shooter growth are covered by
  `ZombieCompositionChecks` only.
- Gun Game at Reduced, and the scav switch at half of the Gun Game ramp (no scav spawned in
  the Gun Game raids).
- A long Team Deathmatch or Domination raid with the full reserve per request, and loading
  time with the raid-start preload left in.
- The step's position-report factor was not timed; it is pinned by `DurationDifficultyChecks`.
- No second host, no Fika.

## State left behind

EFT closed and the SPT server stopped on GPU-2, as before the run. Test values were never
saved. Menu choices the raid recipe wrote were put back: `Match/GameMode` deathmatch and
`Deathmatch/FreeForAll` on through the bridge, and the hidden per-map participant counts
(Factory solo 10, Bay 5 solo 6) in the file with the game closed. Compared with the copy
taken before the install, the `.cfg` differs only by the two new `[Difficulty]` entries; the
server `config.json` is byte-identical. The PitFireTeam raid group is empty as before. The
test profile played the raids listed here, the ordinary one included. The dev build
`72273354` (client and server DLL) stays installed on the VM and, through the folder sync, on
both GPU hosts.

## Offline verification of the same commit

`tools/Run-ArenaVerification.ps1 -All -Parallel 4` on `72273354`: 163 passed, 0 failed
(xunit 4066 total, 4063 passed, 3 environment-gated skips), 8542 s, run while the third Gun
Game pass was played.
