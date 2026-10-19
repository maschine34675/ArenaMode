# Native model fault context — 2026-10-18

This implements the focused diagnostic recommendation from
[the log-source audit](LOG-SOURCE-AUDIT-20261018.md). It does **not** claim to
repair the unresolved model/animation defects, or establish which mod owns
them. Native errors, their fallbacks and original exceptions remain intact.

Enable **Diagnostics → DeveloperMode** before entering Arena to collect the
additional `[dev] Arena native model fault` records. The default remains off.
The parent plugin registers `ArenaNativeModelDiagnostics.Enable()` and invokes
`Shutdown()` when unloading. No new user setting or global logger filter is
introduced.

## Exact observations

| Native seam | Observation and original behavior |
| --- | --- |
| `EFT.PlayerBody.OnDestroy()` | Prefix reads the same `_active` flag that causes the native undisposed-body Error, before native destruction logging. Also captures a bounded managed destroy stack. It never calls Dispose or destroys anything. |
| `FirearmsEffects.Init(Transform)` | Postfix captures the exact returned `_muzzleManager` when its `JetMaterial` remains null. Native Init emitted its own Error; UpdateMuzzle does not assign this material. An Init that throws before returning does not execute this observer. |
| `WeaponPrefab.Parent(IPlayer)` | Finalizer reports only an actual exception and returns that exact exception object. Records requested-player presence, prefab and weapon template; never installs animator parameters or changes hierarchy activity. |
| `Player.FirearmController.UtilityOperation.Update(float)` and `LauncherUtilityOperation.Update(float)` | Observers are inserted immediately before the two existing native timeout Error literals. Both native logging calls and Ready/Idle fallback remain. Normal Update frames never call the observer. |
| `CustomItem.Init(PlayerBody)` and `CustomItemSkin.Init(Skeleton, PlayerBody)` | Prefix reports an `_itemType` absent from the actual `CustomItem.InterestSlots` table, preserving native initialization and its Error. Reports invalid clipping data, not an invented replacement type. In CustomItemSkin, native Skin.Init still runs first inside the original and can independently fail. |

The hooks were checked against `Development/_decompiled` and the **installed**
`EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll`. The harness reads the DLL's
metadata and IL without loading Unity. It verifies every exact parameter list,
the four reflected field types, native error literals, the 2.5-second utility
threshold and the absence of exception handlers in the two instrumented methods.

Utility requires the exact two known literals and refuses changed instructions
or exception-block layouts. Branch labels move to the inserted observer, so a
native branch cannot jump past it. All original instruction instances are kept.
This is deliberately narrower than observing Unity logging or all animator calls.
The expert review rejected an earlier Update-prefix approach: Idling.Update
runs before the timeout condition and can change behavior or fail. Merely seeing
an overdue timer before that call is not proof that native timeout logging ran.

## Ownership, context and cost

Every callback first checks DeveloperMode, before reflection, hierarchy reads,
stack capture or payload creation. Admission then requires either:

- the exact active Arena `AbstractGame` + `GameWorld` pair; or
- a server-confirmed pending Arena arm/token, a `LocalGame` outside stopped/
  stopping states, and the nonempty armed map equal to the current world's map.

The loading path is read-only and refuses uninitialized map IDs. Normal PMC,
menus, mere last-Arena history, stale game/world pairs and map/token mismatches
are not admitted. Consequently this does **not** instrument pre-confirmation
menu previews; the earlier preview NRE cannot be attributed from this probe
unless it recurs in admitted loading/active code.

Each report includes map, mode, native phase, exact component ID/name, up to
seven ancestors with activity, scene, body/profile/customization, parent Player
and local/AI hint, weapon template where available, and asset-pool state when
present. Since the 2026-10-19 correction, `bodyProfile` retains the raw optional
body ID; `ownerProfile`, `ownerBodyMatch`, `lookupProfile` and `lookupSource`
explain the chosen identity. A valid owner supplies its profile only when its
actual `PlayerBody` is the exact reported body. Otherwise the raw body ID is
used, if available. Native `Player.Init` deliberately supplies an empty body ID;
an empty ID alone is not evidence that a bot is absent from the world.

