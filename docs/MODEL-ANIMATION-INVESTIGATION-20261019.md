# Native animation failures: source attribution, 2026-10-19

Source-audit leaf 1.2. Arena base `36e72b9`, Redeploy `d1d7240`, Killcam
`8c8ee45`; native source is `../_decompiled/` for EFT 0.16.9.5.40743.
This leaf changed documentation only. No production edit, build, deployment,
GPU manipulation or commit was performed by the leaf. Current native comparison
and binary identity verification belong to the integrating agent.

## Result and evidence boundary

No gameplay cause in ArenaMode, Redeploy or Killcam is established for the
UtilityOperationEvent timeout, hash-zero event or null-take-to-hands record.
There is one proven defect in Arena's diagnostic attribution: it calls a body
unregistered when its optional native body-profile field is empty, even when an
explicit live Player owner was supplied. Correct that evidence defect before
using the context to decide a gameplay repair. Do not lengthen the native
utility timeout, synthesize animation events, insert a hash-zero no-op handler,
skip rejected hand changes or suppress the teardown warning.

Evidence was recovered by the integrating agent under the ignored directory
`artifacts/model-errors-20261019/`: `historical-errors.json` contains 74 source
records, including one JetMaterial record after the audited window;
`historical-context.json` contains the 21 diagnostic records. This audit uses
the original follow-up window ending 15:11:31 on 2026-10-18. The following are
original source records, not independent gameplay-failure counts. All listed
times are host local, UTC+02:00.

| Family | Recovered observations | What that establishes |
|---|---|---|
| UtilityOperationEvent | Eight records in PFT Tactical; round 1 at 15:06:09.554/.554/.712, round 2 at 15:07:30.877/.884, round 3 at 15:08:41.197/.220/.322 | CheckMagazine timed out on eight active AI bodies, not merely inactive pool models. The first three precede the local death at 15:06:28.047. |
| Missing hash handler | Three records at 15:02:17.488, 15:02:27.014, 15:02:49.953 in Deathmatch | All precede the first recorded death replay starting 15:02:50.786. No owning animator, event name or stack is recorded. |
| Null take to hands | One record at 15:06:20.623 in Tactical | No item, owner, failed predicate or caller is recorded. It precedes the local death and next-round rebuild. |
| Animator.GotoState on Synchronize layer | Earlier 2026-10-15 report: eleven warnings during Zombie exit at 18:34:11.826-.834; separate fresh-process retest reports 32 teardown warnings | Historical report evidence only in this leaf; no recovered object/caller stack. Native teardown is correlated, but ownership is unresolved. |

The timeout contexts give elapsed values 2.504997, 2.507997 or 2.508997 seconds,
`role=ai`, active hierarchy, `inPool=False` and `stub=False`. The eight weapons
span six templates: Benelli M3, Remington 870, MP9, AK-74M, AKS-74UB and MP7A2.
This rules out an explanation restricted to one Benelli asset or only bodies
rebuilt after the first round. It does not rule out shared event data, a common
native/third-party path, or an indirect Arena timing interaction.

## Exact native paths

### UtilityOperationEvent

1. `BotMagazineChecker.ManualUpdate` (`../_decompiled/BotMagazineChecker.cs:15`)
   calls `WeaponManager.ShootController.CheckAmmo`; `PatrolMoveSimple.Update`
   calls the checker. `BotSecondWeaponData.ManualUpdate` is another native
   CheckAmmo initiator. The old context cannot identify which caller fired.
2. `Player.FirearmController.CheckAmmo` (`EFT/Player.cs:13842`) stops ordinary
   interactions and dispatches to `CurrentOperation.CheckAmmo`.
3. `Idling.CheckAmmo` (`Player.cs:5731`) checks the real weapon/magazine and
   inventory actions, then calls `FirearmsAnimator.CheckAmmo` (a native trigger),
   sets the external-magazine parameter and starts
   `UtilityOperation(CheckMagazine)` unless using a stationary weapon.
4. `UtilityOperation.Start` (`Player.cs:11373`) sets Executing and zeroes its
   timer. `OnUtilityOperationStartEvent` changes it to Ready; an IdleStart event
   then finishes it and starts Idling. The timeout Update (`Player.cs:11468`)
   logs the original error only while still Executing and outside inventory,
   after `_utilityTime > 2.5f`; native recovery sets Ready and invokes IdleStart.
   Thus the record means the StartUtilityOperation event did not reach this
   operation in time, not that a 2.5-second reload simply ran slowly.
