# Arena landing, weapon handling and airborne actions

**Historical retest, 2026-09-09 19:19–19:29:** airborne mantle remains unaccepted.
The player has `UnityAnimatorWrapper`; the later FastAnimator transition bridge
rejects it before queuing. No successful transition is logged. Earlier broad
playtest acceptance does not cover mantle. See `TDM-SEARCH-TEMPO-REVIEW.md` for
the frozen log evidence. The Unity implementation below supersedes that code;
it still needs its own GPU acceptance.

Implementation: 2026-09-09, on `feature/arena-air-movement`, inheriting the
independent TDM match flow. This is client-only work. Server settings, profiles,
AI, area barriers, loadouts and Redeploy are unchanged.

## Unity airborne mantle implementation (2026-09-10)

### Physical follow-up after the 19:40 retest

The user confirms the animation now starts but the body usually drops back down.
Frozen log: `artifacts/mantle-physical-retest/c023e425c1a74a0eaf7fa29fa620c96d/client.log`,
SHA256 `83D5A349CA7B6B72429943A245175B5A0F2CDA11D229F12FC35CB4403C239B6A`.
Eight actual Unity Climb entries are recorded (19:33:38 through 19:38:31).
Entry alone does not prove completed traversal. The old log has no entry height
or physical motion outcome; its 1.733m geometry line belongs to a rejected request.

One concrete mismatch is established: Arena expands climb height from 1.31m to
1.70m, while the native standard controller's Climb_on blend tree stops at 1.30m.
The actual `Climb_on_130` clip travels only 1.299624m vertically and 0.605196m
forward; neither ClimbState nor VaultingMovementState scales that travel to the
expanded admission height. `character_animations.bundle` SHA256:
`1434CB5B88871E0276CB26277438364D62600DC4F4B9EAA2F352FB791773C04E`.
The native installed `globals.json` ClimbSettings.MoveRestrictions.MaxHeight is
1.31. GridOffsetY is zero; the actual `playersuperior.bundle` VaultingGridRoot
is directly under Player with zero offset, identity rotation and unit scale.
GridPointsModel converts hits to grid-local coordinates; ObstacleCalculatorModel
uses their maximum Y. Thus this is remaining height from the current airborne
body, and it decreases as the jump rises.

Additional airborne Climb requests now wait until that remaining height is at
most **1.31m**, retaining the native 1cm tolerance. Held input retries naturally;
no release latch, hands notification or committed animation is created while
the ledge is too high. The same bound is rechecked before dispatch. Native vault
preference, standing reach, global settings and the scan itself are unchanged.
This fixes an unsupported admission range; it is **not yet proof** that height
explains every reported drop. No speculative root-motion scaling, teleport,
gravity reset or collider adjustment was added.

`Arena mantle motion #...` records up to twelve accepted attempts per world,
separate from the admission log budget. Entry includes cached native height,
length, grid-local distance, obstacle/body/grid world positions, forward vector
and scan velocity. Distance is not a direct body-to-ledge distance: the grid moves
forward with velocity. ApplyMotion observers compare incoming motion with actual
controller displacement. Exit and a sample 0.75s later record state, grounding,
rise, capsule height and cumulative requested/applied motion. An eight-second
timeout, shutdown, death, scope/controller/world replacement release observation
only; the native move retains all flags and physics ownership. Logging cannot
change native exceptions or movement, and samples never write physical state.

Native source also rules out an ongoing jump-gravity write during Climb:
MovementContext.ProcessStateEnter exits Jump before entering Climb; outgoing
OnStateMove is guarded by current-state identity, and VaultingMovementState's
ApplyMotion bypasses ordinary gravity. CrossFade duration/offset and root-motion
flags match the installed native graph. The two 19:36:55/56 NREs concern optic
activation during icon capture; the 19:38:40 NRE is SAIN OnDisable on unload.
None of these stacks establishes a mantle connection; no unrelated fix applied.

Offline checks include the production observer with extracted native ApplyMotion;
collision responses and actual Unity clips remain controlled host doubles.
Tests cover height boundaries/retries/refresh, ordinary standing/vault isolation,
requested versus accepted movement, post-exit drop, stale ownership, original
exceptions, timeout, per-world bounds and shutdown. GPU test still required:
hold vault while approaching the same ledge in a jump, compare low/high ledges,
then inspect entry/exit/after-exit lines if the body drops back down.

