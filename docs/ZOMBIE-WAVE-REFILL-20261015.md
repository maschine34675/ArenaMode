# Zombie opening countdown and protected squad refill

## Requested behavior

The first wave now uses the same configured intermission duration and HUD
countdown as later waves. The clock starts only when the opening player body is
ready; a protected squad also waits for its complete frozen companion roster.
Loading, a pending opening kit swap and initial follower assembly do not consume
that pause. Wave zero represents only this opening break: it does not trigger
the refill or follower-teardown machinery. A configured zero solo pause still
starts immediately; protected squads retain their existing minimum barrier.

Between cleared waves, a surviving PFT PMC now receives the captured entry
equipment as well as returning companions. This supersedes the surviving-PMC
exception in the older Zombie/PFT and squad-size reviews. A dead PMC continues
through the existing prepared, single-use death-respawn authorization.

## Implementation and protection

`ZombieTeamRuntime.Refill.cs` runs only after PFT proves retirement of the exact
old followers and boss. It arms and prepares the same per-raid
`TeamArenaLoadoutSnapshot` already used for a dead leader, then invokes the
existing `LiveKitRespawn.AtRoundBreakOwnGear` path. Equipment commits at the
engine's existing post-teardown/pre-body-creation seam. Quickslot identities
settle and remap through the existing helper. This is a complete entry combat
loadout restoration, including ammunition, consumables and durability. It also
uses the existing full-health body replacement and nearby safe spawn selector;
the PMC may move a short distance, just as in solo wave refills.

The living replacement has a separate waiting step, not a reusable death lease.
Exact game/world/match, old body, team epoch, frozen companion count, persistence
suppression and PFT `ReadyForLeader` lifecycle are required. The engine checks
the still-current living body before takeover; the commit callback checks its
captured old body and profile without demanding an already-unregistered old
world owner. Only a successful committed replacement with exact live registries
can bind as the pending PFT leader. Followers are rebuilt afterwards. No second
refill is dispatched while the first is pending. Failure, timeout, stale
completion, disposal or an unexpected death cannot release the next wave.

The snapshot primitive is unchanged: protected slot roots and current protected
contents retain their existing identity rules. Fully consumed permitted secure
consumables are not recreated. No inventory/persistence filter was relaxed and
no arbitrary in-place refill was introduced. The ordinary solo refill path and
the protected dead-player authorization retain their existing behavior.

## Terminal roster diagnostic

The user log also showed a physical-roster rejection 14 ms after an acknowledged
Zombie team defeat. Active roster polling continued through the three-second
terminal display while dead bodies could be disposed. The new
`ZombieMode.TeamResultOwnsCurrentRaid` identifies only the exact current terminal
protected Zombie match; the shared compatibility tick uses it to end live-roster
polling at that boundary. Pending native completion retirement still runs, and
all staging, live, rebind and persistence checks stay active. The log did not
prove which rejected body reference changed, so it is not described as an
actual unauthorized replacement spawn.

The subsequent GPU run found the related manual-exit case: native `Stop(Left)`
already disposed a living follower while the Arena session remained active
through the fade. `ArenaRaidExit.NativeStopOwnsCurrentRaid` now recognizes only
`Stopping`/`Stopped` on the exact confirmed game/world and its actual world
reference. The compatibility tick skips its obsolete poll at that boundary as
well. `Started + StandDown`, failed stops and foreign/missing game-world pairs
retain observation. No intent flag replaces proof of native commit; creation
completion draining and persistence suppression are unchanged.
Production-linked UI/exit checks cover these cases and compiled counterexamples.

## Offline verification

The checks execute production mode/coordinator source, with explicit native
boundary doubles; they do not simulate or certify Unity/PFT animations.

- `pwsh -File tools/ZombieTeamChecks/Verify.ps1`: 176 assertions, 18 compiled
  negative controls rejected. Covers initial roster/body readiness, full opening
  duration, no wave-zero refill, later pending-arrival barriers, survivor and
  dead restores, repeated survivor epochs, prepare/commit/bind failures,
  wrong lifecycle/profile/body, pending completion, timeout, disposal, death,
  stale matches/worlds and exact terminal scope.
- `pwsh -File tools/ZombieSoloChecks/Verify.ps1`: 85 assertions, 13 compiled
  negative controls rejected. Terminal-death scenarios start after the opening
  countdown.
- `pwsh -File tools/ZombieWavePendingChecks/Verify.ps1`: 49 mode assertions,
  33 native-service boundary assertions, 12 compiled negative controls rejected.
  Preserves the original wave-relative timing after the opening countdown. No
  pending spawn reservation or watchdog rule was weakened.

Team evidence is under ignored
`artifacts/zombie-team-checks/d9273f5b15da4891802bb3c932cf444d`.
Solo evidence: `artifacts/zombie-solo-checks/87c9ff001a4f4b16923c4cc4f500564a`.
Pending-wave evidence:
`artifacts/zombie-wave-pending-checks/4175cd82da154d668424fa4a650b1ee2`.
The unchanged real loadout snapshot has its separate `TeamArenaLoadoutChecks`
suite. Final staged build, installed hash, native acceptance and profile/store
restoration evidence belong to the dated handoff and GPU report.

## Native acceptance still required

With one to four real invited companions, spend ammunition and consumables,
clear a wave alive and verify the entry gear returns before the next countdown.
Repeat after local death with optional replay active. Confirm the exact follower
count, no duplicate leader, active movement/weapon controls, and a normal wipe
without a new physical-roster error. Compare the player profile and PFT stores
before/after protected exit. Also exercise the opening pause in solo kit and own
gear modes. These checks are not claimed by the offline fixtures above.