5. The event path is `AnimationEventsStateBehaviour` ->
   `AnimationEventsContainer.CheckEventsTime` -> `AnimationEventsEmitter`.
   `WeaponPrefab.Init` creates a fresh emitter and subscribes the
   `FirearmsAnimator.AnimatorEventHandler` (`WeaponPrefab.cs:384-405`).
   EmitOnDemand queues the events; `Player.ArmsLateUpdate` calls
   `HandsController.EmitEvents` (`Player.cs:29137`).
   `ObjectInHandsAnimator.AnimatorEventHandler` forwards to the native table.
   Table entry `1134400241` calls `IEventsConsumer.OnStartUtilityOperation`,
   then `FirearmController.IEventsConsumerOnStartUtilityOperation`
   (`Player.cs:13213`) calls the current operation.
6. `Player.ArmsUpdate` (`Player.cs:29064`) runs the hands controller update
   and, for Manual mode, the arms animator and late event emission. In Auto
   mode the animator/ComplexLateUpdate route is separate. A component's
   `enabled=False` alone therefore cannot prove stalled animation: native
   Manual update deliberately disables the automatic Animator component.

The diagnostic captured neither event binding nor animator progress. Plausible
failure boundaries are a trigger that never enters the intended state, absent
or rejected event data, a stale/missing event consumer, lack of animator/event
updates, or another operation/body taking ownership. None is proved by elapsed
time and active hierarchy alone.

### FATAL no handler for hash zero

`AnimationControllerEventsTable.AnimatorEventHandler` (`../_decompiled/
AnimationControllerEventsTable.cs:125`) logs this exact string when the supplied
integer is in neither the dispatch table nor the name table. Zero is absent
from both tables. A valid event with an empty consumer list does not produce
this message: its known table handler simply iterates an empty list.

`AnimationEvent.FunctionName` computes zero for an empty/null name, but the hash
is also a serialized field; `Deserialize` reads it verbatim, and `Clone` copies
it verbatim (`AnimationEventSystem/AnimationEvent.cs`). Therefore zero can
represent blank or inconsistent serialized event data; it is not proof of an
absent `OnStartUtilityOperation` subscriber. The three own source trees contain
no writes to the event hash/name fields or native dispatch table. No recovered
record ties hash zero to the later Utility timeout family. Treat them separately.

### Error null take to hands

`Player.SetItemInHands` (`Player.cs:31467`) emits the same error for any failed
entry predicate: null item, unsuccessful `item.CheckAction(null)`, an inventory
already changing weapons, or buffer-zone hand restrictions. The wording does
not establish a null item. It then invokes the supplied callback with null.

Native callers include `SetSlotItem`, grenade input in `PlayerInputTranslator`
and Hideout paths. Arena's direct caller is `WeaponSwapService.BeginDraw`
(`ArenaMode.Client/Combat/WeaponSwapService.cs:526`), used by Gun Game promotion;
the observed event is Tactical, so that direct route is not supported by this
record. Core's protected auto-stow uses `SetEmptyHands`, gated on
`!InventoryController.IsChangingWeapon`; it does not directly call the reported
entry point. Indirect inventory or timing effects require the actual rejected
predicate and caller, not inference from the log wording.

### Animator.GotoState during exit

Two native reset paths can reach Unity's Play/GotoState machinery during pool
return: `PlayerPoolObject.ReturnToPool` ->
`AnimatorDefaultStateCache.SetupDefaultParameter` ->
`DefaultLayerState.SetDefaultState` -> `Animator.Play`, and
`WeaponPrefab.ReturnToPool` -> `ResetAnimator` -> `ResetStatesToDefault`, which
plays each nonzero cached layer default (`WeaponPrefab.cs:443-518`). The native
default-state cache also plays every configured layer, without a synchronized-
layer filter. The existing message names neither of these call paths, however.

Arena corpse release invokes native Player.Dispose/DestroyLoot; Core invokes
native body creation and teardown; normal raid cleanup also returns models.
These are possible callers, not proof that our code supplied an invalid state.
Do not skip/reset a different layer globally. Capture the exact animator,
controller, layer index, requested state hash and managed reset caller before
choosing a repair. A successful exit is not proof the warnings are harmless.

## Own-mod and PFT mutation audit

