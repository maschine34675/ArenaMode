# Labyrinth long-raid review, 2026-09-05

Read-only review of the user's completed Gun Game raid. No cleanup, respawn or
third-party compatibility code was changed during this investigation. This run
predates the new spawn-trap suppression and cannot validate it.

## Completed raid and profile safety

`D:/SPT41/BepInEx/LogOutput.log` records one Gun Game raid from 18:04:06 to
18:37:38 local time (about 33 minutes):

- 158 local kills, 47 deaths, 33,400 points, best streak 14; line 16730.
- Six complete weapon ladders, finishing on tier 15/24; line 16729.
- 53 announcer callouts, 53 play requests, zero skipped; line 16728. Playback
  logs use `Arena.Master`, with no announcer warning. These are successful
  dispatch counters, not an independent audibility measurement.
- 649 corpses cleaned up, ten retained visibly; line 16712.
- No Arena error/no-contest. A stale pending spawn batch was discarded at
  raid end, as the lease requires; line 16732.
- The previous Labyrinth alarm change found one verified player and seven
  sources; line 12597.

The client warning `raid-end remains pending` at 18:37:38.642 is not a failed
restore. `D:/SPT41/SPT_Runtime/user/logs/spt/spt20260905.log`, lines 11526-11534,
records the deferred request, native local-end request, snapshot restoration
at 16:37:38.743 UTC, and fully completed protected end/persistence at .782 UTC
(18:37:38 in local time). No session/profile identifiers are copied here.

## Confirmed cleanup diagnostic bug

Native errors log:
`D:/SPT41/Logs/log_2026.09.05_16-02-59_0.16.9.5.40743/2026.09.05_16-02-59_0.16.9.5.40743 errors.log`.

It contains **5,640 `No parent inventory ... RemoveItemEventArgs` errors
covering exactly 649 corpse IDs**. First group: 18:05:07.342, native error-log
line 29, next to Arena's 18:05:07.345 cleanup (BepInEx line 12733). Last group:
18:37:34.723, native line 6445, next to Arena's .725 cleanup (BepInEx line 16712).
This correspondence strongly identifies Arena's gear stripping as the source;
it should not be dismissed as an unrelated mod error.

`ArenaMode.Client/Bots/CorpseCleanupService.cs`, `StripGear`, lines 334-370,
calls `ItemManipulator.RemoveWithoutRestrictions` and then only
`RaiseEvents(controller, CommandStatus.Succeed)`.
Native `../_decompiled/EFT.InventoryLogic/ItemController.cs`,
`ProcessActivity`, lines 1733-1748, expects a matching type/item `Begin` event
in `ActiveEvents` before any completion event. If absent, it prints the
misleading `No parent inventory` message. Here it means missing activity
pairing, not a lost item parent, lost player inventory or failed profile save.

The remaining `RaiseRemoveEvent` view callbacks still run after that diagnostic.
Do **not** simply remove `RaiseEvents`: the corpse gear views need those
notifications to return their objects to the pool. A separate targeted fix
should verify correct Begin/Succeed pairing and failure behavior against native
view handlers, without reintroducing the former unreleased-gear problem.

Two further inventory diagnostics are `DrainItemEventArgs` for AFAK use, not
part of the 5,640 corpse removal events; their cause was not established here.

## Respawn timing trend, not an FPS or leak measurement

For 47 completed respawns, the native pipeline averages about **0.393 seconds
over the first ten** and **0.962 seconds over the last ten**. The last respawn
takes 1.07 seconds in the pipeline / 1.11 seconds total (BepInEx line 16675).
An early example is at line 12824. Mean total respawn time across all 47 is
0.747 seconds. The maximum total, 2.38 seconds, occurs early and includes
2.05 seconds of bundle loading.

There is a measurable upward pipeline trend despite the user's good experience.
The logs contain no adequate FPS, frame-time or memory series to prove an FPS
regression, memory leak, or cleanup logging as the cause. Keep those conclusions
separate; future profiling should measure the growing respawn stages directly.

## Other observations

- 33 native `TriggerZone.MoveInsideRoutine` null-reference errors occur only
  during map teardown at 18:37:44.326-.340 (BepInEx line 16737 onward).
  Native `TriggerZone.cs`, lines 198-208, repeatedly accesses a player's
  transform; normal exit stops that coroutine at lines 191-194. Stale references
  after repeated body replacements are plausible, but the log alone does not
  establish respawns as the sole cause. The spawn-trap change does not claim
  to fix these unrelated movement-trigger lifetimes.
- Two PFT/BigBrain AI update errors reference a dead boss-player position;
  BepInEx line 13716. Four native bot weapon-switch errors include line 14860.
  Three SAIN brain rejections and three SAIN grenade `Player Null` messages
  also occur. Arena's repeated lifecycle transitions can expose stale references,
  but direct causality is not proven for these stacks.
- One spawn attempt has no free point (line 14606); spawning resumes about
  three seconds later (line 14629). No sustained empty-opponent condition follows.
- Three PK spare-magazine space warnings (including line 13201) and one
  Grizzly space warning (line 16017) do not abort the match.

Priority: repair and regression-test cleanup activity pairing separately, then
measure the modest respawn growth. No evidence in this run justifies weakening
profile protection or making broad changes to bot cleanup/lifecycle handling.