Full offline verification passed: **363 assertions / 47 source-fault controls**.
Evidence: `artifacts/air-vault-checks/a17fde8e2deb4c0580e27b3ea4918584/`.
The native physical signature was independently inspected in the installed
assembly; `motion` is a by-ref Vector3, read by value in the observer prefix.

### Native FallingDown flag correction after the 19:09 retest

Frozen evidence: `artifacts/reload-mantle-retest/58b6d72db3324285838ec897c2104dfe/client.log`,
SHA256 `17D6D61FC0BE42CB95C1ACF509ABCBE84C41AAD41B82323A76E0C56DB1E1355C`.
The 100 mantle lines contain 49 queued Climbs, 47 dispatch refusals, three native
check refusals and one geometry-refresh refusal. All 47 dispatch records show
a real Move/Sprint jump with no transition, IsJumping=true, Landing=false and
FallingDown=true. The old FallingDown-bool guard alone prevents these dispatches.

Native `MovePlayerState.UpdatePosition` sets FallingDown when leaving the ground;
only `FallDown.Exit` clears it. Jump, Climb and Vault do not. Inspection of the
actual standard Unity controller found no FallingDown/FallingDownFloat conditions
on any transition, selector or AnyState entry. Thus that bool is not a valid
test for the current managed FallDown state. Only this bool guard is removed;
the exact JumpPlayerState type, its initialized jump-leaf owner, !Landing,
IsJumping and absence of a current native transition remain required. The bool
itself is never rewritten. Native geometry, stamina, hands and barrier checks
still run, and a real FallDown subtype remains excluded.

The 16 Auto/Manual x Climb/Vault x four-jump-leaf fixture cases now start with
the observed stale FallingDown=true. They also assert it remains untouched.
New tests reject a real FallDown owner and exercise alternating queue/refusal
logs. Logging now limits each stable reason separately per world, retaining the
100-line world budget; alternating messages can no longer bypass the 2s delay.
Independent source review found no remaining P0-P2. GPU acceptance is pending.

Full verification passed: 339 production-linked assertions and 41 separately
compiled source-fault controls, including reintroducing the stale flag guard and
breaking the alternating-reason throttle. Evidence:
`artifacts/air-vault-checks/4d6b0bff6d614094af3fa3bc1ab04f38/`.

### Moving/sprinting jump correction after the 17:18 retest

The 17:18:45 archive loaded the Unity hooks, but recorded 16 misleading
`unsupported body animator: UnityAnimatorWrapper` refusals, five native-check
refusals and two queued Climbs without a recorded dispatch/entry. The frozen
log is `artifacts/mantle-moving-source/retest-20260910-171845/client.log`, SHA256
`66E91D32EF0789E83CE377A3EDE8FDCFE38CF1E7A8355E0D45F821E33EC6DFF4`.
No state hashes were logged, so the exact cause of each attempt is unknown.

Inspection of the actual standard Unity controller (bundle hash below) found
two omitted native jump leaves: `Jump_Move` (1056621489) and `Jump_Sprint`
(1332272753). Idle, Move and Sprint containers all initialize the same cached
managed Jump state; incoming MOVE/Sprint transitions use these omitted leaves.
The correction validates the exact current leaf's own initialized container,
instead of accepting only Idle/Loop or borrowing the Idle container. Loop alone
retains its verified behaviorless Idle-owner exception. Landing, falling,
transition, geometry, hands, stamina and current-body restrictions remain intact.

Bounded rejection diagnostics now distinguish admission from queued dispatch
and record source/next hash, transition, update mode, jump phase and native flags.
Dynamic values cannot defeat the existing reason throttle, and logging failures
cannot change gameplay. No scheduling or native transition timing was changed.

The linked fixture passes 320 assertions and 39 required source-fault controls,
including Auto/Manual updates across all four jump leaves and both destinations,
missing/foreign Move/Sprint owners, source changes while pending and throwing
diagnostic sinks. Independent adversarial review found no remaining P0-P2.
These checks do not prove native Unity physics or live mantle acceptance.