| Boundary inspected | Actual mutation and scope | Attribution consequence |
|---|---|---|
| Arena draw/holster | `ArenaHandlingPatches.TryFactor` admits only exact local live SpawnOperation/Remove, writes SpeedDraw, restores exact animator/value ownership at completion/change/destroy. | It does not admit AI UtilityOperation or write global animator speed. |
| Arena reload | `ArenaReloadHandling.TryFactor` admits exact local live reload/magazine operations and leases SpeedReload for arms/body. ManualUpdate is patched for every controller, but its lease admission still rejects AI. | Seeing this patch on the native call chain does not mean AI reload/check animation was accelerated. |
| Local medical/Akimbo/air movement | Medical speed requires exact main-player MedsController and rejects AI; Akimbo requires exact local firing operations and restores its Unity speed lease, or scales only the matching FastAnimator update. Air-vault uses current local-body scope. | Direct AI CheckMagazine acceleration is excluded. Pool carry-over is a separate testable concern, not inferred from these local writes. |
| Bot movement/agility/tempo | `ArenaBotMovement` scales X/Z DirectApplyMotion and a temporary controller cap. Agility substitutes locomotion operands. Tempo modifies SAIN search/strafe and completed-reload wait tail; it does not alter firearm/utility time or force completion. | No direct animator speed/update/event writes. Indirect changes in movement, sight distance or AI action ordering remain testable with BotMovementEnabled off. |
| Zombie body culling | Writes BodyAnimatorCommon culling only for eligible generic infected in exact Zombie scope; release restores native near-camera AlwaysAnimate. Arms are not modified. | Does not explain firearm AI in the recorded Tactical raid. The source has no own-mod `AlwaysUpdate` control; the relevant native value is AlwaysAnimate. |
| General native culling | `Player.VisualPass` uses distance/visibility and sets body/arms AlwaysAnimate or CullUpdateTransforms. | Active GameObject and Player flags do not identify current animation progress. CullUpdateTransforms alone is not proof events stopped. |
| Arena disable sites | The menu's disabled Animator belongs to cloned inert mode buttons. CorpseCleanupService.StopTicking disables a settled corpse Player, not a live bot. Physical release disposes it and uses native pool ownership. | These sites must not be generalized into a live-bot Animator disable. Recycled nested weapon views still merit exact-instance tracing if evidence implicates pooling. |
| Redeploy creation | RespawnEngine selects Manual arms only when native UseHandsFastAnimator is enabled; otherwise Auto, with Auto body. This matches BaseLocalGame's normal local-player spawn selection. Native LocalGame bot creation uses Auto/Auto. | The chosen update mode is not itself a proven Core mismatch. Core's local replacement does not rewrite AI update modes. |
| Killcam | GhostPuppet sanitizes a clone under an inactive holder. LivePlayerHider changes renderer/light enabled flags; it does not disable live Player/Animator components or alter animation speed. | Renderer visibility can interact with native culling; measure state/event progression if a future timeout overlaps replay. Current first-round Utility samples and all hash-zero records precede the relevant recorded replay/death. Earlier same-process pool history is not excluded by this timing. |
| PFT 0.10.3 source | BotOwnerManualUpdatePatch is a postfix; follower sprint patches adjust movement intent. Follower weapon-selection policy can change which native actions run. Arena's PFT rebind disposes retired/revoked exact bodies and calls native activation for fresh bodies. | No reviewed PFT path directly writes weapon animation speed/event hashes. Old diagnostics do not identify companion versus opponent, so do not label all eight as PFT followers. Compare exact bot membership first. |

Primary own-code boundaries: `Movement/ArenaMovementScope.cs:15`,
`Movement/ArenaHandlingPatches.cs:98`, `Movement/ArenaReloadHandling.cs:100`,
`Movement/ArenaBotMovement.cs:69`, `Movement/ArenaBotAgility.cs:47`,
`Bots/ArenaBotTempo.cs:147`, `Bots/ZombieBodyCullingRuntime.cs:198`,
`Bots/CorpseCleanupService.cs:1489/1709`,
`Compat/PitFireTeamRoundLifecycle.cs:3905`,
`../Redeploy/Redeploy.Core/Engine/RespawnEngine.cs:177`,
`../Killcam/src/Replay/LivePlayerHider.cs:126` and `GhostPuppet.cs:48`.

## Proven diagnostic correction and discriminating controls

