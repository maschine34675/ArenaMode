# Independent TDM search awareness — 2026-09-08

Later 2026-09-09 follow-up: `TDM-SEARCH-TEMPO-REVIEW.md` adds a separate native
search-argument cap and passive actual-mover diagnostics. The hearing admission
and contact-aging behavior described here remains unchanged; references below
to unchanged search delays describe this original hearing-only milestone.

Scope: `TeamDeathmatchRelations`, optional `TdmSearchAwareness`, and the isolated
production-linked `tools/TdmAwarenessChecks` harness. No new movement owner,
navigation graph change, path-guard relaxation, personality override, PFT change,
profile write, external implementation or bundled SAIN dependency.

## Evidence and change

GPU retest `55164e4` contains full-roster quiet USEC 2v2, initial BEAR 2v2 and
BEAR 5v5 windows, but also fighting matches. The quiet windows have no Arena
movement rejection or TDM awareness exception. They do not record per-bot
contact/decision state. The later BEAR 2v2 firing experiment showed a kill/refill
before the player's shots; it does not establish a shot-triggered wakeup.

The current one-second independent-TDM reconciliation already chooses the nearest
live opponent without a distance cap and refreshes SAIN's known position. Installed
SAIN's `Enemy.UpdateLastSeenPosition(Vector3, float)` updates position and squad
knowledge, but does not set `Seen` or `Heard`. Its search admission can require an
aged personal sight/hearing or squad report; continuously refreshed squad positions
do not age. This is a concrete missing search prerequisite, not proof that every
quiet GPU actor stalled for the same reason.

After the unchanged position refresh, exact current live opposing bodies may now
receive a `Generic` / `Hearing` report through `Enemy.Hearing.SetHeard`. Both
`isDanger` and `shallReportToSquad` are false. This happens only when native SAIN
`Seen`, `Heard` and `IsVisible` are all false. It is a private Arena search cue,
not a global fabricated gunshot or visual contact. SAIN's own ignore-sound policy
still controls acceptance. A non-null result and `Heard` readback are required to
report successful admission.

Existing contact prevents another call, so the original heard timestamp can age
through SAIN's normal search delay. If SAIN later forgets the same live wrapper
and clears contact itself, a subsequent nearest-opponent acquisition may admit a
new episode. Arena never clears native contact to force this. Ignored cues retry
only through the existing one-second mode reconciliation; there is no new polling
loop, timer backdating, visible/can-shoot write, goal assignment or mover command.

Before new hearing writes and after optional-mod callbacks, ownership checks cover
the exact active independent-TDM raid, native `GameStatus.Started`, faction-consistent
current live bodies, source component/controller, group, native dictionary pair,
and SAIN wrapper references to the same physical target and `EnemyInfo`. Stops or
body replacements during a callback cannot be reported as continued successful
admission. The existing broader faction/late-projectile attribution gate is unchanged.

The hearing bridge resolves independently of the pre-existing SAIN bridge. An absent
component, unsupported hearing shape or optional hearing exception preserves native
awareness and the old SAIN position update when available. Normal raids and other
Arena modes have no new awareness action or observation.

## Installed contracts inspected read-only

Installed `SAIN.dll` 4.5.1 was inspected with local ILSpy, alongside the matching
reference tree for navigation only. No external implementation was copied.

| Contract | Confirmed behavior used here |
| --- | --- |
| `SAIN.SAINComponent.Classes.EnemyClasses.Enemy` | `EnemyPlayer: Player`, `EnemyInfo: EnemyInfo`, `Hearing`, and bool `Seen`, `Heard`, `IsVisible` getters. `UpdateLastSeenPosition(Vector3, float)` does not itself set sight/hearing flags. |
| `SAIN.Components.BotComponentSpace.Classes.EnemyClasses.EnemyHearing.SetHeard(SAIN.Models.Structs.SAINHearingReport, float)` | Returns `SAIN.SAINComponent.Classes.EnemyClasses.EnemyPlace`. Checks `HearingInput.IsIgnoringSounds` first. Sets heard state/time through native SAIN handling, without writing visual contact. Internal heard time comes from `Time.time`, so passing an older time would not safely shorten its delay. |
| `SAINHearingReport` | Instance fields `position: Vector3`, `soundType` enum containing `Generic`, `placeType` enum containing `Hearing`, `isDanger: bool`, `shallReportToSquad: bool`. Resolved by shape and enum names, without integer assumptions. |
| Native forgetting | `EnemyHearing.OnEnemyKnownChanged(false)` clears `Heard` on an existing wrapper. The one-shot latch therefore belongs to a contact episode, not the entire wrapper lifetime. |
| SAIN search | `SearchDecider.shallBeginSearch` can admit aged personal hearing when `WillSearchFromAudio` is enabled. Other settings, search delay, equipment comparison, real perception and path feasibility remain SAIN decisions. |
| Native EFT | `AbstractGame.Status`, `BotOwner.StandBy.StandByType`, `BotOwner.Mover.IsMoving`, current `EnemyInfo` and group ownership verified in `../_decompiled`. |