### Original Unity bridge

Client-only, on `feature/arena-mantle-reload`. The last user log (2026-09-10
11:06:50 archive) still showed five deliberate UnityAnimatorWrapper rejections.
The new Unity path removes that unsupported-animator failure for the reviewed
body controller. **A build and offline contracts do not yet prove live climbing.**

The actual installed `player_anim_controller.bundle` was inspected read-only,
SHA256 `9A6B93737D806D6A563BD1AFCBDFB77538DFCCE917C9D1DB0E2BE2E1F39CDD9F`.
Its Unity controller has Jump_Idle (232800732), behaviorless Jump_Loop
(1889265959), Climb_on (-285230529), and Vault (262098243). The jump leaves
have landing exits but no climb/vault entry. This is separate from FastAnimator's
serialized graph; Unity does not have FastAnimator's timed Idle-to-Loop edge.

`Player.BodyUpdate` submits one native CrossFade before manual body animation
updates and also runs when Unity owns automatic animation updates. Admission
requires the existing exact local living Arena Jump owner, held vault and
forward input, fresh native obstacle geometry, stamina and complete native
hands/inventory restrictions. Existing transition, Landing, FallingDown, missing
IsJumping, disabled/uninitialized animator, stale body/controller and foreign or
missing state containers reject the move. No shared controller asset is edited.

The wrapper, underlying Unity Animator, runtime controller and initialized
movement owner are bound to the request. Destination identity is checked against
both the actual state hash and initialized ClimbState/VaultState. CrossFade uses
the native MOVE entry's normalized blend and destination offset. Unity itself
runs PlayerStateContainer.OnStateEnter, Jump.Exit, Climb/Vault.Enter, root motion,
collider changes, stamina effects and exit cleanup. No manual Enter, Play,
Animator.Update(0), teleport or fake grounded flag is used.

CrossFade is the commitment boundary. Before it, input release, setting changes,
geometry loss and the original 0.5-second request deadline cancel safely. After
it, native movement and sibling animation-event behaviors own the move; releasing
Space does not rewind an accepted climb. A finalizer observes actual native entry,
with a polling fallback that also verifies the animator destination and absence
of movement overrides. It then clears the old IsJumping flag and releases pending
double-jump precedence. Captured movement identity cannot be replaced in-place.
Cleanup still runs if the movement setting changes after dispatch. Native callback
exceptions remain visible; partial native entry is not logged as clean success.

A delayed native entry reports once after the original deadline and retains its
observer until entry or body/state replacement. It never repeats CrossFade or
rewinds a possibly executing native move. Post-dispatch errors do not discard
that observer. Before native confirmation, a held request retains precedence over
double jump; real Jump.Exit retires that jump's credit normally.

The production-linked fixture now exercises extracted native Player.BodyUpdate
and PlayerStateContainer.OnStateEnter in addition to the FastAnimator pipeline.
Its Unity engine seam controls callback evaluation without pretending to execute
Unity. It covers Auto/Manual updates, both jump leaves, both destinations,
pre-admission refusals, native full state handoff, post-dispatch release/disable,
late entry, native errors and replaced bodies/controllers/containers. Native
physics, clips, sibling event timing and actual Unity scheduling remain GPU tests.
An independent adversarial review found no remaining P0-P2 after lifecycle fixes.

GPU acceptance: approach a climbable ledge during a real jump, hold the existing
vault binding (with jump-on-release, press/hold again in flight), and verify a
complete native climb and subsequent normal movement. Compare ordinary standing
vault, unsupported walls/ceilings, empty stamina, reload/meds rejection, releasing
before/after commitment, double jump away from a wall, death/respawn and an ordinary
PMC raid. Expected success log: `Unity entered Climb through native state callback`
or `Unity entered Vault through native state callback`; repeated delayed-entry
messages or only `queued`/`transition requested` are not acceptance.
## Player controls and defaults

The F12 `Movement` section has five new settings, all covered by its existing
`Enabled` master switch:

