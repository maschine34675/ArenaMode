# PitFireTeam M2/M3 live log review — 2026-09-01

## Scope

This review covers the two successful Labs Tactical 5v5 runs at 20:58–21:01 and
21:04–21:07 CEST. It correlates the BepInEx client log, EFT logs, SPT server log,
profile backups, PitFireTeam teammate files, ArenaMode source, PitFireTeam reference
source, SAIN source and decompiled EFT source. EFT was not launched during the review.

## Result

M2 and M3 passed. Both raids reached live play with the original PMC plus four friendly
PitFireTeam companions against five distinct active Arena opponents. The second run kept
the companions active after the local player died, ended when they eliminated the opposing
team and did not replace the player's body, respawn the player or start a second round.

The server-side protection also completed correctly in both runs. PitFireTeam outcome,
extracted-item, kill-message and hidden recovery paths were suppressed; the Arena snapshot
was restored and persisted while ordinary post-raid processing was skipped. The server log
contains no Warning, Error, Critical or Fatal entry during the second raid.

## Runtime evidence

The first run requested five USEC opponents, planned five profiles and five distinct points,
received five `PreActive` callbacks, waited for activation and entered live 5v5 play at
20:59:18.928. All five opponents were eliminated and the raid ended once as Survived.

The second run repeated the same sequence with five BEAR opponents and entered live play at
21:04:51.670. The local player died at 21:05:05.095. Arena suppressed PitFireTeam's normal
leader-death cleanup, parked the respawn coroutine and left the companions in the match. At
21:07:15.868 all five opponents had been eliminated. The native end completed at 21:07:23.

The four PitFireTeam teammate profile/equipment/settings files were last modified between
07:03 and 18:16, before either live test. Comparing the available profile backups around both
runs found only the normal Hideout generator consumption, fuel remainder and Hideout timestamp
changes. Inventory, skills, statistics and all other profile data were unchanged.

## Material follow-up: follower inventory/hands and aiming state

At 21:05:48.435 EFT logged a parentless `DrainItemEventArgs` for follower `sdfgsdfg` while
using ibuprofen. Sixteen milliseconds later a burst of 50 null-reference exceptions began in
`SAIN.AimDownSightsController.SetADS` through EFT's firearm aiming controller. Six later EFT
exceptions occurred in `ResetAimingAnimationsFlags`, with the final one at 21:06:24.831.

This is a real runtime defect, but the current evidence does not prove a direct ArenaMode
cause:

- The exception stacks contain SAIN and EFT bot-weapon code, without an ArenaMode frame that
  executes for bots. Arena's Harmony wrappers are present around the EFT methods, but their
  logic is restricted to the local player and passes bots directly to the original method.
- The aiming stacks do not contain a bot identity. Their timing makes the follower with the
  immediately preceding medical inventory error a strong candidate, but it could still be an
  Arena opponent.
- The first 5v5 run, in which the local player survived, had no such aiming errors. Arena can
  therefore have exposed the condition indirectly by keeping PitFireTeam followers alive in
  the unusual state where their leader is dead. The first aiming failure occurred 43 seconds
  after the player death, so the logs do not prove that lifecycle link either.
- The defect did not prevent the remaining team from winning or interfere with protected raid
  finalization and persistence rollback.

A targeted diagnostic is the safest next step before changing behavior. On the next M3-style
run it should record the throwing bot's profile, team classification, bot state, current hands
operation and firearm animator state while leaving the exception behavior unchanged. An SAIN
A/B run would then distinguish PFT/EFT follower-hands behavior from SAIN's ADS update.

## Other messages

- `Arena death #1 — instant respawn`, `External raid end during arena respawn — standing down`
  and `Arena raid-end remains pending` are misleading descriptions of expected control flow in
  this team mode. The respawn was held until the native end, and the pending custom end was
  followed 61 ms later by the successful server snapshot restore. These are logging-quality
  issues, not failed gameplay or cleanup.
- Four `PlayerBody destroyed without being disposed` messages appeared immediately before the
  four PitFireTeam followers were resolved in each raid. Count and timing point to PitFireTeam's
  temporary group/player bodies, but the messages have no identity or stack. Arena explicitly
  skipped its local-player body replacement, and no follower activation failed.
- The repeated ammo-box stack-slot exception existed at profile load at 20:56:43, before M2.
  The Arena profile restore only causes the already-invalid Scav inventory entry to be loaded
  again; neither successful Arena run created it.
- Labs `SpawnPointMarker`, `WeatherObstacle`, missing scene-script and `JetMaterial` messages are
  map/engine asset diagnostics. They did not prevent the planner from finding the five points
  or the match from reaching live play.
- The nine `Updated spt bot ... brain` warnings are successful SPT brain assignments for the
  four companions and five opponents. The warning severity does not indicate a failure.
