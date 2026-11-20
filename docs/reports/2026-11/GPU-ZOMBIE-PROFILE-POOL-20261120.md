# Native check on GPU-2, 2026-11-20: the profile pool held every reloaded profile twice

Check of the branch `feature/zombie-profile-supply` (on `master` `0d596980`, local, not pushed) on SPT-GPU-2 on the
morning of 2026-11-20 (host time, UTC+1). Contract: the section "The profile pool held every reloaded profile twice"
in [ZOMBIE-ESCALATION-20261119.md](../../reviews/ZOMBIE-ESCALATION-20261119.md). The night before, the acceptance
([GPU-OPENING-ROUNDS-ACCEPTANCE-20261120.md](GPU-OPENING-ROUNDS-ACCEPTANCE-20261120.md)) had read the dropped Zombie
spawn requests as a profile supply that runs out. That reading was wrong: the game's backup loader stored every
reloaded profile twice, and the second copy could not be used. The correction is a Harmony prefix that does not add
what the pool already holds (`ArenaBackupProfileDuplicatePatch`).

## Result in one table

| Question | Result |
| --- | --- |
| Do the default-size Zombie waves still drop requests? | **No** in run A (waves 1 to 12, 1952 arrivals, 0 `no profiles` lines, 0 failed bursts) and in runs B and C |
| Do the waves fill as fast as the first ones? | **Yes** in run A: 2.23 to 2.78 infected per second in every wave; the night before waves 5 to 7 filled at 0.61 to 0.72 |
| Was every reload a duplicate? | **Yes**: 17 reloads in run A, 1530 profiles, every one `N of N` |
| Does the final reviewed build behave the same? | **Yes** in run C (waves 1 to 5, 4 reloads all `N of N`, no Error line) |
| A played match with kills, the PMC modes, memory | **not shown** (see "Not shown") |
| Audio exception | **seen once in run A, not caused by anything shown, open** (see "Seen on the way") |

## Setup

- Builds. Run A and B: client and server `0.6.0+62de29e4`. Run C: client and server `0.6.0+fb8bea72` (the code after
  review: the prefix binds the list as `__0`, starts with `ArenaSession.IsActive`, and four stale comments were
  corrected). The gpu-run after the install of run C answered `server stopped · game restarted · build 16/16
  identical`. Redeploy Core `1.0.24+67f8303` was not touched (checked before and after each no-deploy build).
- Installed on the VM and on SPT-GPU-2: the client DLL, its PDB and the server DLL of the branch; nothing else was
  replaced. EFT 0.16.9.5, UiBridge 0.10.1. Backup before the first install:
  `D:\SPT41-Backups\zombie-supply\20261120-082536\` with a SHA-256 manifest.
- Method as in the acceptance of the night before. Factory, Zombies, Maximum, Arena kits, 32 alive, no player kills.
  Godmode was re-applied every 1.5 s by a script. `Zombie/StuckWaveTimeoutSeconds` 5 and `Zombie/IntermissionSeconds`
  2, both with `persist=false`, so the no-kill watchdog removes the living infected and the wave goes on. Rates are
  arrivals per second from the lines `Arena burst spawn: <role> arrived.`, cut at the composition line of each wave
  (written when the wave is complete). One raid per configuration.
- Test values. Wave start size 50 and growth 20 (runs A and C); wave start size 60 and growth 0 with
  `Zombie/ShooterPercent` 100 (`persist=false`, run B); shooter share 20 otherwise. The raid recipe wrote its menu
  choices to the cfg (mode, wave start size, wave growth, Zombie own gear off); they were put back with the cfg.

## Run A: build 62de29e4, waves of 50 growing by 20

Match 08:28:52 to 08:41:37 (12.7 minutes, ended by the orchestrator), waves 1 to 12 complete. The comparison column
is the run of the night before on `0.6.0+0efe818f` with the same procedure.

| Wave | Size | Theme | Seconds | Arrivals per second | Dropped | Night before: per second (dropped) |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 50 | None | 17.6 | 2.78 | 0 | 2.80 (0) |
| 2 | 70 | None | 31.3 | 2.23 | 0 | 2.22 (0) |
| 3 | 90 | None | 35.4 | 2.54 | 0 | 2.50 (0) |
| 4 | 110 | None | 44.6 | 2.47 | 0 | 1.91 (6) |
| 5 | 130 | Runners | 53.7 | 2.42 | 0 | 0.68 (64) |
| 6 | 150 | None | 57.9 | 2.59 | 0 | 0.72 (70) |
| 7 | 170 | None | 67.0 | 2.54 | 0 | 0.61 (98) |
| 8 | 190 | None | 71.3 | 2.67 | 0 | not reached |
| 9 | 210 | None | 80.2 | 2.62 | 0 | not reached |
| 10 | 230 | Shooters | 89.8 | 2.56 | 0 | not reached |
| 11 | 250 | None | 94.1 | 2.66 | 0 | not reached |
| 12 | 270 | None | 103.1 | 2.62 | 0 | not reached |

The night before ended with 251 dropped requests in 14.4 minutes and wave 10 was not reached. Run A: 1952 arrivals, no
`no profiles` line, no `burst spawn of ... failed` line, 66 watchdog retirements (55 of them of 28 or more infected),
as the method intends.

## Runs B and C

- **Run B**, same build, waves of 60 without growth, `Zombie/ShooterPercent` 100: match 08:45:33, about 4 minutes, 9
  waves, 530 arrivals, 0 dropped, 6 reloads all `N of N`, 18 retirements (9 of 32).
- **Run C**, build `0.6.0+fb8bea72`, waves of 50 growing by 20: match 08:53:54, stopped by the orchestrator after wave
  5. Waves 1 to 5 took 17.5 s at 2.80, 31.3 s at 2.23, 35.5 s at 2.54, 44.7 s at 2.46 and 53.9 s at 2.41 (Runners)
  infected per second. 450 arrivals, 0 dropped, 4 reloads all `N of N` (270, 60, 120, 90 profiles), 12 retirements of
  32, no audio exception and no Error line at all.

## Loader and request counts (run A)

- 17 loader reloads brought 1530 profiles (per reload: 210, 180, 60, 60, 60, 120, 30, 90, 90, 30, 90, 90, 120, 60, 60,
  60, 120). Every reload read `N of N`: all 1530 were already in the pool, so without the patch each would have been
  a second copy.
- 20 requests of the miss path, 30 profiles each (600).
- In-raid requests in all: 37, for 2130 profiles. The first request came 72 s after the match start. Wall time from
  the server's answer to the complete batch: 257 to 2398 ms.

## Frame times (run A)

Longest frame share of a profile build: 1.7 to 4.6 ms in 35 of the 37 batches, once 11.7 ms and once 71.6 ms
(08:32:41.919, a batch of 60; one profile's construction, not repeated). Keep-alive samples in 5 s windows: p95 mean
per wave 9.5 to 12.8 ms, largest 17.3 ms.

## Not shown

- A played match. There were no player kills; the rate is bounded by the method at about 32 per 11.6 s. A player who
  kills fast was not simulated.
- More than one raid per configuration.
- The PMC modes with the patch. The mechanism is the same code path, but in the Tactical raids of the night before the
  PMC pool was fed by the miss path and not by the loader.
- Memory. Profiles that are pooled and never drawn were not measured (2130 profiles requested in raid against 1952
  arrivals in run A).
- The effect of the loader's changed stock count (it now orders a pair again when the real stock is more than 3 below its
  target) beyond what run A shows: 17 reloads in 12.7 minutes.

## Seen on the way

In run A the game's audio code threw `Exception: Can't handle not proposed occlusion test : Combined` from
`Audio.SpatialSystem.MultiSourceSpatialAudioOccluder.GetTargetOcclusionType` (called from `OnUpdate`), once per frame,
in five bursts, 5259 exceptions in all:

