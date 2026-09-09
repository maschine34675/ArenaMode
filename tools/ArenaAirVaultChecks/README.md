# Arena airborne native mantle checks

Run from the repository root:

```powershell
pwsh -NoProfile -File tools/ArenaAirVaultChecks/Verify.ps1
```

This standalone console fixture links the actual `ArenaAirVault.cs`. It also
extracts the installed-version decompile's native component strategy/request/
commit methods and `MovementContext.TryVaulting`, and compiles the full native
`VaultingGameplayRestrictions` class. It has no client project reference,
deploy target or installed-file mutation. It requires the repository's sibling
`_decompiled` directory and .NET 10.

The fixture deliberately doubles Unity, the shared live-player scope, raycast
models' `CanMove` results, animator transitions and stamina delegates. It proves
that the production hooks respect those results; it does not prove scene rays,
native Animator graph connectivity, real input ordering or Unity movement. The
parent movement checks separately exercise the actual shared scope and
double-jump integration.

## Verified contract

- Only the exact live local Arena body's held native vault input can obtain an
  airborne strategy, and only in an actual `JumpPlayerState` (not walk-off fall,
  head bump, prone, mounted, stationary or overridden state).
- Native forward/sprint/terrain/stairs/animation/stamina/geometry restrictions
  remain; inventory, meds, food, weapon operations and grenades still pass
  through native gameplay restrictions.
- Manual geometry is refreshed before native gameplay checks and again by the
  native request. Final commit rechecks native height-sensitive restrictions.
- The lease is synchronous and always cleared. Other actors and ordinary or
  automatic strategy queries cannot inherit the airborne exception.
- A queued native flag reserves precedence over double jump. It is rechecked,
  canceled on lost input/eligibility/geometry, and canceled after 0.5 unscaled
  seconds without animation acceptance. Timeout requires releasing vault before
  retrying. No obstacle does not reserve or consume double jump.
- Pending geometry must still match the native committed parameters and world
  obstacle point (1cm dimensional/point scan tolerance, 0.01 behind ratio and
  0.05m/s forward velocity tolerance). Native `CanMove` still applies unchanged;
  those tolerances never enlarge native obstacle limits.
- The lease binds exact game, world, body, movement context, component, animator
  wrapper and native animator handle. Replacements drop ownership without
  writing replacement flags. Entered native vault/climb states own their own
  flag, stamina, capsule and cleanup even when the feature is later disabled.

## Native source anchors

Paths are relative to `../_decompiled` and correspond to this checkout's game.

| Source | Contract |
| --- | --- |
| `EFT/Player.cs:25542` / `:25569` | Native vault toggle and held update dispatch; the nonempty update hook avoids patching an empty inherited state method. |
| `EFT/MovementContext.cs:2814` | Native gameplay restrictions, component request and successful handoff side effects. |
| `EFT.Vaulting/VaultingComponent.cs:79` / `:103` | Native manual request and geometry refresh. |
| `EFT.Vaulting/VaultingComponent.cs:113` / `:192` | Normal ground/jump exclusions; airborne explicit strategy preserves the other restrictions. |
| `EFT.Vaulting/VaultingComponent.cs:138` / `:172` | Native animator parameters and requested vault/climb flags. |
| `EFT.Vaulting/VaultingComponent.cs:215` | Automatic strategy remains unmodified. |
| `EFT.Vaulting/VaultingGameplayRestrictions.cs:31` | Native height-dependent inventory/hands restrictions, exercised as actual source. |
| `EFT.Vaulting/VaultMoveModel.cs:23`, `ClimbMoveModel.cs:22` | Native obstacle, roof, wall, distance, size and landing restrictions remain authoritative. |
| `EFT/VaultingMovementState.cs:44`, `EFT/ClimbState.cs:13`, `EFT/VaultState.cs:18` | Native state entry owns the actual move, collider setup and stamina. |
| `EFT/Player.cs:25136`, `EFT/PlayerAnimator.cs:312` | Native body animator can change with the spirit body; bind both wrapper and returned handle. |

## Evidence and review

2026-09-09: verifier exited 0 with `ARENA_AIR_VAULT_CHECKS_PASSED`:
70 assertions passed, 13 separately compiled production-source faults were
rejected by their named behavioral assertion, then production passed again.
Evidence directory:
`artifacts/air-vault-checks/9ecca087c7ad4bf7bcc904d40551d1dc`.

Faults cover removed scope, held input, native climb geometry, native stamina,
final hands recheck, synchronous lease cleanup, pending geometry validation,
game/world identity, finite parameters, animator wrapper/handle and component
identity. Build success is required before every fault run; a compiler error
cannot stand in for a rejected behavior.

Review passes: (1) native input/strategy/geometry source trace; (2) production
eligibility and double-jump precedence; (3) pending lifecycle/epoch/animator
ownership and stale-geometry adversarial review; (4) actual linked/extracted
production checks plus independently compiled fault controls. Independent final
read found no remaining P0-P2 findings against production SHA256
`714417AB4B9675C6017563E215D3042587B0190907A715EB06A8C292617B8800`.

## GPU acceptance remains required

The native Animator may not accept Jump-to-Climb/Vault from every jump phase.
There is deliberately no forced graph state, fake grounded flag, teleport,
controller move, global setting mutation or collision bypass. During an actual
native animation blend, pending geometry is not rewritten; entered state
ownership is never canceled. Acceptance timing and scan tolerances must be
checked in-game. An unsupported transition safely times out and requires a
fresh press, but offline checks cannot prove the requested gameplay is visually
successful. Verify ordinary grounded mantle, airborne mantle from varied jump
phases, no-obstacle double jump, blocked roof/wall cases, held versus released
vault, low stamina and respawn/raid-end cleanup on the GPU machine.
