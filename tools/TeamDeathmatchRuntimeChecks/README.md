# Team deathmatch runtime model checks

Run `dotnet run --project tools/TeamDeathmatchRuntimeChecks -c Release` from the
repository. This is a standalone .NET test executable; it never invokes a client
or server deployment target and does not need a GPU.

The harness links the actual production `TeamDeathmatchMode`, population policy,
`TeamDeathmatchRelations`, scoreboard, kill/score/award policies, tracer team
registry, administrative death scope and no-corpse-looting patch. The actual
native prefix methods run through the modeled dispatch, rather than reimplementing
their decisions in test callbacks.

The 2026-09-07 run covers 209 assertions:

- USEC and BEAR with 2, 5 and 12 players per faction; human slot reservation,
  one-shot readiness, actual faction scoreboard and no overfill.
- Synchronous observer-before-dispatch completion, delayed loading, expiry,
  failed preflight cooldown and PreActive physical occupancy.
- Human death, continued knowledge for both bot factions, same-profile body
  replacement and a partially built local body during the respawn operation.
- Mismatched faction/role, foreign or mixed groups, unexpected live owners,
  administrative retirement ordering and suppressed or throwing native Kill.
- Healthy bodies in all five native activation states, timeout retirement,
  retry spacing and the three-consecutive-failure boundary.
- Old exact game/world, late observers, match end, normal/non-PMC/PFT refusal.
- Native Stopping/Stopped with healthy teardown bodies, first-observer stop
  detection, late dispatch completion, reentrant stop during retirement and
  suppression of new requests/score/readiness cues after committed shutdown.
  Genuine NonActive failures while Started still retire bodies and stop at three.
- Bounded unavailable/recovery and timeout/settled diagnostics, request phases
  and age, active/occupied faction counts, pre-retirement state and failure
  budget, successful activation recovery, and snapshots before queue cancellation.
  Technical details stay out of player toasts and do not include profile IDs.
  An unreadable disposed identity forces a snapshot exception and verifies it
  cannot interrupt native shutdown, retire bodies or create a failure toast.
- Friendly enemy admission for initial, direct and revenge causes, memory-only
  and goal-setter bypasses, old relation cleanup and dead attacker attribution.
- Friendly-fire decision, self/unknown damage, same-profile foreign bodies,
  score/awards, retained dead shooter tracer colors and no-looting exceptions.

`Verify-Regression.ps1` creates only temporary copies of production sources. It
proves eleven independent changes fail for the intended behavioral assertion:
removing the reserved human slot; allowing native friendly enemy admission; and
adopting a partially built local body during respawn; restoring the native exit
failure window; accepting detached requests after Started; failing the mode on a
reentrant stop; announcing readiness after a stop during refill; logging an
unchanged missing roster every tick; reporting the wrong request phase; and
capturing diagnostics only after cancelling the pending queue; and letting a
diagnostic exception interrupt shutdown. It verifies production
source hashes never changed and finishes by rebuilding/running the original
production inputs. Temporary copies are outside the repository and are retained
for inspection; no recursive cleanup is performed.

`Verify-InstalledContracts.ps1` uses the installed Mono.Cecil to read metadata,
without loading EFT/SAIN game code. It verifies the installed SAIN private enemy
admission/body properties and native group/memory plus all four attributed
damage surfaces. It also verifies installed native Stop IL commits Stopping
before StopGettingInfo disposes the bot owners. The reviewed SAIN 4.5.1 DLL SHA256 is
`C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489`.
Run it with PowerShell 7 (`pwsh -NoProfile -File
tools/TeamDeathmatchRuntimeChecks/Verify-InstalledContracts.ps1`). Windows
PowerShell 5.1's .NET Framework can refuse the installed downloaded Mono.Cecil
assembly; this check does not unblock or modify any installed file metadata.

## Deliberate limits

`Stubs.cs` models the small engine surface and provides a controlled burst-service
seam. It does not emulate Unity destruction, NavMesh, rendering, audio, the entire
EFT spawn coroutine or Harmony's machine-code patch installation. SAIN is absent
from this process; its integration metadata is checked separately. Friendly
damage tests exercise the production decision; actual armor/health mutation
ordering is established by native source inspection and the installed method
contract check, not by pretending this model executes EFT inventory code.

The complete production burst extension was independently traced against native
`BotSpawner.method_7`, `method_10` and `ActivateBotCallback`. Same-body duplicate
callback suppression is in that extension and was reviewed there, before its
expired-ticket test; this harness does not claim a copied duplicate filter in
its burst stub as production coverage. A different second body is retired.
The shutdown observer scenarios likewise exercise the actual mode, not the
shared burst helper: its existing rejected-arrival cleanup remains unchanged.
Root's separate population/dispatch harness exercises the actual one-shot
dispatch lease. No local test is proof of live SAIN movement or GPU raid behavior.

## Independent burst review (2026-09-05)

The review found a retirement confirmation gap: a no-op Kill could leave an
unexpected living body while refill continued. Root fixed actual mode retirement
to verify death and stop on failure; the suppressed/throwing Kill scenarios here
pass against that source. The shared late-spawn helper now reports unsuccessful
retirement instead of claiming it succeeded. Unexpected current-world survivors
are checked by the mode before its next refill.

The final source trace found no unresolved P0-P2 issue:

- Explicit factions use separate request-local reservation lists; they cannot
  release ambient/Tactical FIFO reservations or alter faction alternation.
- Exact raid/generation/request leases are repeated after the awaited profile
  load and immediately before native dispatch. Actual role is checked before
  dispatch and role/side/body consistency again at callback.
- One point is passed per profile. Native `method_7` increments counters before
  checking StopSpawn; all known cancellation/filter rejection occurs before
  that call. Dispatched uncertain work is never StopSpawn-cancelled.
- Thread-local dispatch scope spans only the synchronous call, binds exact
  spawner/data/game/world, consumes once, and cannot resurrect disposed outer
  scopes. Unknown later patch refusal remains bounded uncertain work.
- Native activation registration and the observer occur synchronously in
  `ActivateBotCallback`; arrival can precede dispatch completion. Settled mode
  tickets cannot be reopened by that completion notification.
- Duplicate identity detection precedes ticket expiry checks so repeated
  notification cannot kill an already admitted same body. A different body is
  rejected. Observer exceptions and stale leases retire exact returned bodies.

Source evidence: `ArenaMode.Client/Bots/BurstSpawnService.cs`,
`ArenaMode.Client/Bots/TeamDeathmatchSpawnDispatch.cs`,
`ArenaMode.Client/Bots/ArenaBotSpawnPatches.cs`, and
`../_decompiled/EFT/BotSpawner.cs:899`, `:958`, `:982`.

## Bounded-area consumer contract

The production mode now also runs against explicit area-state inputs: it freezes
2v2 despite a larger configured size, holds respawn and suppresses bot requests
while preparing, releases exactly one teammate/two opponents when ready, and
checks replacement positions before reactivating the local roster. A rejected
position requests abort instead of admitting the body. `AreaStandIns.cs` models
those external verdicts and records the abort request; it does not execute
geometry, NavMesh, native Stop or profile restoration. Earlier assertions remain
unchanged, and normal/full-map fixtures reset the new external input.
