# Independent TDM search awareness — 2026-09-08

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