| Setting | Default | Effect |
| --- | --- | --- |
| `LandingRecoveryMultiplier` | `0.5` | Halves the duration of the native hard-landing speed limit; does not reduce its strength or fall damage. Range 0.1–1. |
| `WeaponDrawSpeedMultiplier` | `1.5` | Speeds up firearm draw animations. Range 1–3. |
| `WeaponHolsterSpeedMultiplier` | `1.5` | Speeds up firearm holster animations. Range 1–3. |
| `AirVaultEnabled` | `true` | Allows an explicit native vault/climb request while jumping toward a valid obstacle. |
| `AirHipFireEnabled` | `true` | Allows conventional firearm hip fire during a genuine jump, without enabling airborne aiming. |

Use the game's existing vault/climb binding while moving toward the obstacle.
There is no new key, automatic ledge catch, teleport or artificial grounded flag.
A queued native mantle takes precedence over double jump and the new hip-fire
exception. The native animation graph still decides whether to enter the mantle.
An unaccepted request expires after 0.5 seconds and requires releasing the vault
binding before retrying. The ordinary standing vault path is unchanged.

Drawing and holstering are each 1.5 times their native speed by default, so the
animation portion takes roughly two thirds of its usual time. This does not
change global animator speed, rate of fire, reloading, malfunction clearing,
medicine/grenade handling or equip pipeline/loading time. Initial pre-raid and
in-flight replacement-body draws deliberately retain native handling when the
live local-body guard is not yet satisfied.

Walking off a ledge into the separate native falling state and the underbarrel
launcher operation are outside this first hip-fire extension. Native ammunition,
trigger, malfunction, inventory and weapon-specific restrictions remain in use.

## Native contracts and ownership

Read the installed decompiled sources under `../_decompiled/` before modifying
these hooks. `Player.LandingAdjustments` applies the height-based speed limit;
the separate `MovementContext.OnJumpEnd` penalty is not the requested hard-fall
duration and must not be shortened incidentally.

Firearm draw/holster operations use `FirearmsAnimator.SpeedDraw`, including native
ergonomics/fast-switch inputs. Only a currently executing local firearm operation
may own an adjustment. Native refreshes update its baseline without multiplying
an already scaled value. Operation change, setting change, teardown and raid end
release that ownership; restoration must preserve a subsequent foreign write.
Initial acquisition replaces calls inside the large native draw/holster methods
to avoid relying on the tiny common `Start` method being patchable on Mono. The
native speed-refresh wrapper is also small and could inline. If a mid-operation
injury/skill refresh bypasses its hook, the controller update recognizes the
foreign parameter write and relinquishes the boost safely; that particular
operation may finish at native speed. No global animator hook is added to hide
this unverified coverage limitation.

The native jumping state has no vault action. `VaultingComponent.CanVaulting`
also rejects airborne movement, and `GetVaultingStrategy` excludes the jump
animation. The additional request is scoped synchronously to the exact player,
movement context and vaulting component. It retains native obstacle geometry,
clearance, stamina and full hands/gameplay restrictions, including a final check
after the native manual-vault rescan. Pending requests must not execute stale
obstacle parameters or survive a body, world, session or eligibility change.
Once native vaulting has entered, its flags belong to the native state machine.

Air fire replaces exactly one `SuitableForHandInput` call inside
`Player.FirearmController.Idling.SetTriggerPressed(bool)`. It does not patch the
small shared getter or change the global unsuitable-state list. The original
predicate runs first and retains its result whenever already true. Only its
false result for the exact live local `JumpPlayerState` can be extended. Missing
or ambiguous call sites keep the method untouched and produce one diagnostic.
Native firearm operations still own shot execution and ammunition consumption.

`ArenaMovementScope` requires the current started local Arena game/world and
exact living local player body. It denies AI, stale bodies, respawn, spectator
and exit/stand-down contexts. Input extensions also require closed inventory
and unblocked Arena input. All temporary state must be released independently
of the movement master switch, so turning it off cannot prevent cleanup.

## Offline verification and its limits

The production-linked checks are executable with:

```powershell
pwsh -NoProfile -File tools/ArenaHandlingChecks/Verify.ps1
pwsh -NoProfile -File tools/ArenaAirVaultChecks/Verify.ps1
pwsh -NoProfile -File tools/ArenaAirFireChecks/Verify.ps1
pwsh -NoProfile -File tools/ArenaAirFireChecks/Verify-Integration.ps1
```