| Burst start | Length | Exceptions |
| --- | --- | --- |
| 08:31:55.313 | 5.3 s | 633 |
| 08:37:25.640 | 11.8 s | 1014 |
| 08:37:48.983 | 11.6 s | 1191 |
| 08:39:54.503 | 11.7 s | 1248 |
| 08:40:41.145 | 11.7 s | 1173 |

Each burst starts in the frame after a watchdog retirement of 32 infected that is followed at once by the next spawn,
and ends at the next mass retirement (the first one at the player's body swap of the kit refill).

- Tally of the day: 5 of 55 mass retirements in run A, 0 of 9 in run B (all shooters, so the shooter share is not the
  trigger) and 0 of 12 in run C. None in the 41 mass retirements of the two logs of the night before and in none of the
  40 newest client logs of the host.
- Reading of the decompile, not tested: audio source containers are pooled (`AudioSourceContainerData.GetContainer`
  re-initialises a pooled object with a new id and test type); the multi-source occluder handles only the two one-shot
  tests and throws for any other, so a container it still lists that was handed out again as a player's `Combined`
  container throws every frame.
- The profile patch touches no audio code. Whether the exception needs the sustained throughput that the correction
  makes possible, the raid's length, or something else is open, and so is whether it can happen in a played match (it
  was never seen in one). The watchdog also exists in normal play (100 s without a kill). Nothing was changed for it.

## Restore

The branch builds are not installed. The build `0.6.0+72273354` is back on the VM and on SPT-GPU-2, checked by SHA-256 on
both: client DLL `978B32CB...`, PDB `9183A6F9...`, server DLL `69A60D06...`, `com.maschine.ArenaMode.cfg`
`F9AF829F...`. The four server configuration files were restored from the backup: `client-settings.json`, the server's
mirror of the client settings, had taken the test values and the new descriptions. Redeploy Core `1.0.24+67f8303`
(`AA46EC84...`) was never touched. EFT and the SPT server on SPT-GPU-2 are not running, as before. The host's test
profile `6ab7db1c...` differs from its backup in exactly two leaves of 5450: `Health/UpdateTime` and
`Hideout/sptUpdateLastRunTimestamp`.

## Raw data

Git-ignored, not in the repository, under `artifacts/zombie-supply-20261120/`: `native/full-default-fixed/` (run A:
`host2-bepinex.log`, `waves.txt`, `keepalive.jsonl`, `analyze_waves.py`), `native/shooters-100-fixed/` (run B:
`raid.log`, `keepalive.jsonl`), `native/final-build-confirm/` (run C: `RESULT.txt`, `waves.txt`), `reviews/REVIEW-*.json`
(three independent reviews of the code), and `FACTS.md`.
