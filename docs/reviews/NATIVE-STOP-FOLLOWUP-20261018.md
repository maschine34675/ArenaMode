# Native-stop roster follow-up — 18 October 2026

Independent review of the current `PitFireTeamCompat.TryGetLiveFollowers`,
`ArenaMatchService.Tick` and `SynchronizeScoreboard` correction. The reviewer
changed only this document and `tools/NativeStopChecks`; no runtime, deployment
or GPU state was changed by this leaf.

## Finding and correction

The previous audit reproduced a physical-roster error immediately after the user
committed native `Stop(Left)`. Native bot disposal precedes Arena's `GameEnd`
cleanup, so `ArenaSession.IsActive` and `ArenaMatchService.Current` may still exist
while the wrappers disappear. That period is not a new live roster loss.

The correction uses the existing exact ownership predicate:
`ArenaSession.IsExactActiveRaid` for the current native game/world, the game's own
`GameWorld` matching that world, and native status **Stopping or Stopped**.
Neither a leave request nor `StandDown` proves this. A native callback that
throws before commitment remains Started and must retain live validation.

Three caller guards cover the demonstrated paths:

- `ArenaMatchService.Tick` stops mode advancement after native commitment, but
  leaves the separate failed-start-abort owner before that guard.
- `SynchronizeScoreboard` checks again because `match.Tick` or `OnMatchStart`
  can itself commit Stop before synchronization in the same frame.
- `TryGetLiveFollowers` checks after returning any previously latched violation,
  but before new reflected discovery. Tactical HUD/status getters also reach
  this method outside the match tick. Stopped, unfailed discovery returns an
  empty observation without erasing any identities or persistence ownership.

Reviewed call graph: `ArenaController.Update` calls PFT maintenance before match
maintenance, then HUD and the queued exit service. Tactical staging/live,
Tactical HUD/status and Zombie team observation reach the shared follower getter.
Scoreboard synchronization also occurs directly after match startup. The
additional caller guards therefore cover paths the old guard in PFT.Tick alone
did not cover. No runtime defect was found in this scoped review.

`PitFireTeamCompat.Tick` still calls
`PitFireTeamRoundLifecycle.TickPendingRawPlayerCompletions` **before** its
live-roster guard. Those old-creator completions must drain during native Stop
and even outside Arena; they keep their captured owner-thread rule. The pending
queue, revoked creator history and persistence/finalization state are not cleared
by the new guards. Existing live errors and their first full diagnostic remain.

## Production-linked checks

```powershell
dotnet run --project tools/NativeStopChecks/NativeStopChecks.csproj -c Release -- .
pwsh -NoProfile -File tools/ArenaUiInteractionChecks/Verify.ps1
```

The new harness extracts complete current methods with Roslyn: follower
discovery/latching/getter, PFT.Tick, raw-completion draining, match Tick,
scoreboard synchronization and its helper methods. It also compiles the entire
actual `ArenaRaidExit.cs`. It does not reproduce the stop predicate in test code.
EFT objects, reflected PFT wrappers, mode bodies and the final native completion
action are controlled boundaries. The scenario uses one valid frozen companion,
which is a supported roster size, then removes its actual reflected wrapper.

**33 assertions pass**, covering valid discovery; live disappearing bodies and
one-time error latching; preserving a prior fault through Stop; Stopping/Stopped
disposal without a new fault; unchanged bound identity storage; stopped match
and scoreboard; failed/queued Stop; foreign game/world; Stop committed by the
mode in the same tick; continued failed-start handling; ordinary PFT non-use;
and owner-thread pending completion draining.

Five compiled production-source mutants are rejected with the exact expected
runtime assertion (compiler errors do not count): remove the central roster
guard, remove the match guard, remove the separate scoreboard guard, bypass a
previously latched fault during Stop, and put pending draining behind the Stop
guard. Output is retained locally in
`artifacts/native-stop-checks/caller-tests.txt`.

The existing UI-interaction verification independently exercises the full native
exit coordinator and checks installed EFT method metadata. Its standalone exit
baseline passes **81 assertions**, with explicit controls for mistaking StandDown
for commitment, a foreign world, unsafe owner/pipeline/paused states, failed
precommit rollback and notification failure. All **eight compiled exit mutants**
are rejected. A separate direct run of `ArenaUiInteractionChecks.csproj` again
passes 81 assertions.

The enclosing script is **not green** at this leaf's handoff: its subsequent
native-menu checks pass 738 assertions, and preview checks pass 52 assertions,
five mutations, hook compilation and the synthetic frame parser. It then stops
in `PreviewHostLifecycleChecks` with CS0103 at `ArenaPreviewFlow.cs:155`: its
boundary fixture lacks `ArenaMode.Kits.TacticalTeamKitEntry.Selected`. This is
outside this leaf's ownership and was reported to the parent, not patched or
ignored. The parent integration report owns its resolution. Existing exit
control logs are under
`artifacts/ui-interaction-checks/3114c9153de249f4aedde84a4ebec620`; downstream
failure evidence is under
`artifacts/preview-host-lifecycle/1bb3ddc66e2e499db93fd2e1d6fde144`.

## Four-pass review and limits

1. Traced every current follower getter caller and both scoreboard entry paths.
2. Checked exact native commitment, latch order, failed-stop retry and unchanged
   persistence/creator ownership; found no change to native gameplay order.
3. Executed complete production methods with live, teardown and failed controls;
   injected compiled bad variants, including a wrongly gated pending drain.
4. Reran the final harness and reviewed source/diff boundaries and this report.

These are offline behavior and installed-contract checks. They do not execute
Unity's actual disposal, PFT's native wrapper creation or the final old-player
retirement implementation. A fresh GPU test of intentional PFT exit and profile
restoration is still the native acceptance gate; no clean-log claim follows
from this harness alone.
