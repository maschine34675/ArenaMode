# Arena double jump

Prepared 2026-09-08 on `feature/double-jump`, based on `62c7b70`.
The user authorized integration into `master` after GPU report `2327f63` and
before the Customs team-start navigation fix. Merge `227fb10` integrated it;
the combined client was delivered locally from clean `master` code commit
`e2859e8`. **GPU acceptance remains pending.** Original isolated preparation
and its unchanged-DLL checks are recorded below; current delivery is in HANDOFF.md.

## Player behavior

- The normal jump binding still performs the normal ground jump. Once airborne,
  press it again for one additional upward impulse. Holding the default click
  binding does not generate a second command; release and press again.
- Only the living current local Arena PMC receives the feature. Bots, ordinary
  PMC/Scav raids, Hideout, spectators and bodies in respawn handover do not.
- A fresh ground takeoff grants one credit; repeated presses, ground-ray flicker,
  same-state airborne re-entry and settings toggles cannot replenish it in flight.
  Landing and the next normal takeoff make the additional jump available again.
- Version 1 deliberately excludes `FallDown` (walking off a ledge), vaulting,
  climbing and a jump already halted by a ceiling collision. It adds no new
  animation, air steering, horizontal boost or fall-damage exemption.
- F12 / Movement / `DoubleJumpEnabled` defaults to true. The existing
  `MovementEnabled` master switch also gates it. Turning either switch on or off
  clears outstanding credits; take off again after changing it midair.
- `DoubleJumpHeightMult` defaults to 1 and accepts 0.25 through 1.5. It scales the
  second jump's ballistic height relative to the initial Arena jump, using the
  square root for impulse. The first jump's `JumpHeightMult`, skill and weight
  factors are already present and are not applied a second time.
- Native stamina eligibility and one normal jump debit still apply. Existing
  infinite stamina and no-fall-damage settings retain their separate behavior.

## Native evidence and implementation

The source of truth is the installed 4.1 game under `Development/_decompiled`:

- `EFT/MoveInputTranslator.cs:53,138`: configured `ECommand.Jump` dispatches to
  `Player.Jump`. Native owner/inventory/exit input restrictions run upstream.
- `EFT/Player.cs:25534`: jump dispatch respects vault-input timing and the
  vaulting animator flag. The extra action reads the same guard; it never edits
  `GlobalConfiguration` or forces `IsGrounded`/`CanJump`.
- `EFT/MovementState.cs:276`: the inherited airborne `Jump()` is empty. The mod
  hooks the non-empty command translator rather than assuming Mono will leave
  this empty method uninlined. Original command dispatch always runs.
- `EFT/JumpPlayerState.cs:82,127,219`: native Enter establishes the vertical
  impulse; animator updates handle ceiling/landing; ProcessJumpMovement computes
  gravity from `_stateTime - _liftDelay` and retains all collision/motion work.
- `EFT/FallDown.cs:5`: derives from JumpPlayerState and zeroes lift. Exact type
  matching deliberately rejects this and other derived movement states.
- `EFT/MovementContext.cs:2805` and `Physical.cs:471,1139`: physical and contextual
  jump restrictions, then one native stamina consumption per accepted action.
- `Stamina.cs:211`: consumption invokes synchronous callbacks, so the mod binds
  a pending command to its exact credit/body/state and rechecks ownership after
  consumption before changing any movement fields.

Credits live in a weak table keyed by the native jump state. Each also owns its
exact player and world. Active game/world identity, current body/context/state,
life, inventory, Arena input blocker, spectator and respawn checks constrain
every request. A prefix captures the eligible credit before native dispatch;
the postfix cannot turn the initial ground press into an extra jump or spend an
old command on a replacement credit. The credit is spent before stamina callbacks,
including failure/re-entry. The post-debit check deliberately does not require
remaining stamina: paying the correct cost may exhaust it.

The accepted action resets the native vertical clock, potential-height prediction,
descending timer and blocked-motion history. It preserves horizontal direction
and magnitude, ground state, gravity, freefall time and fall-height tracking. No
teleport, extra CharacterController.Move, Animator re-entry or native jump
Enter/Exit invocation is added. Native jump noise is requested once; a successful
action has a debug diagnostic, while unexpected exceptions warn once per process.

## Verification and delivery

The complete client builds against installed EFT/Redeploy APIs using
`tools/DoubleJumpChecks/Build-Staged.ps1`. It imports the real project but removes
the external Redeploy project build, references its installed DLL read-only,
overrides client/audio deployment targets and redirects SPT paths to unique
ignored staging. It checks installed client/server/Redeploy hashes before and
after, including failure paths. Use `-SptRoot C:\SPT41` on the GPU host if staging
there is deliberately needed.

Full staged build: zero errors, four existing nullable-annotation warnings.
Independent adversarial review found and resolved the reused-credit/stamina-
callback lifetime issue; re-review found no unresolved P0-P2 issue in the feature
or deployment isolation. The production-linked suite passes 143 assertions and
rejects seven named source faults (scope/ownership, spent credit, body identity,
grounded entry, vertical clock, captured-credit identity and post-debit ownership).
The parent re-runs verification; evidence is recorded in
`tools/DoubleJumpChecks/GATES.md`. The gates use PowerShell 7 (`pwsh`) and .NET 10.
These checks substitute EFT/Unity objects and do not prove native Mono detouring,
animation transitions, collision behavior or gameplay acceptance.

The original feature preparation preserved the old test client
`49762EB638B244F3E2D45ADB3BC040CA6A42FBAF895B7A2A3C80B1CC006839E9`.
After the tests and authorized merge, the combined client was installed locally:
`DEFC7DA21EA311A5078F83037FEA56972C24C36CAE499F5D9FEF7FCC06CE973E`.
Server and Redeploy remain unchanged. The clean-build and delivery manifests,
previous-client backup and GPU mirror limitations are recorded in `HANDOFF.md`.
The merged double-jump checks passed again; no GPU success is inferred.

## Pending GPU acceptance for the merged double jump

1. Start a fresh Arena raid; jump from standing and sprinting, then press again
   during ascent, near the apex and during descent. Confirm one visible second
   impulse and preserved lateral momentum. Holding the default binding and a
   third press must not add jumps. Check a rebound/custom jump binding as well.
2. Land and repeat; test low ceilings, slopes, walls and the area barrier without
   crossing geometry or trapping the player. Walking off a ledge must not grant
   a jump. Test crouch/vault/climb transitions and returning to normal movement.
3. Repeat after death/respawn and Tactical round changes. Buy menu, inventory,
   pause menu and spectator input must not move the player unexpectedly.
4. Toggle the feature/master switch and vary second-jump height. Disabling while
   airborne must not interrupt the native current arc or restore a spent credit.
   With infinite stamina off, check eligibility and the second jump's cost;
   with fall protection off, confirm ordinary falling consequences remain.
5. Check ordinary PMC/Scav and Hideout movement remain unchanged, bots do not
   double-jump, and finish the Arena raid cleanly with successful profile restore.

No GPU double-jump test has been performed on this development host.