The diagnostic bug is exact: native `Player.Init` passes an empty string to
`PlayerBody.Init` (`Player.cs:28204`), which stores that value in
`_playerProfileID` (`EFT/PlayerBody.cs:564`). Arena's `Report`
(`Diagnostics/ArenaNativeModelDiagnostics.cs:198`) uses only that string for
`world.GetEverExistedPlayerByID`; it ignores the explicit operation.Player for
this lookup, and labels a missing lookup result `unregistered`.

Correct the report by preferring an explicit/hierarchy owner only after proving
`ReferenceEquals(owner.PlayerBody, body)`. Use that owner's nonempty ProfileId
to perform independent exact-body checks against the ever-existed and alive
registries, and record whether RegisteredPlayers contains that same instance.
Retain the raw body field as separate evidence. With no usable identity report
`identity-unavailable`/`not-checked`, not `unregistered`. Do not repair registration
or alter the body in this diagnostic path. Check against a real local AI with
empty body field plus matching owner/registry, a replaced owner, a menu preview,
and a genuinely absent registry body.

Necessary fault-only probes, still bounded by DeveloperMode and the exact
admitted Arena world, are:

1. Utility start/timeout: exact operation/controller/body/held item identities,
   BotOwner state and companion/opponent membership; animator wrapper/type,
   native enabled/update mode/culling/speed, current and next state hashes with
   normalized time, and exact event emitter/consumer binding. A start/end pair
   must preserve enough state to distinguish frozen progress from missing event
   data. Capture expected event name/hash/conditions from the affected behaviour
   without mutating it. Avoid per-frame scene enumeration or unlimited stacks.
2. Hash zero: observe before native `AnimationEventsEmitter.SendEvent` dispatch,
   record FunctionName, supplied hash, state hash and emitter animator. Retain
   the original table call/error. Determine whether the event data is already
   zero before any own-mod lifetime transition.
3. Null hands: capture original SetItemInHands arguments/caller and the native
   failed predicate at the error branch. Do not invoke `CheckAction` a second
   time merely for logging; that would add work or side effects and might observe
   a different result. Preserve the callback and error path exactly.
4. GotoState: bracket the two native reset routines with bounded object/state
   context, or collect a managed warning stack where the runtime supplies one.
   The warning alone cannot identify a repairable owner.

Priority native comparisons for the integrating agent:

- Fresh-process ordinary PMC with the same weapon families and equivalent
  native bot CheckMagazine opportunity. No occurrence without an observed
  CheckMagazine is not a successful negative control. Presence shows the
  failure does not require active Arena; it is not a mod-free attribution.
- Fresh-process Arena Tactical first round with replay disabled and
  BotMovementEnabled false, then an otherwise matching run with bot movement
  enabled. Keep PFT settings, weapons, map, runtime and bot membership recorded.
  Compare successful and timed-out operations rather than total error counts.
- Same-process rounds and a separate fresh process: identify every body,
  weapon-prefab and animator instance before/after native pooling. This tests
  carry-over that the first-round chronology alone cannot exclude.
- If the utility event exists and state time advances through its event point,
  check binding/conditions/delayed delivery. If state time does not advance,
  isolate the exact update/visibility owner. Only restore a missing update or
  binding once its own-mod write/lifecycle break is demonstrated. If native
  assets already contain the bad hash or lack the event in the control, report
  that limitation; do not rewrite common native animation data speculatively.

## Four-pass leaf review and handoff

1. Complete trace: all four requested signatures, restored historical records,
   direct animation writes, body/weapon pool boundaries and PFT were inspected.
2. Domain review: distinguished operation timeout from reload duration; known
   handler dispatch from event consumers; Auto from deliberately disabled Manual
   animator updates; and Player owner identity from optional PlayerBody metadata.
3. Adversarial review: first-round errors prevent a rebuild-only claim, earlier
   process history still permits pool carry-over, renderer-only replay changes
   are not declared universally harmless, and bot role does not prove follower
   membership. No missing-error count is claimed as native acceptance.
4. Final pass: checked exact source anchors and recomputed the historical counts;
   production edits/builds/GPU tests are explicitly absent. A1 and A2 have manual
   source-audit evidence in this document; gameplay-cause attribution remains
   unresolved pending the named discriminating controls, not silently closed.

Parent must independently verify leaf gates and decide the scoped diagnostic
repair/native comparison. This report does not claim any animation failure fixed.
