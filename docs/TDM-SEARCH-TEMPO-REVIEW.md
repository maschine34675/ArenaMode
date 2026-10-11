# Independent TDM search tempo — 2026-09-09

## Retest evidence

Frozen client log: `artifacts/bot-movement-improvements/current/LogOutput.log`,
SHA256 `DD4AD680FC48A44776106BE8433E59EC1525B732D4ED019E30C40AF1134DB4E8`.
The match reached ready at 19:19:22 and ended normally at its 600-second limit,
19:29:26, USEC 23:13 BEAR. A native `BotWeaponSelector.OnWeaponTaken` null reference
occurs at 19:29:32 after shutdown began; this log alone does not establish its cause.

Bot 29 has three zero-endpoint-displacement samples covering 91.6 simulation
seconds, with `Freeze`, `DontWantTo` and a search delay around 61–62 seconds.
Selected-opponent heard ages in those samples are 10.6/5.4/11.2 seconds.
The largest delay on an evaluated rejected-search row is 77.6 seconds. Cached
values reach 99.1 seconds on rows whose search predicate was not evaluated.
Endpoint displacement is not total distance travelled or proof of continuous
immobility. The earlier repeatedly invalid navigation origin does not recur:
this log contains one invalid-path sample, with 7.9m endpoint displacement.

Separately, all 17 rate-limited airborne-mantle diagnostic rows report
`unsupported body animator: UnityAnimatorWrapper`. There are no queued or
accepted transitions. The previous FastAnimator-only correction does not cover
this runtime body. The input is arriving; this run cannot validate mantle.
**This bot change does not repair the Unity mantle path.** A Unity transition
needs a separate admission/cancellation design that preserves native state and
root-motion callbacks; do not treat the absence of animation errors as success.

## Behavior change and safety boundary

`TdmSearchTempo` binds the installed SAIN assembly **4.5.1.0** only. The private
instance methods `SearchDecider.shallBeginSearch` and `ShallContinueSearch` take
`Enemy, float, out SearchReasonsStruct.EWantToSearchReason` and return bool.
Both are checked before installing a pair of Harmony prefixes. Partial patch
failure removes only this adapter's hooks. SAIN is optional and is not bundled.

For an exact adopted opposing pair in a live independent TDM match, each prefix
reduces only the second argument to at most **5 seconds**. Shorter, nonpositive
and nonfinite native inputs are unchanged. SAIN still executes the entire
predicate and supplies its result/reason. Its continuation logic further divides
that argument by three. Five seconds is an initial balancing choice, not a
measured optimum or a guarantee that every bot starts moving within five seconds.

No preset, personality, `Info.TimeBeforeSearch`, forget timer, combat goal,
contact flag, path, body position or navigation graph is modified. SAIN retains
healing, reloading, cover, suppression, equipment and path restrictions. The
existing one-shot hearing cue is unchanged. No PFT/BigBrain movement owner is
added. Normal raids, other Arena modes, assembling/countdown/result phases,
stand-down, frozen PFT rosters, replaced/foreign bodies or mismatched native/SAIN
enemy pairs keep their original input. Bot searches remain eligible while the
local player is absent during respawn.

The new read-only `TeamDeathmatchMode.IsSearchActive` does not advance the clock,
score or result pipeline. Ownership also verifies the exact Search/Decider,
BotComponent, physical roster, native enemy dictionary/group and SAIN enemy
wrapper. Exceptions preserve the original argument.

## Observability

`Arena TDM search tempo` reports **nativeArgument/appliedArgument** and predicate,
at most once per physical owner every 30 simulation seconds. Existing awareness
`searchDelay` still reads the unchanged `Info` value; a high number there does
not mean the adapter failed. Look for the new applied-argument line.

`Arena TDM mover detail` captures cached actual SAIN Mover/ActivePath state after
two consecutive measured endpoint displacements of at most 1m at the existing
30-second cadence, at most once per physical bot/raid. It includes path ID/age,
movement/pause/cancellation/recalculation, bounded corner index/position/progress,
start/destination and separate cached/native NavMesh status. Missing and unreadable
paths are distinct. No path calculation, NavMesh sampling or unchecked native
corner helper is called. Reused path objects require the same ID, TimeStarted,
index and corner-list identity/count after reads; changing snapshots are dropped.
Cached `PathInvalid` alone does not establish a bad active path: SAIN's manual
path initializer can retain that cached default alongside a complete native path.

## Verification and remaining acceptance

`tools/TdmSearchTempoChecks/Verify.ps1` links actual adapter and native-pair guard
sources to small independent API doubles; no external AI implementation is copied.
`tools/TdmAwarenessChecks/Verify.ps1` extends existing awareness ownership checks
with passive mover cases. Both have named source-fault negative controls.
The existing TDM runtime regression and installed-API client staging build cover
integration. These cannot certify Unity Harmony detours or SAIN movement in raid.

Root positive checks: 104 tempo assertions and 620 awareness assertions;
their verifiers reject 7 and 33 named source faults respectively. The actual-mode
suite includes explicit search admission in assembling/countdown/live/result and
an overdue-clock read that must not publish a result. Independent final source
review has no unresolved P0–P2 findings; its only diagnostic-registration concern
was corrected and covered by a failing source-fault control. Installed-API client
staging builds with zero errors and four existing nullable-context warnings.

Next user test: restart the game, play independent TDM through local death and
respawn, and compare `Freeze/DontWantTo`, applied search arguments and repeated
low-motion mover details. Confirm bots still reload/heal/take cover. Check native
PMC play remains unchanged. Repeated invalid paths or actual Search stalls need
their new path evidence before selecting a recovery; no teleport is included.