Read-only installed SHA-256 observations:

- `SAIN.dll`: `C8942B646463283D1247B5673D018C1D3F2EDFBE754A06061CB33BD09C368489`
- `SAIN.Preset.Shared.dll`: `CD68E6DC65508C8BBBE71103F2AE7DE083C5281D6A57BC7D3CB846C133FC1F58`
- `Assembly-CSharp.dll`: `EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436`

## Bounded diagnostic contract

`Arena TDM awareness:` emits at most one snapshot per physical bot every 30 seconds,
including the initial observation. It records:

- Per-raid physical actor numbers and faction, nearest target number/distance and
  current native goal number. No profile IDs or player names are disclosed.
- Hearing admission stage, actual seen/heard/visible/known flags, heard age and
  SAIN search delay, combat decision and nested search-decision reasons.
- Native mover/standby state, displacement and the actual elapsed sample interval.

Displacement is endpoint distance, not distance travelled; a loop can return close
to the same point. Property/field reads are cached and best-effort (`n/a` if absent).
They never call search-decision methods for measurement. Optional exceptions warn
once per raid. Weak entries and actor-number counters reset with relationships;
diagnostics do not retain corpses. With nine live bots, normal steady-state output
is at most nine snapshots per 30 seconds, plus observations for newly spawned bodies.
Correlate with the unchanged Arena movement-rejection diagnostics; absence of a
rejection still does not prove a traversable/searchable path was attempted.

## Search diagnosis follow-up — 2026-09-08

The user's two subsequent Customs team-start USEC 5v5 runs contain 86 and 158
awareness samples, respectively. Hearing/known state is present in every sample,
but selected bots still report `PathCalcFailed` and `NullTargetPlace`. In 19/27
samples the nearest cue target differs from the native goal. These are samples,
not a continuous movement trace. Read-only log snapshots are under ignored
`artifacts/log-comparison/20260908-201507/`.

The diagnostic-only extension retains `target`/`distance` and the original
unprefixed contact flags for the nearest hearing-cue target. `goal` still means
the EFT native goal. Added fields are:

- `sainGoal`: the physical actor selected by SAIN's filtered
  `BotComponent.GoalEnemy`, read once for this snapshot; `none` for an available
  getter with no goal, `n/a` for absent/failed optional support.
- `goalMatchesTarget`, `goalDistance`, `goalSeen`, `goalHeard`, `goalVisible`,
  `goalKnown`, `goalHeardAge`: read from that same SAIN goal/body, never borrowed
  from the nearest opponent or the native goal. Missing goal values stay `n/a`.
- `shallSearch`: SAIN's nullable `EnemyDecisionClass.DebugShallSearch` result;
  both `True` and `False` indicate that search was evaluated in its cached
  decision pass. `pathReason` exposes `DebugSearchReasons.PathCalcFailReason`,
  including its existing distinction between absent known positions, invalid or
  incomplete paths, insufficient corners and targets too close to search.

Installed SAIN 4.5.1 clears `DebugShallSearch` before the ordinary decision
checks, but can retain `DebugSearchReasons` when another decision wins first.
Its no-enemy and `AvoidGrenade` returns precede even that nullable reset.
Therefore all search reasons are reported as `n/a` if the nullable result is
absent, no SAIN goal is available, or the combat decision is unavailable/`None`/
`AvoidGrenade`. Never report a retained error as if those branches had just
attempted a path. These remain cached SAIN decision values alongside the sampled
current goal; no timestamp or fresh path calculation is fabricated.

All added members were verified against the installed SAIN DLL with the hashes
above. Getters only read existing state. No enemy selection, path calculation,
hearing refresh, decision evaluation, movement, load scheduling, profile mutation,
LootOutline change or external dependency was added. The same 30-second cadence,
weak per-raid actor numbers and ordinary-raid separation remain in force.

Verification adds distinct cue/native/SAIN goals, differing contact ages/flags,
no-goal and failing optional getter cases, retained reasons after skipped search,
and early grenade/no-combat decisions to the existing production-linked harness.
The review and final verification result are recorded in the dated handoff.

## Invalid-path positions — 2026-09-08

The next user log (`artifacts/log-review/20260908-211257/client.log`) contains
eight sampled `path Invalid` failures. Six concern actor 6, whose sampled position
stays unchanged after initially moving 19.3 metres; another actor also cannot find
a path to actor 6. Different selected opponents fail while contact/search delay
requirements are met. This suggests an endpoint/navigation issue, but the log does
not prove which endpoint is invalid or that the Arena boundary caused it.

