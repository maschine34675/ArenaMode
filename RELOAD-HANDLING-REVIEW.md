# Arena firearm reload handling

2026-09-10. Client-only change; native raid acceptance remains pending.

## Weapon comparison and bounded diagnostics (2026-09-10)

The user first reported no difference between slider values, then confirmed that
acceleration does work. Their subsequent VSS/SVDS/Saiga-9/AK103/HK416/AKS-74 fast
versus MCX .300/AKM/RD-704/VPO-209 slow comparison used **one fixed setting across
different kits**, not matched same-weapon timing at 1 versus 3. It does not by
itself show a defective multiplier; native reload durations remain different.

Read-only inspection of actual Unity controllers found the primary reload phases
bound to `SpeedReload` (compiled hash 2571298643, Float, default 1) in both groups.
RD-704 and AKS-74 even share the reviewed START/OUT/IN/END speed multipliers and
transition timing. AKM and VPO-209 use the same controller. No alternate channel,
speed-parameter animation curve or separate compiled activation flag explains the
reported split. Bundle SHA256s for repeatable asset inspection:

- RD-704: `D187D3414726B0144AF4A245AD211945C19A68DB545FF9C4CC28F8A454B174D4`
- MCX: `7CCFE72AB81105B04C89957A65D43ABC119FE933721BCC0EB18B488FC8E0E8AB`
- AKM/VPO controller: `DA9200839B3DEF7EB04F0258C786F78489B99CCE4D31C19DF8DC450EF5B8EE01`
- AKS-74n controller: `CD19679731B7D86EB2B09EA98A2D30461DEFFD1CB5ECD72BFA200D632AB29C34`

The inspected native path has no equipment-weight, armor, rig or ergonomics term.
`EFT/SkillManager.cs:1870` computes reload speed from weapon skill and the bot
bonus; the weapon skill contributes 0.004 per level. `EFT/Player.cs:12579` returns
that speed, and `SetAnimatorAndProceduralValues` uses 1 for injured arms or the
skill speed otherwise. Magazine `magAnimationIndex`, mastery and reload type
select animation variants. `LoadUnloadModifier` is for individual cartridge
packing/unpacking, not firearm reloading. This does not exclude another installed
mod changing behavior and does not establish Arena speed on every tested weapon.

The timing policy is therefore unchanged. New `Arena reload diagnostic` records
identify weapon template, operation, configured factor, original/written/read-back
arms/body values, native Unity state and observed elapsed time. Unity diagnostics
read `AnimatorStateInfo.speedMultiplier` directly: EFT's generic state wrapper
discards it and cannot prove that a speed multiplier is absent. Requests are
labelled as requests, followed by readbacks at ticks 2/8/30, factor changes,
rejections and native completion. Elapsed time starts at observation, can include
interruptions/pauses and is not a precise clip-duration benchmark.

Diagnostics are read-only, exact-local-Arena scoped, weakly held and capped at
96 lines per raid, with unchanged reasons emitted once per operation. Factor 1
can be observed as the native control. Failing getters/log sinks cannot decide
speed admission or interrupt cleanup. F12 changes are live; the web configuration
is deliberately not polled during active Arena and applies on a later raid.

For a useful next comparison, keep the same weapon, magazine, ammunition and
equipment and compare matching tactical or empty reloads at F12 factors 1 and 3.
Use e.g. AKM plus AK103 as a control; change the value between reloads. Only then
change equipment weight while preserving the weapon and reload variant.

Final offline checks: 724 production-linked assertions and eight required source
fault controls passed, plus 161 handling assertions and twelve handling faults.
Evidence: `artifacts/arena-reload-checks/e78ee16bf4b54491a0814034c409ab98/` and
`artifacts/arena-handling-checks/b0e6b91a87144fe59c3814142c10f1e5/`.
Independent adversarial review found no P0-P2. Body state-speed details were
omitted after review because its movement layer is not the reload layer; the
body's actual `SpeedReload` value is still recorded. This is diagnostic delivery,
not proof that every listed weapon has passed a GPU timing comparison.

## Existing timing policy

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
