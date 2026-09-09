# Arena airborne native mantle checks

Run from the repository root:

```powershell
pwsh -NoProfile -File tools/ArenaAirVaultChecks/Verify.ps1
```

The fixture links both production `ArenaAirVault` files and the full native
`VaultingGameplayRestrictions`. The verifier extracts native component
strategy/request/commit methods, MovementContext.TryVaulting, Player's held
ToggleVaulting dispatch, FastAnimatorProcessor.ProcessState and immediate/
interrupted transition paths, MovementState enter/move/exit callbacks and
MovementContext.ProcessStateEnter. Only the overridden-state Play call receives
a host type cast; native control flow stays intact.

It has no client project reference, deploy target or installed-file mutation.
It requires sibling `_decompiled` sources and .NET 10. Faults rebuild separately
and must fail their named behavioral assertion, not just fail compilation.
Production is run again afterward. Source text is normalized for LF/CRLF when
constructing fault copies; production files are never mutated by the verifier.

## Host fixture limits

Unity raycasts, actual `CanMove` geometry, clip blending, graph data, shared
player scope and movement-state internals remain doubles. The native processor
executes real transition ordering through those services; this proves admission,
callback order and ownership cleanup, not physical climbing or actual stamina
consumption. Native destination-interruption selection is exercised, while the
fixture supplies its already-eligible candidate; it does not simulate the
animation's physical fall detection. Full shared-scope and double-jump behavior
are covered separately by the parent regression checks.

The historical 70-assertion fixture did not execute graph transitions. Its
success, and the absence of errors in a log, did not establish live mantle
acceptance. The user subsequently confirmed failure even when holding vault
after a second Space press. See AIR-MOVEMENT-REVIEW.md for the correction and
installed animator-asset hash.

## Contracts

- Exact live local Arena Jump owner, held input and native forward/phase guards.
  Ordinary raids, bots, stale bodies, inventory/blocked input and automatic
  vaulting retain their native behavior.
- Fresh manual geometry, native stamina and full height-dependent hands checks
  before request and again at animator consumption. Float-setter callbacks
  cannot change geometry, hands, stamina policy or ownership and retain approval.
- Four native parameters refresh for a new valid scan; no repeated TryVaulting
  side effects and no extension of the original 0.5-second expiry. Invalid
  pending requests cancel immediately. Timeout requires release before retry.
- Captured game/world/body/context/component/animator wrapper/native handle and
  FastControllerInfo identity. Replacement objects do not inherit flag writes.
- One exact processor/base-layer/nonrecursive admission lease, cleared even on
  native failure. Existing native transitions and foreign ForcePlay are retained.
- Source is the actual Jump node. The real `Base Layer.JUMP.Jump_Loop` has no
  behavior; only its exact graph identity and verified Jump_Idle incoming owner
  authorize this special case. Foreign/unknown behaviorless leaves are denied.
- Destination comes from the existing native MOVE edge, with the correct bound
  ClimbState/VaultState, template conditions, native blend seconds and offset,
  and native interruption policy. No persistent graph edits/listener ownership.
- Native target entry precedes old Jump movement and runs one complete managed
  Exit/Enter. Jump flag clears only after confirmed owned entry; ordinary failed
  attempts preserve Jump. Blend completion cannot repeat entry or jump motion.
- Pre-entry callback exceptions retire only the injected edge and its pending
  flag; post-entry exceptions retain native state ownership. Exceptions propagate.
  Reset, respawn and controller replacement never authorize replacement writes.

## Native anchors

Paths refer to the installed-version sibling `_decompiled` source:

| Source | Contract |
| --- | --- |
| EFT/Player.cs:25554-25574 | Held vault subscription and ToggleVaulting dispatch. |
| EFT/MovementContext.cs:2814 | Native request, hands restrictions and successful side effects. |
| EFT.Vaulting/VaultingComponent.cs:79-215 | Manual geometry, parameters, flags and ground/air strategy exclusions. |
| EFT.Vaulting/VaultingGameplayRestrictions.cs:31 | Native height-dependent hands/inventory restrictions. |
| FastAnimatorSystem/FastAnimatorProcessor.cs:913-1167 | Transition dispatch, target-before-old-move ordering and interruptions. |
| EFT/MovementContext.cs:2942 | Actual managed Jump.Exit and target.Enter lifecycle. |
| EFT/MovementState.cs:72-99 | Old movement executes only if it is still current. |
| EFT/JumpPlayerState.cs:116-148 | Exit does not consume Jump; native landing normally does. |
| AnimationEventSystem/FastAnimationPlayerStateContainerBehaviour.cs:71 | Graph-to-managed-state binding. |
| EFT/Player.cs:28117, AnimationSystem/AnimatorFactory.cs:15 | Configuration-dependent direct FastAnimator handle. |

## GPU acceptance still required

Restart the game after comparing the installed client hash with HANDOFF.md.
Test valid low/high obstacles while airborne, including prolonged/double jumps,
low stamina, obstruction/roof limits, missed attempts, held/released vault,
landing, respawn and a normal PMC raid. A failed attempt must leave movement
usable; an accepted mantle must not trigger an extra jump afterward. Confirm
native vault fall/interruption during a longer frame if practical.

Bounded Info diagnostics distinguish rejected native checks, queued requests,
accepted native Climb/Vault, timeout and unsupported animator/entry. There is no
Animator.Play, fake grounding, teleport, global graph mutation or collision
bypass. Successful offline checks remain separate from GPU acceptance.
