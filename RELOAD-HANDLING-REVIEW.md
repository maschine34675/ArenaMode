# Arena firearm reload handling

2026-09-10. Client-only change; native raid acceptance remains pending.

The local living Arena player reloads at 1.5 times native animation speed by
default. F12 / Movement / `WeaponReloadSpeedMultiplier` accepts 1–3; 1 retains
native timing. The Movement master switch applies. A factor change above 1 is
applied to the original speed, never multiplied into the previous frame. After
turning the feature off during a reload, re-enabling takes effect on the next
reload. Server configuration, APBS, skill values and profile data are untouched.

## Included and excluded operations

The exact reviewed native families are external magazine reloads (including
quick/emergency reloads), internal magazines, internal magazines with open bolt,
revolver cylinder reloads, and single-/multi-barrel reloads. Each retains its own
native animation, skill/injury baseline, rounds, cancellation, callbacks and
inventory transactions. Only `SpeedReload` changes on the separate arms and body
animators. This is independent from the existing loose-round magazine loading
speed, draw/holster speed, and medical speed settings.

Underbarrel launchers remain native. Their launcher prefab has an additional
animator (`WeaponPrefab.SetUnderbarrelFastAnimator`) whose timing relationship
has not been validated; speeding only the main hands risks unsynchronized parts.
Manual inventory chamber/insert/remove operations, firing (including bolt cycling),
malfunction clearing, bots, corpse/preview bodies, normal raids and unknown derived
reload operation types are excluded. Global animator speed and firing-rate values
are never written.

## Native contracts and lifetime

The current installed Assembly-CSharp contracts were checked with Mono.Cecil,
not inferred from a successful client build:

- `Player.ArmsUpdate` calls the hands controller's `ManualUpdate` before advancing
  the arms animator. Existing Arena handling ticks acquire the current eligible
  reload there. A body update immediately before the first arms tick can still
  have native timing for that one update.
- `BaseReloadOperation.set_State` is the shared nontrivial lifecycle setter for
  all six included families. Its prefix releases owned speeds before Finished/
  Ready native callbacks, so following firing/idle operations inherit no boost.
- The larger `FirearmController.SetAnimatorAndProceduralValues` contains exactly
  three firearm and two body speed setter call sites. These are wrapped directly;
  correctness does not depend on patching a tiny potentially inlined setter.
  Fresh skill/injury values replace the previous baseline. Native draw arguments
  and native exceptions pass through unchanged.
- `FirearmController.OnPlayerDead` releases before native FastForward; no live
  frame update is assumed after death. Existing Destroy, shutdown, raid reset and
  body/operation/scope checks also release ownership.

Each lease identifies the exact current operation, controller, arms/body wrapper
and native animator. Restoration changes only a still-owned value. A foreign
writer, failed write, identity change or disabled setting blocks further
acquisition for that operation. Already released values cannot be restored a
second time. Failure after one animator write restores the known owned values;
unknown or replaced animator state is never overwritten. Partial patch setup is
rolled back and failed removal cannot create duplicate patches on retry.

## Offline verification

Run from the repository:

```powershell
pwsh -NoProfile -File tools/ArenaReloadChecks/Verify.ps1
pwsh -NoProfile -File tools/ArenaHandlingChecks/Verify.ps1
```

The new executable links the actual production reload source against external
API doubles; it does not duplicate the timing policy. It covers all six included
families, excluded states, reference/body guards, independent animator baselines,
hot changes, skill/injury refresh, native exceptions, foreign writers, pooled
replacement, immediate completion/death cleanup and partial patch installation.
The installed-contract check verifies actual IL and handling integration sites.
Six mutations of temporary source copies must fail their claimed assertions.
The separate existing handling suite checks draw/holster/landing regression.
Evidence is written under ignored `artifacts/arena-reload-checks/` and
`artifacts/arena-handling-checks/`; no check deploys a mod DLL.

These checks do not execute native Unity animations, inventory transactions or
raid gameplay. User GPU acceptance should compare 1 and 1.5 with a magazine rifle,
pistol, internal-magazine shotgun and revolver; include empty and tactical/quick
reload, interruption to fire, damaged arms, death during reload, weapon switching,
and a normal PMC raid after Arena. Native callbacks must remain in order and no
hands softlock, ammunition duplication or firing-rate change may occur.