Each feature suite includes deliberate source faults that must fail named
behavioral assertions and a final run of unchanged production sources. Native
installed assembly contracts and a staged real client build supplement the
host-side models. The integration runner also covers the existing double-jump
behavior and actual TDM menu/settings. Run these sequentially because some
regression projects share their build output directories.

The host-side Harmony instruction/owner-access doubles do not execute Harmony
inside the game's Mono runtime. Extracted native methods do not execute Unity
physics or the animator graph. Neither offline success nor compilation proves
the airborne climb transition, animation timing or visible live shot behavior.
See the dated delivery entry in `HANDOFF.md` for final evidence and client hash.

Historical initial verification (f53bffa, 2026-09-09): handling 161 assertions/12 source faults,
air vault 70/13, hip fire and shared scope 92/10, double jump 144/8 and native
TDM menu/settings 80/8 all passed, including final production reruns. Parent
independently repeated both delegated suites. Evidence is retained in ignored
`artifacts/`:

- `arena-handling-checks/de3ccca726234be8afbe816287bdf7a9/`
- `air-vault-checks/ef9ec58df7ea422599c721714e3022f6/`
- `air-fire-checks/985dd9fa07954dc986921d003e1a97a9/`
- `double-jump-checks/e74ef2e66e7e4a65a6baf84e4a78d1f0/`
- `tdm-match-menu-checks/92b31c4bebec4c97b7ed302781cc3439/`

Independent four-pass review found no remaining P0–P2 after the pending-geometry,
exact-target cleanup and transactional weapon-speed corrections. The complete
final source also built against the installed client APIs with zero errors and
four existing nullable-context warnings, without changing installed artifacts.

## User GPU follow-up, 2026-09-09

The earlier broad user report was initially recorded as acceptance of all four
changes. The later explicit report that airborne mantle still fails retracts
that part of acceptance. The other three changes remain user-reported working.
The 12:28–12:43 client log confirms successful
registration of all eleven handling/vault/fire patches and contains none of the
new movement failure/fallback/timeout messages. The TDM completed its ten-minute
limit normally; all three player respawns completed in 0.76–0.80 seconds.

This records user feedback, not proof of the mantle transition or a measurement of
animation timings or separate proof for every regression case below. The same
run has one recovered initial bot activation failure and one SAIN unload-time
exception; the server confirms the profile was restored and persisted. See the
dated `HANDOFF.md` entry and `artifacts/movement-playtest/20260909-1228/` evidence.
Movement production sources are unchanged; no corrective patch was inferred
from the unload-time stack alone.

## GPU regression checklist

Compare the installed `C:\SPT41` client hash against the dated handoff, restart
the game, then test an Arena with the same weapon/obstacle and movement settings:

1. Compare a hard landing at multiplier 1 and 0.5. The movement slowdown should
   recover in half the time; normal short jumps, fall damage and unrelated
   effects must retain their previous behavior.
2. Draw and holster at multipliers 1 and 1.5, including pistol/primary and fast
   weapon switches. Check reload, firing and malfunction animations afterward;
   toggle the settings during a switch and check for stuck or persistent speed.
3. Jump toward several valid low and high obstacles and explicitly use vault.
   Check insufficient stamina, crouch/prone/inventory, a disappearing or moving
   obstacle, a missed attempt and an area barrier. Native standing vault must
   still work; a failed aerial attempt must leave no flags stuck at landing.
4. Fire single and automatic shots while jumping, including a sprint jump.
   Check ammunition is spent normally, empty guns remain empty, reload and
   malfunction handling still work, and aiming is not unlocked in the air.
5. Combine double jump with attempted vault/hip fire; a pending mantle must not
   spend the extra-jump credit. Check another attempt after landing and respawn.
6. Disable movement during each action, end the raid, and enter an ordinary PMC
   raid. Verify native handling and movement there and unchanged bot behavior.

If air vault never enters despite valid geometry, collect the bounded
`Arena airborne mantle` diagnostics and a short recording. Native geometry,
clip blending and physical collision still need the GPU test; do not infer
success from hook registration or an empty error log.

