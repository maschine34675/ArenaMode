# Arena double jump and deliberate ledge jump

Updated 2026-09-12 on `feature/arena-visibility-movement`. The user explicitly
reversed version 1's exclusion of walking/falling off a ledge. This source change
is staged for later GPU acceptance; it has not replaced the running r3 client.
Original integration/delivery history appears at the end.

## Player behavior

- The normal binding still performs the normal ground jump. A deliberate further
  press provides one additional vertical impulse during ascent or descent. A
  deliberate press after walking off a ledge is now allowed too, including the
  small drop before EFT switches from Idle/Move/Sprint to FallDown.
- The allowance belongs to one uninterrupted airborne episode of the exact living
  local Arena body/context/game/world. Changing between native Jump and FallDown,
  entering the same state again, repeated presses, a nearby-ground ray, upward
  contact or changing settings cannot replenish a spent allowance. Actual native
  controller contact during landing restores one allowance. A newly replaced
  body/session, including a body first observed already falling, starts fresh.
- Only the living current local Arena PMC receives the feature. Bots, ordinary
  PMC/Scav raids, Hideout, inventory/UI-blocked input, spectators and respawn
  handover remain excluded. Native stamina, medicine, prone, mounted, ceiling-
  halt and vault guards remain. Pending Arena mantle input retains precedence;
  this change does not alter any mantle distance, height or geometry limit.
- `MovementEnabled` and `DoubleJumpEnabled` retain their existing defaults and
  gates. A toggle while airborne invalidates the outstanding allowance; toggling
  while standing on confirmed ground permits the next airborne action normally.
- `DoubleJumpHeightMult` remains 0.25–1.5, default 1. It scales ballistic height
  using the square root for lift. Existing first-jump height, weight/injury and
  strength factors apply once. Brief landings with a reused native Jump object
  reuse its original lift basis, so repeated episodes do not compound strength.
- Existing FallDown and Jump states receive a direct renewed vertical clock and
  exactly one normal jump debit. Input in early Idle/Move/Sprint requests the
  native jump animation once and reserves the allowance for 0.5 seconds. A real
  Jump or FallDown entry confirms that request and owns the native debit. An
  absent/expired/cancelled transition grants no mod impulse or mod debit; its
  reservation stays spent until landing. This native transition needs GPU testing.
- Holding a click binding does not synthesize repeat presses. No air steering,
  horizontal boost, new collision movement, teleport or fall-damage exemption is
  added. Native fall-height and freefall accounting remain untouched.

## Native evidence and ownership

Installed SPT 4.1 source under `Development/_decompiled` is the reference:

- `EFT/MoveInputTranslator.cs:53,138` dispatches configured `ECommand.Jump` to
  `Player.Jump`; native UI/input ownership runs upstream. `MovementState.Jump`
  is empty, so the patch stays on the non-empty translator and always runs the
  original command. Capturing before it prevents a ground press from becoming
  an extra press after an animator transition.
- `EFT/Player.cs:25534` guards vault timing and the vaulting animator flag.
  `EFT/IdlePlayerState.cs:120` and `MovePlayerState.cs:396` additionally require
  pose above 0.6 and `IsGrounded`; Sprint inherits Move. These grounded checks
  explain why a centimetre-scale ledge departure could swallow the input.
- `EFT/MovementContext.cs:2805` (`TryJump`) checks contextual/physical CanJump,
  emits noise, then sets the jump animator bool. `PlayerAnimator.cs:406,822`
  confirms this bool persists until cleared; it is not a one-frame trigger.
  The early-edge path only borrows a previously-false bool. Successful FallDown
  completion, expiry and cancellation release that owned bool on the same exact
  current body/context/game/world. Native Jump entry takes ownership itself.
  A preexisting native bool and a replaced body are never cleared. Cleanup also
  handles an Idle-to-Move/Sprint transition and synchronous noise subscribers
  that cancel/complete the request before native TryJump finally sets its bool.
  A new grounded command settles the old air reservation before vanilla input,
  so delayed landing observation cannot clear that new native command's bool.
- `EFT/JumpPlayerState.cs:82` computes skill, speed/weight/injury lift and calls
  `MovementContext.OnJump` (`MovementContext.cs:1627`), which owns native stamina
  consumption (`Physical.cs:1139`). `FallDown.cs:12` calls that whole entry and
  then zeroes lift. The extra FallDown action reconstructs just the documented
  lift formula; it never calls Enter again or duplicates its physical/animator
  side effects. `ArenaMovementPatches.ArenaMovementJumpHeightPatch` applies existing Arena
  height; the Jump entry postfix runs last to retain that native lift basis.
