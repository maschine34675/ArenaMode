# Native acceptance on GPU-2, 2026-11-20: Tactical opening rounds, Zombie special waves, weapon pool, collector

Acceptance of the branch `feature/opening-rounds-zombie-waves` at `0efe818f` on SPT-GPU-2 in the night of
2026-11-20 (about 01:25 to 03:05 host time, UTC+1). Contracts:
[TACTICAL-OPENING-ROUNDS-20261119.md](../../reviews/TACTICAL-OPENING-ROUNDS-20261119.md) and
[ZOMBIE-ESCALATION-20261119.md](../../reviews/ZOMBIE-ESCALATION-20261119.md).

Words used below. **Operator**: the agent that drove the game for one block. **Verifier**: a second, read-only
agent that re-derived the block's verdicts from the raw evidence. **Orchestrator**: the session that planned the
blocks, ran four more raids itself and wrote this report. **Purge**: the client drops pre-built bot profiles of one
faction that were made for another gear level, so that the next ones are generated fresh. **Skipper**: the way the
Zombie waves were passed without playing them: godmode on, no kill, and the no-kill watchdog
(`Zombie/StuckWaveTimeoutSeconds`) removes the infected that are alive, after which the wave goes on.

## Result in one table

| Block | Result |
| --- | --- |
| Solo Tactical, Maximum, rounds 1 to 4 | **pass**: levels inside 20..30, 40..50, 61..71, 61..71, `impossible`, the three buy-screen lines, the three server band lines, no warning |
| Solo Tactical, Reduced, rounds 1 to 4 | **pass**: levels inside 10..20, 22..32, 35..45, 35..45, `hard`, the same lines |
| Buy screen from round 4 on | **pass**: no line in round 5 (two reads inside the buy phase) |
| Purge of the profile pool | **pass, with a corrected expectation**: it runs when a faction is next requested, so a purge line also comes in round 4 |
| Cost of the purge | **pass**: from request to live, the slowest round with a purge was 1.8 s above round 1 of its raid; every other one was faster than round 1 |
| Zombie menu row ALIVE AT ONCE | **pass**: `24 (setting 32, Reduced)`, fits its cell; the raid recipe reads it |
| Zombie special waves 5 and 10 (waves of 6) | **pass**: at Maximum with a shooter share of 20, Runners in wave 5 and Shooters in wave 10 (composition, callouts, status line); none at Reduced; none at a share of 100; at a share of 0 only the Runners wave |
| Zombie waves at the default size | **finding, also on the build before**: the profiles of the difficulties the waves draw run out; from wave 5 on the waves fill at 0.6 to 0.7 infected per second. Wave 10 at the default size was not reached. *Superseded on 2026-11-20: the attribution (an empty pool, a refill once a minute) was wrong, every reloaded profile was pooled twice; see [GPU-ZOMBIE-PROFILE-POOL-20261120.md](GPU-ZOMBIE-PROFILE-POOL-20261120.md) and the contract section "The profile pool held every reloaded profile twice". The measured numbers stand.* |
| Gun Game with the generated weapon pool | **pass**: 155 weapons, no preset left out, a ladder of 24 rungs drawn from them, first promotion |
| Ported performance collector | **pass**: smoke run, every integrity gate, frame-count ratio exactly 1 |

## Setup

- Build: client and server `0.6.0+0efe818f`. Only `maschine-ArenaMode.Client.dll`, its PDB and
  `maschine-ArenaMode.Server.dll` were replaced; the helper executable, the whitelist file, the kit and ladder
  configuration and Redeploy Core `1.0.24+67f8303` were not touched. EFT 0.16.9.5, UiBridge 0.10.1. The
  orchestrator's `uibridge_status` after the game start: 16 `maschine-*` plugins, all identical on the VM build,
  the host disk and in the loaded process. Arena ran without a private session; the whitelist wrote five version
  notes at Info (for example `Anvil-WebOverlay: version 1.12.0 differs from reviewed 1.11.0`) and blocked nothing.