`Arena TDM path detail:` now records one additional snapshot per physical bot per
raid after its second evaluated `PathCalcFailed` / exact `path Invalid` sample.
Counting uses the existing 30-second observer cadence, not every reconcile call;
intervening other decisions or changed targets do not erase the first sample.
Ordinary raids, skipped search evaluations and other path failures cannot trigger
it. Extra navigation reads only occur after the threshold, and stop after emission.
A selected-goal change during the reads discards the mixed snapshot without using
up the one-message allowance. Weak observation state resets with the raid.

The line identifies the same selected SAIN goal as the ordinary snapshot and adds:

- `sourceActual`/`goalActual`: physical player positions; `sourceNav`/`goalNav`:
  cached `Transform.NavData.Position` values. The source NavData is precisely what
  SAIN's `Bot.NavMeshPosition` getter reads.
- Each endpoint's `LastHit`, `HitValid`, `Status`, `OnNav`, `LastOnAge`,
  `ActualMinusNav` and `NavMinusHit`. Coordinates/differences are signed `(X;Y;Z)`
  metres with two decimal places and invariant formatting.
- `goalKnown` and `goalKnownMinusNav`: the selected enemy's cached
  `KnownPlaces.LastKnownPosition`, which is the destination used by SAIN's path
  calculation; it is not substituted with the current or nearest player's position.
- `pathStatus`, `pathCorners`, `pathLength`: existing selected-enemy path state.

All new SAIN getters and Unity's `NavMeshHit.hit`/`position` were checked against
the installed DLLs (same SAIN hash above; UnityEngine.AIModule SHA256
`A446A7EC1D78C853617D8C751EB7F089E903C4E4EB18677915471265471CD893`).
No third-party implementation was copied or bundled.

**Cached status is not an independent NavMesh probe.** Installed SAIN samples
within one metre, but constructs NavData.Position with current XZ and the hit's Y.
Its status comparison ignores height, and a failed sample can retain an old hit.
`IsOnNavMesh` adds a 0.15-second status buffer. `TimeLastOnNavMesh` may therefore
advance without a new successful hit; `LastOnAge` is not the age of LastValidHit.
Unset/future last-on times and missing/throwing optional members remain `n/a`;
a hit with `HitValid=False` must not be interpreted as a valid origin coordinate.
These values help distinguish candidate endpoint errors without pretending that
they prove fresh connectivity. There is no SamplePosition/CalculatePath call,
path/cache edit, teleport, recovery, AI decision or loading change.

Production-linked checks cover deferred reads, retained reasons, differing targets
and coordinates, vertical/lateral offsets, per-body limits/reset, missing getters,
selected-goal drift and unchanged contacts/bodies/path fixtures. Four additional
named source faults exercise threshold, emission cap, destination attribution and
the selected-goal recheck. Final evidence/review/delivery are in the dated handoff.

## Offline checks and review

Run only the isolated harness:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/TdmAwarenessChecks/Verify.ps1
```

It compiles the actual production Relations and hearing bridge against independent
minimal EFT/Unity/SAIN API doubles. The test executable is locally named `SAIN` to
exercise production's real optional assembly discovery; it is not an external SAIN
binary and is never deployed. Tests model controllable admission outcomes and API
identity relationships, not AI, navigation, Harmony detours or Unity.

565 assertions cover both factions, cold and existing contacts, 120 seconds of
one-second updates without timer renewal, same-wrapper native forgetting, nearest
opponent selection including distant targets, allied exclusion, stale/foreign/live
identity permutations, callbacks that stop native play or replace a body, ignore/null/
readback/exception behavior, missing/unsupported optional support, unchanged fallback,
bounded/private diagnostic output and actual sample intervals. The aged-hearing
predicate is explicitly only a search prerequisite, not a simulation of SAIN search.

The verifier recompiles 20 source mutants and requires each to fail a named behavioral
assertion. It covers omitted cues, renewed heard timers, missing sight/visibility
guards, wrong sound/flags, false admission, permanent wrapper latch, raid/native-stop/
body/wrapper checks, post-callback ownership, diagnostic flooding and lost original
position refresh. A final production rebuild/run restores the harness after mutants.

Four passes: (1) installed/native contract and minimal-diff review; (2) exact body,
native-stop and callback review; (3) production-linked regression/negative-control
self-tests; (4) independent adversarial source review. That review found the initial
permanent-wrapper latch and requested clearer diagnostics; both were corrected.
The independent reviewer reports no remaining P0–P2 findings in the corrected source.

Latest complete verifier evidence:
`artifacts/tdm-awareness-checks/e59c937c662c4eb5a577b762663257da`:
565 assertions, all 20 named mutants rejected, final production run green.
No mod build, deploy, commit
or native GPU test was performed by this leaf; parent integrates and verifies delivery.
GPU acceptance must still show actual search/movement, combat/refills and safe
respawns in the previously quiet USEC/BEAR cases. SAIN can legitimately delay or
refuse search; this change does not promise immediate charging.