- `EFT/JumpPlayerState.cs:127,219` keeps all HeadBump, Halt, LimitMotion,
  ApplyMotion and gravity work. Only vertical lift/clock, potential-height
  prediction, descending timer and stale blocked-motion history are renewed.
- `EFT/MovementContext.cs:2574,2644,2681` observes flying after ground collision.
  Its IsGrounded combines controller contact with a proximity ray. The allowance
  reset additionally requires actual `CharacterController.isGrounded`, a
  supported landing/locomotion state and no upward/push phase or pending vault.
  No IsGrounded, CanJump, global physics or GlobalConfiguration value is forced.
- `Stamina.cs:211` invokes synchronous subscribers. Direct requests are marked
  spent before debit, bind the exact credit/body/state revision, and revalidate
  after payment without demanding remaining stamina again. Native entry captures
  are immutable, so nested entry of the same object cannot reuse an outer capture.

The live owner is one current credit plus a conditional weak table keyed by
MovementContext. Shutdown drops the strong owner while retaining the spent value
only as long as its live context exists. Raid end clears credits and leaves weak
world/game tombstones, denying stale requests before singleton teardown without
retaining the departed raid. Actual landing replaces the credit, invalidating
any previously captured command. World/body replacement never transfers an old
pending command or its cleanup to the replacement body.

## Offline verification and pending delivery

Run `pwsh -NoProfile -File tools/DoubleJumpChecks/Verify.ps1`. It compiles the
production file with narrow EFT/Unity contract doubles, runs behavior assertions,
then compiles deliberately faulty source variants and requires each to fail its
named behavioral assertion. It restores the production-linked helper afterward.
`tools/DoubleJumpChecks/GATES.md` records the completed count and evidence path.
The final leaf run passed 278 assertions and rejected all 22 compiled faults:
`artifacts/double-jump-checks/0a7474de55034562a9a4aaa313679ea1/`.
Independent native/source re-review passed on production source SHA256
`21F4F97E6EE79A765C19CDA8742E40B05FF4A2C20DC60E6A6E818A345327452D`.

The checks cover ordinary-raid and exact-body exclusion, normal/falling/early-edge
input, actual/failed/cancelled native transitions, native flag ownership, one
stamina debit, repeated state changes, ray/upward contact, real landing, replacement,
settings/shutdown, nested callbacks and lift stability across reused states.
They do not execute native Harmony/Mono, the animator graph, actual contact or
visual motion. Root performs the isolated full client build and separate review;
this leaf does not invoke Build-Staged, main builds or any deployment target.
The installed r3 client/server/dependencies/audio and r3 GPU instructions are
outside this change's delivery scope.

## Pending GPU acceptance for this revision

1. In a fresh Arena, use the configured jump binding during ascent, apex and
   descent. Walk off a very small step and a taller ledge, both standing and
   moving/sprinting, and press once. Confirm a single visible air impulse with
   preserved horizontal momentum; extra presses must not keep gaining height.
2. Exercise the early ledge case before the fall animation, including Idle-to-
   Move/Sprint. Confirm one native debit and no delayed second jump after a
   FallDown completion. Test a blocked/failed native transition without repeated
   debit or an animator jump stuck on after landing.
3. Land briefly and step off again while the previous animation settles. Each
   new episode must have the same configured strength. Test slopes, low ceilings,
   walls and the area barrier without bypassing collision or trapping the body.
4. Exercise pending vault/climb, inventory/buy menu, death/respawn, spectator and
   Tactical round transitions. Test setting/master toggles on ground and in air;
   neither a toggle nor a native state change may refill a spent airborne action.
5. With infinite stamina/fall protection disabled, check the accepted jump cost
   and unchanged falling consequences. Check ordinary PMC/Scav/Hideout and bots
   remain unchanged, then complete Arena with successful profile restoration.

No GPU acceptance is claimed for this revision or the earlier double jump.

## Historical version 1 delivery

Version 1 was prepared 2026-09-08 on `feature/double-jump` based on `62c7b70` and
integrated by merge `227fb10`. The combined local client from `e2859e8` had SHA256
`DEFC7DA21EA311A5078F83037FEA56972C24C36CAE499F5D9FEF7FCC06CE973E`.
Its prior preserved client was
`49762EB638B244F3E2D45ADB3BC040CA6A42FBAF895B7A2A3C80B1CC006839E9`.
Those historical hashes are not the current running r3 artifact. Version 1's
original restriction to a native grounded takeoff is superseded by this revision.