## Airborne mantle correction after failed playtest

The user also tried releasing Space to jump, then pressing and holding it again
in flight. Installed Control.ini uses Jump=Release and Vaulting=Continuous;
this is not explained by merely forgetting the second press.

The installed `player_anim_controller.animatorcontrollerdata.bundle` has SHA256
`2896AFC2C1C4AF8F36758B89EB34AEAB94D97545B27FE650DD80A31968CE0DF3`.
Sequential native binary parsing found 97 parameters, 245 states and 496
transitions. Its DoClimb/DoVault entrances originate only from standing/moving/
sprinting states, never from Jump. The original queued flags therefore lacked
an animation path. The old offline fixture did not exercise that graph.

The correction scopes a single temporary edge to the captured FastAnimator
processor, controller-state instance, base layer, exact live Jump owner and
one pending held request. A native transition always wins. A real manual
geometry rescan, same strategy/stamina and full hands restrictions run again
at animator consumption, including validation after native float callbacks.
Moving to another valid obstacle refreshes the four native parameters without
repeating TryVaulting side effects or extending the original 0.5s expiry.

The edge targets the existing native MOVE-to-Climb/Vault destination, retaining
native height variants, blend seconds, destination offset and interruption
settings. It is not inserted into any stored graph list and owns no shared
parameter listeners. Normal ProcessState enters the target before dispatching
old Jump motion; MovementContext runs the real Jump.Exit and target.Enter.
Jump's animator flag is consumed only after verified native entry (Jump.Exit
alone does not clear it). A pre-entry exception removes only the exact injected
transition, preserves native errors and requires release before retry. Once
native entry starts, no generic state/capsule rollback is attempted.

`Base Layer.JUMP.Jump_Loop` is a real behaviorless leaf, reached from Jump_Idle.
It deliberately keeps that managed Jump owner. Only this exact graph leaf and
its verified incoming Jump_Idle owner receive the exception; arbitrary
behaviorless states do not. Non-FastAnimator bodies retain native behavior and
produce a bounded diagnostic. No Animator.Play, teleport, grounded override,
profile change or bot movement patch is used.

The updated checks link both production files and extract native ProcessState,
ProcessImmediateTransition, interruption selection, MovementState callbacks,
MovementContext.ProcessStateEnter and held ToggleVaulting dispatch. Graph data,
Unity geometry, animation blending and native state internals remain host
fixtures: offline success is not visual or in-raid acceptance.

Final correction verification (2026-09-09): airborne mantle 120 assertions and
25 separately compiled source faults passed, followed by a final production
run. Hip-fire/shared scope (92 assertions/10 faults), double jump (144/8) and
TDM menu (80/8) regressions were repeated successfully. Final source builds
against the installed game APIs with zero errors and four existing nullable
warnings; staged builds leave installed client/server/Redeploy files unchanged.
Independent final review found no remaining P0-P2 for the exact source hashes
below, including the weak diagnostic-world reference and stage-aware logging:

- `ArenaAirVault.cs`: `AFC80B1FEDE073C1EA78D9E215AF3CFCD630C965B42C2EFE32F72F64127FA6CE`
- `ArenaAirVault.Animator.cs`: `5F9E38A0F3EA071F84A4B6446D2270B85F77B692F79ED2E128663566CAA450E3`

Evidence: `artifacts/air-vault-checks/477a9030a7a04d5ba3963129aa164463/`,
`artifacts/air-fire-checks/6ec54b73c8e942f985a4b0f83a1d21b4/`,
`artifacts/double-jump-checks/9293e07d3b9b4f7fba2a30d92c609610/` and
`artifacts/tdm-match-menu-checks/cf3d9606391e46baaba941f1de66fa35/`.
GPU acceptance is still pending; the dated handoff records client delivery.

Verified offline on 2026-09-10 before delivery: 227 production-linked assertions
and 35 required source-fault controls passed. Recorded evidence:
`artifacts/air-vault-checks/bc51c539feb54a04bc81c177c809c551/`.
This count includes both FastAnimator and Unity fixtures, not native GPU tests.
