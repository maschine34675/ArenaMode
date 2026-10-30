# Zombie wave completion and pending spawn ownership — 2026-10-09

The tiny solo-wave smoke in `GPU-ZOMBIE-PERFORMANCE-RESULTS-20261009.md`
could start intermission before the final requested infected had arrived.
The shared wave policy treated a submitted request as a completed spawn; only
the protected-team adapter had a partial pending-tail workaround. That workaround
also missed a watchdog clearing live infected while another body was pending.

## Completion rule

`ZombieWavePolicy` now enters intermission only when the entire quota was
requested, no infected remains alive, and no request is pending. Watchdog
retirement re-enters this same rule with zero survivors and the unchanged pending
count. It can still free capacity midway through a larger wave, but cannot forgive
an unresolved final request. Zero-length intermission still starts the next wave
immediately once actual clearance is proven. Pending bodies are included in the
HUD remaining count.

`ZombieMode` removes the team-only early return; both solo refill and protected
team restoration consume the same phase result. Player death, team wipe,
readiness and protected rebind retain precedence over ordinary wave progression.

## Why the 20-second expiry also had to change

The old `ZombieSpawnService.Pending` dropped requests after 20 seconds whether
or not native work had ended. Adding only the policy guard would therefore still
permit a sufficiently late body to arrive during a later wave. A warmed profile
could also callback synchronously before the old code registered its request.

Reservations are now registered **before** calling the native adapter and are
identified by their exact entry and raid generation. Arrival and known failure
settle only that entry; reset invalidates every old callback. A successful
dispatch notification is not an arrival and cannot release capacity. Confirmed
failure before native handoff restores the owed wave slot, consumed once before
the next policy evaluation. Immediate preflight rejection retains the existing
caller-side rollback; it does not also queue an asynchronous rollback.

Two independent ceilings avoid an infinite wait without inventing free capacity:

- Profile/pool preparation: 300 seconds from request creation.
- Native activation: 60 seconds from actual handoff, after preparation and
  placement. Cold preparation does not consume this shorter window.

An unresolved request at either ceiling, an unexpected second body, or malformed
native identity ends the match as no contest. Its reservation is retained until
raid cleanup. Solo uses its existing protected result coordinator without
fabricating a player death or waiting for a nonexistent replay. PFT uses its
existing no-contest/finalization path, never its solo refill adapter or a generic
failed-start abort. These are conservative fixed internal safety limits, not new
menu settings or increased population limits.

## Native adapter boundary

The extended `SpawnInfected` callbacks are opt-in. Only owned infected requests
get the new lifecycle; ambient calls and existing explicit-faction ownership
retain their behavior. The handoff flag is set immediately before calling EFT.
A native exception after this point remains uncertain and is never retried as
though nothing spawned. `StopSpawn()` is only used for preparation that was not
handed to EFT.

Actual game source confirms why: `_decompiled/EFT/BotSpawner.cs:577` detaches
`method_7`; that method at line 899 increments `_inSpawnProcess` before cancellation
checks. `ActivateBotCallback` at line 982 registers and runs `AfterCreation`, then
invokes the caller, and only afterward decrements the counter. Submission returning
therefore does not prove activation; cancellation after submission can strand the
native counter. Existing exact raid/match admission is checked before preparation
continues and on arrival.

Owned infected arrival validates the requested role, a nonempty profile ID,
matching player identity, health controller and exact AI owner before hostility
or the observer. `PreActive` remains legitimate. A repeat callback for the same
already observed body is a no-op; a different body is retired administratively
and causes no contest. No other mod's hooks are removed or reordered.

## Offline verification

- `tools/ZombieWavePendingChecks`: actual production mode, wave policy, protected
  team runtime and refill policy; native boundary doubles. 49 assertions cover
  both solo loadouts, delayed tail, watchdog tail, normal/PFT transition,
  asynchronous and synchronous failed requests, cap, zero pause, no-contest,
  local death and inactive raid exclusion.
- Its `ServiceChecks` project links the entire production spawn service.
  33 assertions cover 25+ second and 250-second preparation, a separate native
  clock, warmed synchronous arrival, out-of-order completion, exactly-once
  rollback, old-generation callbacks and unresolved/native identity failures.
- `Verify.ps1`: 12 compiled faults must fail the intended behavioral assertion,
  not compilation. Includes the original premature-clear bug and old 20-second
  forgetting behavior. Evidence under `artifacts/zombie-wave-pending-checks/`.
- The existing extracted-production Burst integration now covers owned infected
  handoff, no-profile/role failures, thrown native submission, malformed bodies,
  same/different duplicates and terminal admission alongside the existing TDM
  cases. Final counts and independent reruns are recorded in the delivery report.
- Four additional xunit theory cases exercise pending-tail and watchdog-tail
  clearance with zero and nonzero intermission. The parent runs the server-linked
  unit project only with redirected deployment paths.

Four implementation passes checked native ordering, failure/duplicate accounting,
terminal ownership, performance and scope. Independent review identified the
missing live-role/body check; it was fixed before hostility, with direct negative
coverage. Missing IDs also reject. No remaining P0–P2 source issue was reported.
These desktop checks do not certify native gameplay or timing: restarted-client
tiny-wave, refill and protected-exit evidence belongs in the GPU delivery report.
