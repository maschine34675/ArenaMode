# Scoped optic pool preload safeguard

2026-09-08; isolated `feature/area-team-setup`, baseline `62d610e`.

## Pass 1: observed failure and native contract

The accepted Customs raid logged a single optic-camera NRE at 07:27:12.675 local
during pool instantiation, 25 ms before the Enforcer kit preload completed.
`CUSTOMS-CONTOUR-REVIEW.md` records the evidence. The missing IL offset means the
exact null dereference is still unproven; this change addresses the independently
verified unmounted-role-model publication hazard, not every optic exception.

Checked in the installed SPT 4.1 decompilation under
`D:\SPT41\Development\_decompiled`:

- `EFT/ObjectsFactory.cs`: `LoadBundlesAndCreatePools` with Raid/Local and General
  priority awaits `InitAndFillPools`. `InitializeAssetsPool` chooses
  `AssetPool<WeaponModPoolObject>` for weapon mods.
- `EFT.AssetsManager/AssetPool.cs`: `Create` registers the exact pool instance in
  the resource's completion source **before** initial-stage work. The synchronous
  `GetOriginalGameObject(parent)` instantiates that resource under `parent`.
- `EFT.AssetsManager/AbstractAssetsPool.cs`: `RunInitialStageBody` creates a
  `PoolContainerObject`, calls that original factory and then deactivates the
  weapon-mod role model. Pool expansion clones the now-inactive model and does
  not use `GetOriginalGameObject` again.
- `EFT.AssetsManager/AssetsPoolStaticConfig.cs`: weapon-mod pools keep objects
  inactive; the parent for original construction is exactly `ParentObject`.
- `EFT.CameraControl/OpticSight.cs`: `OnEnable` clears the reticle, changes the
  lens material and publishes the optic globally. `OnDisable` also clears the
  reticle and publishes a counterpart. Skipping only enable would retain that
  unrelated reticle side effect.
- `EFT.CameraControl/CameraManager.cs`, `method_10`: the published optic's parent
  `WeaponPrefab` is dereferenced without a null guard.
- `Diz.Jobs/JobYield.cs`, `JobAwaiter.cs`, `JobScheduler.cs`: native async pool work
  includes Task awaits and a queued `INotifyCompletion` continuation. A request
  must flow with its execution context, not remain a process-wide boolean while
  the scheduler runs other work.

## Pass 2: implementation and ownership boundary

`KitBundlePreloader.PreloadAsync` owns an `AsyncLocal` request until the awaited
native load completes, faults or cancels. Coroutine polling is not the owner:
abandoning the coroutine does not orphan the native task's protection, and late
completion still cannot publish warmed templates after the existing raid reset.

Lazy Harmony installation targets only the weapon-mod original factory and the
two optic callbacks. During the **synchronous** original-factory call, a thread
frame admits only an active, undisposed, current-generation request with:

- the exact still-current Raid pools object;
- a requested resource (including its subasset), matching asset service and Local
  assembly type;
- the exact pool instance in that Raid resource's completed registration;
- its matching Raid pool parent and the exact original-construction parent.

Only an optic under that parent, without any `WeaponPrefab` ancestor (including
inactive parents), skips its first paired callbacks. The model is not rendered;
its later genuine mounted activation runs all native lens/camera code normally.
An unrelated or nested foreign pool receives no ownership. The synchronous frame
is released by a finalizer that returns the original exception unchanged.

A weak, per-optic marker suppresses only the corresponding disable callback and
is consumed once. Later genuine enable clears it. Reset expires both suspended
requests and disable markers. Normal raids, mounted sights, ordinary item
assembly, pool expansion and camera-manager exceptions retain native behavior.
There is no camera-manager exception handler, prefab mutation, renderer disabling
or global "preloading" flag. One info message per raid records actual suppression.
Missing hook contracts roll back owned patches and warn, preserving native pool
loading. Plugin shutdown should call `KitOpticPreloadGuard.Shutdown` to unpatch;
the integrating root owns that one-line `Plugin.cs` addition.

## Pass 3: checks and evidence limits

`powershell -NoProfile -ExecutionPolicy Bypass -File tools/OpticPreloadChecks/Verify.ps1`
links both changed production sources into a standalone project with no mod
project references or deployment targets. Game/Unity/Harmony doubles replay the
observed original-instantiate/enable/deactivate order and invoke the actual
registered production prefix/finalizer methods. They do not assert that native
Unity, Mono or Harmony detouring was executed.

The checks cover the reproduced standalone publication hazard; current mounted
camera and reticle preservation; mounted and ordinary-raid pass-through; foreign
resource/subasset, factory, parent, pool identity and assembly exclusions;
nested calls; native exception identity; request expiry/replacement; pending,
faulted and canceled registrations without blocking; late mounted activation;
Task and manually scheduled native-style awaiter flow; concurrent unrelated
loads; escaped child continuations; abandoned coroutine polling; failed/canceled
loads and existing template-cache behavior; partial install rollback and shutdown.

The verifier also requires 11 deliberately faulty production variants to fail
at named behavioral assertions: missing normal-raid, resource, exact-pool,
mounted, hierarchy, epoch and disposal checks; thread-local rather than async
ownership; missing finalizer cleanup; missing paired disable suppression; and
removal of the production preloader's request scope. It forces a clean rebuild
of actual production inputs again in `finally`, so a mutant is never left as the
check project's current binary. Success marker: `ARENA_OPTIC_PRELOAD_CHECKS_PASSED`.

The implementation worker ran 67 assertions and all 11 negative controls.
Root still independently reruns the checks, builds the full client into staging,
reviews native hook compilation and verifies delivery hashes. Neither a doubles
test nor a successful build proves the generic Harmony hook works on the game's
Mono runtime or that the original isolated NRE is eliminated.

## Pass 4: adversarial review and pending user test

Self-review added exact registered pool identity (resource matching alone could
admit another pool), null/expired Raid-pool handling, nested-call masking, a
paired weak disable marker and a real queued-await-style context test. Independent
adversarial review is requested from the integrating root before commit/delivery;
its result must not be inferred from these test results.

User GPU acceptance remains pending: restart the game, start an Arena raid with
cold kit optics, allow background kit preloading while aiming through a mounted
optic, then switch/respawn through optic kits. Confirm the diagnostic appears if
an active standalone optic is encountered, no new optic-camera NRE appears, the
current sight does not flicker/change unexpectedly, and ADS works after equip.
Also verify an ordinary PMC raid's optics. No claim is made about the unrelated
inactive `MenuScreen` coroutine error.

Independent integration review (root, 2026-09-08): re-read the actual ObjectsFactory registry/Create ordering, AbstractAssetsPool.RunInitialStageBody and OpticSight callback bodies. The pool is registered before initialization; ParentObject is not yet under ParentTransform when the original is instantiated, and the guard correctly compares those independent identities without requiring that not-yet-established ancestry. Verified mounted optic callbacks still execute native lens initialization and the weak disable marker cannot suppress later mounted activation. No unresolved consequential issue found. Root reran the67 behavioral assertions and11 named fault controls successfully and compiled the real client against installed APIs in isolated staging (only4 existing nullable annotations warnings). Native Mono generic Harmony detouring remains a GPU acceptance item. Plugin shutdown integration is included in the commit.
