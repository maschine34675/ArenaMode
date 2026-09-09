# Arena landing, weapon handling and airborne actions

**Latest retest, 2026-09-09 19:19–19:29:** airborne mantle remains unaccepted.
The player has `UnityAnimatorWrapper`; the later FastAnimator transition bridge
rejects it before queuing. No successful transition is logged. Earlier broad
playtest acceptance does not cover mantle. See `TDM-SEARCH-TEMPO-REVIEW.md` for
the frozen log evidence. A safe Unity transition/cancellation path remains open;
the subsequent bot search-tempo change does not repair it.

Implementation: 2026-09-09, on `feature/arena-air-movement`, inheriting the
independent TDM match flow. This is client-only work. Server settings, profiles,
AI, area barriers, loadouts and Redeploy are unchanged.

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