- Before the first raid the three binaries, the ArenaMode cfg and the server configuration of the VM installation
  (which Syncthing mirrors to the host) and the host's cfg and profile files were copied to
  `D:\SPT41-Backups\opening-rounds\20261120-012436\` with a SHA-256 manifest.
- Six blocks, each with an operator and then a verifier. Where a claim rests on what an operator or the
  orchestrator read from a tool answer and no file holds the raw answer, this report says so.
- Four more raids by the orchestrator: the Tactical buy screen of round 5; Zombies at the default size (the
  operator's own run of that block ended in wave 5, because godmode was lost when the wave kit was refilled; its
  waves 1 to 4 are a second sample); Zombies with waves of 40 on this build; and the same on the build installed
  before, `0.6.0+72273354`, after it had been put back.
- Test values were set with `uibridge_config_set persist=false`; the raid recipe wrote its menu choices to the cfg
  (mode, the Zombie numbers, Zombie own gear off). All of it was put back, see "Restore".
- The skipper invalidates reward capture by design and the watchdog logs two Warnings per removal; neither is a
  finding.

## Solo Tactical

Factory, squad of 5 PMCs, `Tactical/RoundSeconds` 120, `BuySeconds` 20 in the two operator raids and 25 in the
third raid. Every round was ended by killing the player with the test hook. The squad was pmcUSEC in rounds 1 and
3 and pmcBEAR in rounds 2 and 4 in all three raids.

| | Round 1 | Round 2 | Round 3 | Round 4 |
| --- | --- | --- | --- | --- |
| Maximum, expected band | 20..30 | 40..50 | 61..71 | 61..71 |
| Maximum, levels read | 25, 26, 29, 24, 30 | 45, 50, 47, 44, 43 | 71, 68, 63, 70, 67 | 64, 65, 68, 66, 68 |
| Reduced, expected band | 10..20 | 22..32 | 35..45 | 35..45 |
| Reduced, levels read | 18, 12, 18, 14, 20 | 28, 31, 29, 25, 32 | 39, 45, 41, 41, 42 | 45, 42, 45, 42, 44 |
| Buy-screen line (both steps) | `Enemy squad this round: light gear` | `Enemy squad this round: medium gear` | `Enemy squad from now on: full gear` | none (read in round 5) |

- **The level reads are the operators' notes.** Each operator read `Profile.Info.Level` and the difficulty with the
  inspector once per round and saved them as text; the raw tool answers were not saved, in either raid. What can
  be re-derived from raw logs agrees with them: the server band lines, the level range every purge line names for
  the pool it dropped, and the request lines (`requesting 5x pmcUSEC (hard)`). All 20 opponents of the Maximum raid
  were noted as `impossible`, all 20 of the Reduced raid as `hard`. The third raid read no levels.
- Server, Maximum raid (UTC): `00:32:01.387 ... bots now level 20-30; mode=tactical, step=maximum, progress=100 %,
  round=1.`, `00:33:41.541 ... 40-50 ... round=2.`, `00:34:38.150 ... 61-71 ... round=3.`, no line for round 4.
  Reduced raid: `10-20 ... step=reduced ... round=1.`, `22-32 ... round=2.`, `35-45 ... round=3.` Reports:
  `Tactical round N reported (seq N); gear level 25 / 45 / 66 / 66 / 66` and `15 / 27 / 40 / 40`. The Zombie and
  Gun Game raids of the same night sent no report and their band lines carry no `round=`.
- **When the PMC profiles are made.** The round 1 band line is written 14 s before the first report arrives
  (00:32:01 against 00:32:15), on a generation request that holds no PMC. The first PMC batches (15 pmcUSEC,
  15 pmcBEAR, 15 pmcUSEC) are generated when round 1's squad is requested (00:32:35.9 to 00:32:36.3), after the
  round 1 report. So in these raids the PMC pool was made with round 1 already reported; they cannot show that the
  default alone would have fed it.
- A report logs nothing at Info on the client; the SPT server's own request middleware logs one Information line
  per request (`/arenamode/tactical-round`), one per round.
- No hold line was written in any raid. For rounds 2 and 3 that means the acknowledgement had arrived when the
  request was due (the report goes out more than 20 s earlier); rounds 1 and 4 never wait. No Warning or Error of
  ArenaMode on client or server.
- The buy-screen lines of rounds 1 to 3 are in saved snapshots, next to `BUY · ROUND N` and the countdown. The
  operators' looks at round 4 came after its buy phase ("requesting the squad" marks the end of the buy phase, not
  its start), so the absence of the line was read by the orchestrator in the third raid, in the buy phase of
  round 5: 77 text elements at "starting in 23s" and a filtered read at "starting in 11s", none beginning with
  "Enemy squad" (the orchestrator's reads, copied to `ORCHESTRATOR-READS.md`). Round 4 and round 5 are the same
  case for the text helper.

### The purge runs per faction

| Raid | Round 2 | Round 3 | Round 4 |
| --- | --- | --- | --- |
| Maximum | 15 pmcBEAR, levels 21 to 30, gear level now 45 | 26 pmcUSEC, levels 21 to 30, now 66 | 27 pmcBEAR, levels 41 to 50, now 66 |
| Reduced | 15 pmcBEAR, levels 11 to 20, now 27 | 28 pmcUSEC, levels 11 to 20, now 40 | 27 pmcBEAR, levels 22 to 32, now 40 |
| Maximum, third raid | 15 pmcBEAR, levels 21 to 30, now 45 | 28 pmcUSEC, levels 23 to 30, now 66 | 28 pmcBEAR, levels 42 to 50, now 66 |

No purge in round 1. The first pool holds both factions at the round 1 band. Round 2 asks for BEAR and drops the
BEAR pool; round 3 asks for USEC and drops the USEC pool, which still holds round 1 profiles; round 4 asks for
BEAR again and drops what the two fresh BEAR batches of round 2 (30 profiles, 5 used) left over. From round 5 on
both pools hold the full band. The contract expected purge lines "at the stage changes only"; that sentence was
wrong and is corrected. The noted levels on the field were inside the band in every round, which is what the purge
is for.

### Cost of the purge

Seconds from "requesting the squad" to "round N live":

| Raid | Round 1 (no purge) | Round 2 | Round 3 | Round 4 |
| --- | --- | --- | --- | --- |
| Maximum | 22.4 | 19.4 | 15.7 | 8.2 |
| Reduced | 12.6 | 12.4 | 8.5 | 9.7 |
| Maximum, third raid | 10.5 | 12.2 | 7.3 | 4.2 |

The only round with a purge that was slower than round 1 of its raid is round 2 of the third raid, by 1.8 s
(12.24 against 10.45). Round 1 is not a clean baseline (it contains the first creation of the whole pool, and the
times fall from round to round for reasons other than the purge), so this bounds the cost of a purge and does not
isolate it. The eager purge at the acknowledgement stays unbuilt.

## Zombies: the menu row and the special waves

- **Menu row.** With the setting at 32: `32` at Maximum (operator's read, not stored),
  `24 (setting 32, Reduced)` after the step changed (screenshot: whole, right-aligned in the smaller font, clear
  of the label, the slider on the row below), `32` again after switching back (operator's read). The raid recipe
  at Reduced reported `MAX ALIVE 24 (setting 32, Reduced)` and started the raid; the match line said
  `max alive 24 ... difficulty reduced`.
- **Run A, Maximum, shooter share 20, waves of 6.** `Zombie wave 5 composition: easy 0, normal 6, hard 0 (size 6,
  theme Runners).`, `Zombie wave 10 composition: easy 0, normal 0, hard 6 (size 6, theme Shooters).`, waves 1 to 4,
  6 to 9 and 11 `theme None` (wave 11: `easy 0, normal 4, hard 3`, one slot drawn twice). Screenshots: callout
  `NEXT: ALL SHOOTERS` with the status line `WAVE 10 (ALL SHOOTERS) IN 11s`, and callout `WAVE 10: ALL SHOOTERS`
  with `WAVE 10 (ALL SHOOTERS) | 6 LEFT`. The longest text is not cut; the fading callout over bright metal is
  hard to read. The Runners texts (`NEXT: ALL RUNNERS`, `WAVE 5: ALL RUNNERS`, `WAVE 5 (ALL RUNNERS) IN 11s`) are
  the operator's snapshot reads, not stored.
- **Run B, Reduced.** Ten waves, all `theme None`; status `WAVE 5 | 6 LEFT`, callout `WAVE 10`.
- **Run C, shooter share 100.** Ten waves, all `easy 0, normal 0, hard 6 (size 6, theme None)`; no theme text.
- **Run D, shooter share 0.** Wave 5 `easy 0, normal 6, hard 0 (size 6, theme Runners)` with `NEXT: ALL RUNNERS`;
  wave 10 `theme None` with `hard 0`.
- The absence of theme text in runs B to D rests on the composition lines, on waits for the text that timed out and
  on snapshots during the waves; no snapshot of the first seconds of those intermissions is stored.
- Two Warnings in these raids were not the watchdog's: `Arena burst spawn: no profiles for infectedAssault —
  request dropped.` in run A (three seconds before the wave 11 line, the slot that was drawn twice) and the same
  for infectedCivil in run B (wave 8). They are the supply finding of the next section at a small scale; with
  waves of 6 the pool is planned small, and the first one came after about 60 infected.

## Zombies: the profiles of the drawn difficulties run out (also on the build before)

*Note of 2026-11-20, later the same day: the attribution in this section (an empty pool that the server refills about
once a minute) was superseded. The game's backup loader had stored every reloaded profile twice, and the second copy
could not be drawn; the pairs were not empty. The numbers below are what was measured and stand; their reading does
not. See [GPU-ZOMBIE-PROFILE-POOL-20261120.md](GPU-ZOMBIE-PROFILE-POOL-20261120.md) and the section "The profile pool
held every reloaded profile twice" of [ZOMBIE-ESCALATION-20261119.md](../../reviews/ZOMBIE-ESCALATION-20261119.md).*

Default size on this build (Maximum, shooter share 20, waves of 50 growing by 20, 32 alive, intermission 2 s,
watchdog 5 s, the skipper). "Draws" is the composition line (a slot that got no profile is drawn again);
"dropped" counts the Warning `Arena burst spawn: no profiles for <role> — request dropped.`; a wave runs from the
previous composition line to its own.

| Wave | Size | Theme | Draws easy / normal / hard | Seconds | Arrived per second | Dropped |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 50 | None | 32 / 10 / 8 | 17.5 | 2.80 | 0 |
| 2 | 70 | None | 27 / 29 / 14 | 31.5 | 2.22 | 0 |
| 3 | 90 | None | 12 / 49 / 29 | 35.9 | 2.50 | 0 |
| 4 | 110 | None | 30 / 52 / 34 | 57.7 | 1.91 | 6 |
| 5 | 130 | Runners | 0 / 194 / 0 | 192.2 | 0.68 | 64 |
| 6 | 150 | None | 22 / 137 / 61 | 209.0 | 0.72 | 70 |
| 7 | 170 | None | 29 / 156 / 83 | 279.3 | 0.61 | 98 |

The operator's aborted run of the same block gave 2.82, 2.24, 2.52 and 2.06 per second for waves 1 to 4, with 4
dropped requests in wave 4. The first dropped request came after 228 arrived infected in this run and after 235
in the operator's. The run was stopped in wave 8 after 14.4 minutes (801 infected arrived, 251 requests dropped);
wave 10 would have needed about another quarter of an hour. The frame time stayed flat (p95 8.5 to 12.8 ms per
wave), so this is supply, not load. In wave 5 the arrivals fell from about 0.8 per second in its first 100 s to
about 0.5 in its last 92 s.

What the server log shows for that raid (UTC): 12 batches of 30 infected profiles with the loading
(01:22:25, 16 s before the match start, one per pair of role and difficulty), 14 more about a minute into the
match (01:23:38 to 01:23:52), and after that 11 batches in 10 requests until 01:35:55, never less than 50 s apart
and up to 130 s. That is 37 batches or 1110 profiles: 17 `normal`, 11 `easy`, 9 `hard`. The waves drew 627 times
`normal`, 229 times `hard` and 152 times `easy`: the pairs the waves use ran out while about 180 `easy` profiles
were never used. A spawn request for an empty pair comes back without a profile (that is the Warning). That it
does not bring a fresh batch at once is an inference: 251 dropped requests stand against 10 later requests of the
client. The decompiled backup loader sends at most every 50 s, which fits the gaps; no log line ties the two
together. The contract had assumed "a miss requests a batch and costs about a third of a second"; the refill
exists, but it comes about once a minute and not per miss, and the contract is corrected.

Is that new? The same procedure with waves of 40 (no growth, watchdog 8 s), once on this build and once on the
build installed before, which has no special waves and no composition line. Both runs are cut the same way, by
arrival count: wave k is the 40 infected from arrival 40(k-1)+1 to arrival 40k, and its block runs from the
arrival that completed the wave before (so it contains that wave's removal and the intermission).

| Wave | This build `0efe818f`: per second (dropped) | Build before `72273354`: per second (dropped) |
| --- | --- | --- |
| 1 | 2.17 (0) | 2.16 (0) |
| 2 to 4 | 1.52 to 1.53 (0) | 1.53 to 1.54 (0) |
| 5 (Runners on this build) | 1.38 (1) | 1.53 (0) |
| 6 | 1.15 (4) | 1.53 (0) |
| 7 | 1.15 (4) | 1.08 (5) |
| 8 | 0.80 (11) | 0.92 (8) |
| 9 | 0.71 (14) | 0.71 (14) |
| 10 (Shooters on this build) | 0.54 (22) | 0.84 (10) |
| 11 | 0.77 (12) | 0.74 (13) |
| first dropped request | after 178 arrived | after 248 arrived |
| whole run | 440 arrived, 68 dropped | 446 arrived, 57 dropped |

One run per build, so small differences mean nothing; the skipper's own ceiling is 1.5 per second here. Both
builds fall to about half of it by wave 9. On this build the first dropped request comes earlier (a Runners wave
takes all its profiles from the `normal` pairs) and the Shooters wave 10 is the slowest block (all its profiles
from the `hard` pairs: 74.6 s against 47.6 s). The total time to wave 11 does not differ beyond what one run can
show. The spawn, pool and loader code is the same on both builds (`BurstSpawnService`, `ZombieProfilePlan`,
`ZombieRoleMix`, `ZombieWavePolicy`, the preload and load-boost patches are not in the branch's diff; `ZombieMode.cs`
and `ArenaDifficultyPolicy.cs` differ by the themes and the composition tally; the branch's new Tactical patch acts
only on pmcUSEC and pmcBEAR in solo Tactical). The Warning itself is in the host's logs of 2026-11-19 (8 and 3
times), made with the build before.

Reading, and its limits. These runs had no player kills: the watchdog removed up to 32 infected at once every few
seconds, and with nothing killed the limit of 32 was still reached again and again (25 of 185 samples in the
default-size run). What the runs show is the refill rate once the drawn pairs are empty, about 0.6 to 0.7 per
second from wave 5 on at the default size. A player who kills faster than that for long cannot keep 32 alive; that
is an extrapolation from the refill rate, not something a played match showed. Wave 5 at the default size arrived
as a trickle in its second half; wave 10 was not seen at the default size and, judging from the waves of 40, would
be slower still. Whether and how the supply is raised is the owner's decision; nothing was built. It bears on the
alive-limit growth: a higher limit asks for more profiles from the same supply.

The Shooters wave at the size of 40: 60 draws for 40 infected, p95 frame time 9.3 ms mean and 11.5 ms worst.
Whether the infected held their pistols was not read (`uibridge_bots` does not show the weapon).

## Gun Game and the weapon pool

Server start with this build: `Weapon pool generated: 155 weapons from the live item database; 9 cartridge(s) left
out as placeholders or without a handbook entry.`, 10 templates kept out by the configuration, no
`weapon preset(s) left out` warning and no `Weapon pool generation failed`. At the Gun Game start:
`Gun Game ladder drawn: 24 rungs from 155 weapons.`; the client logged `Gun Game ladder loaded: 24 rungs.` Start
weapon PP-91 Kedr, one kill (the operator teleported next to a bot and fired with the test hooks), then
`Gun Game: promoted to tier 2/24 - TOZ KS-23M 23x75mm pump-action shotgun.` No Warning or Error of ArenaMode.
No preset on this installation is defective, so the guard's new path (a preset left out, the pool kept) was not
exercised natively; it is pinned offline. The build before generates the same 155 weapons.

## The ported performance collector

`tools/ZombiePerformanceChecks`, its smoke check: one raid on Factory at 32 alive and the collector's standing
protocol (`dauerlast`: the player stands and fires at the nearest visible infected) for 60 s. `run.mjs` started
against the 0.10.1 ring and completed; `integrity.frameCountMatches` and `integrity.frameTimeMatchesWall` true,
`integrity.passed` and `qualifiedPass` true, one segment, no discontinuity, `frameCountRatio` 1 (6666 of 6666
engine frames), recorded frame time 61,667 ms against 61.674 s of wall time, no entry above 2 s and no spike above
100 ms in the run. The summary's `errors` list holds 20 `look` calls that found no visible target; there is no
capture-error record. Smoke figure only (16 shots, 2 kills, SKS): p95 11.2 ms, mean 9.3 ms. The summary has no
field named `runIntegrity` (that is the name of the function); the README's smoke section now names the real fields.

## Not run

- Tactical: both failure shapes of the report (request lost, answer lost), a round shorter than 20 s, a protected
  PitFireTeam Tactical raid, a scav squad, a custom map with other level numbers, `/status`, the gear tier the bot
  system gave the opponents, a raid in which the PMC pool is made before round 1 is reported.
- Zombies: wave 10 at the default size, a played match with kills at the default size, the shooters' hit rate, a
  PitFireTeam squad (the warning after the restore barrier), the optional Crowd pass, the whole-profile comparison
  before and after.
- The weapon-pool guard with a defective preset, and AmandsGraphics (not installed on the hosts).

## Seen on the way

- The status line shows the match clock twice (`03:24 WAVE 10 (ALL SHOOTERS) | 6 LEFT | 03:24`, in a screenshot).
  The build before does the same (`00:38 WAVE 2 | 40 LEFT | 00:38`, the orchestrator's read, copied to
  `ORCHESTRATOR-READS.md`).
- Three `NullReferenceException` entries in the game's `BotDoorOpener` (source Unity Log, the stack holds game code
  only): 01:53:35, 02:40:05 and 02:41:32. Each came 1.7 to 2.7 s after bots had been removed shortly after they
  spawned (the watchdog's removal in run C; the squad retired because the round was ended at once in rounds 1 and
  3 of the third Tactical raid). The ten older session logs on the host and the one raid on the build before hold
  none. Whether the quick removal is the trigger, and whether the branch changes anything about it, was not tested.
- `uibridge_hook action=fire` with a hold of 1500 ms answered with a timeout after 3.8 s although the shots were
  fired.

## Restore

After the last raid EFT was closed and the SPT server on the host stopped (both were off before). **The build
installed before, `0.6.0+72273354`, is back on the VM and on the host; the branch build `0.6.0+0efe818f` is not
installed anywhere.** Checked by SHA-256 against the backup, on the VM and on the host share: the client DLL, its
PDB, the server DLL, the ArenaMode cfg (`f9af829f...a32b`) and the four files of the server configuration. The
helper executable and the whitelist file were never replaced and were not hashed; Redeploy Core was never touched.
Of the host's profile files only the test profile `SPT-GPU-2` differs from the backup, because it played the
raids.

## Raw data

Git-ignored, not in the repository: `artifacts/opening-rounds-20261119/native/acceptance/` with one folder per block
(`OPERATOR-RESULT.json`, `VERIFIER-RESULT.json`, log exports, snapshots, screenshots), `zombie-full-wave10-rerun/`
(`waves-rerun.txt`, `waves-first-run.txt`, the keep-alive samples and the analysis script), `zombie-mid-wave10/`,
`zombie-mid-baseline-72273354/` (`comparison-by-arrivals.txt` and its script), `tactical-round5-buy-line/`,
`ORCHESTRATOR-READS.md` (tool answers the orchestrator copied by hand) and `REPORT-VERIFIER-*.json` (three
verifiers checked a draft of this report against the evidence; their corrections are in).