`currentWorldBody=same/different/unregistered/destroyed-player/not-checked` compares the actual
body reference with the world's ever-existed Player for the selected profile.
`aliveWorldBody` separately uses the live-player lookup; `registeredBody` tests
the exact body reference in `RegisteredPlayers`, including retained managed Player
wrappers. A lookup returning a destroyed wrapper is `destroyed-player`, not
`unregistered`. Missing identity and destroyed bodies report `not-checked`
without a registry query. These are observations of
three different registries, not a claim that ever-existed means currently alive.
A stale or destroyed owner cannot lend its identity to a different body. A
shared profile ID or AI flag alone never labels a clone as the live body.
Destroyed Unity wrappers retain their managed ID/profile context, without
dereferencing their unavailable Transform.

Utility faults also capture the operation type/state/elapsed time, the actual
arms update mode/queue, exact current hands/operation/item/animator references,
controller disposal flag, emitter mode and counts, consumer ownership, animator
backend/name/settings, and current state hash/normalized time for at most layers
0 and 1. Next-state information is read only during a transition. A disposed or
Unity-destroyed controller stops further detail; a missing, destroyed or
uninitialized Unity animator stops before setting/layer-state getters. A
partially unavailable utility read appends `utilityReadFailure` without dropping
the collected identity or disabling later reports. No Update, Play, reset,
parameter setter or event dequeue is called. In particular the FastAnimator
`runtimeAnimatorController` getter is deliberately not used: native code resets
state and logs from that getter. The observed speed setting and one fault-time
snapshot do not prove animation progression or its cause.

There are at most **two reports per family/object signature**, **24 contexts per
raid**, and one cap notice. IDs and short signatures, not body references, fill
the bounded dictionary; game/world keys are weak references. Respawning the
local body does not renew this budget. Details and the disposal stack are built
only after admission and budget allocation. The original native alarm is never
limited. An exception in the diagnostic's own metadata read emits one opt-in
notice and disables this observer until plugin restart, rather than creating a
recursive error storm. Gameplay and native logging remain enabled.

## Checks and limits

Run:

```powershell
pwsh -NoLogo -NoProfile -File tools/NativeModelDiagnosticsChecks/Verify.ps1 -Mutants
```

The production file is linked into an executable fixture: **138 assertions**
cover disabled/ordinary/stale/loading contexts, healthy paths, all fault
families, unchanged native alarms/exception identity and operation state,
per-object/raid budgets, respawn identity, destroyed wrappers, current-world vs
same-profile clone bodies, diagnostic self-failure and shutdown. The production
utility transpiler is executed on a branch-equivalent dynamic method, including
both alarm branches and an ordinary no-alarm path. Actual DLL metadata/IL is
checked separately; the fixture is not a claim to execute Unity player models.

Seventeen behavioral mutants must fail: missing opt-in, stale-world admission,
loading-map bypass, swallowed original exception, removed per-signature and
per-raid caps, lost branch labels, replacement of original instructions, lost
owner fallback, stale-owner borrowing, unknown identity mislabeled unregistered,
destroyed-body lookup, destroyed registry result mislabeled unregistered,
uninitialized/destroyed Unity animator state reads,
unbounded layer sampling, and detail evaluated after budget exhaustion.
Production is rerun afterwards. The real client also compiles against installed
game DLLs using the explicit NoDeploy target override; see
[the repair evidence](MODEL-DIAGNOSTIC-REPAIR-20261019.md). Native gameplay
validation remains the parent's separate gate; do not deliberately construct
invalid live models to trigger these diagnostics.

Known limits remain visible: no global Animator.GotoState or hash-zero event
capture, no claim about warnings hidden by Killcam's existing synchronous clone
logging window, and no guessed attribution to Arena, Redeploy or Killcam from
a timestamp alone.

## Four-pass review

1. Scope: one diagnostic module and its harness/documentation; no native state,
   profiles, cameras, renderers or logger preferences modified.
2. Native expertise: exact source/API emitters checked; utility prefix rejected
   in favor of the actual error branch; Harmony's supported six-parameter Patch
   overload selected; private fields verified by installed DLL metadata.
3. Adversarial review: stale ownership, ordinary PMC, nil tokens/maps, respawn
   budget renewal, destroyed wrappers, read exceptions, missing IL anchors and
   branch labels exercised. Native errors and fallback instructions survive.
4. Polish/evidence: bounded lazy details, body/customization/pool provenance,
   explicit uninstrumented cases and production-linked mutation checks; native
   runtime evidence remains a separate parent gate.
