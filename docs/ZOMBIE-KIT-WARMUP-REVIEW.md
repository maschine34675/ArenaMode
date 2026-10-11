# Zombie kit prewarming — 2026-10-09

## Change and boundary

Solo Zombie survival has one life. Arena kits are drawn once for the opening
body; a living intermission re-applies that same kit, and own gear never draws
an Arena kit. Loading other catalogue entries is therefore unused work.

Two independent seams previously allowed it:

1. `KitBundlePreloader.BeginRaidStart` obeyed `WholeCatalogue` for Arena-kit
   Zombies. It now caps that mode's requested scope at `InitialKit`. `Off`
   remains off, and the selected kit is still drawn/reserved by the existing
   `KitPreloadPlan.OpeningKit` call. Own gear and protected PFT already return
   before the draw and retain that behavior.
2. `ArenaController.TickKitWarmup` scheduled all kits after eight seconds when
   `RandomKit` was enabled. It now excludes the latched active Zombie mode,
   including the gap before a match object exists and explicit rewarm requests.
   This includes both solo equipment choices. Protected/finalizing PFT retains
   its earlier exclusion.

`ZombieKitWarmupPolicy` only decides these optional scopes. No config option,
profile edit, server change, bot cap increase or global loader change is added.
Other modes keep their existing scope, eight-second schedule and TDM readiness
deferral. The loading seam reads `ArmedMode`; the runtime seam reads `ActiveMode`.
Neither reads the mutable menu or requires a current mode object.

## Required work is unchanged

- `KitCatalog.ReserveBaseKit` reserves exactly the kit warmed for entry; there
  is no additional random draw or replacement selection.
- `LiveKitRespawn.SwapRoutine` still awaits `KitBundlePreloader.Preload(kit)`
  before touching the replacement pipeline (currently line 261). Loading-screen
  `Off`, failures, missed loading seams and additional required cold templates
  therefore still load when needed.
- `ZombieMode` records the kit through `OnKitApplied` and passes that remembered
  definition to `ZombieLoadoutRefill.TryRefill` (currently lines 441–447).
  `TryRefill` temporarily supplies it to `KitCatalog.KitProvider`, and restores
  the previous provider in `finally`. `LiveKitRespawn.AtRoundBreak` captures the
  kit synchronously before starting its coroutine (currently line 113). The
  refill cannot depend on a later random catalogue entry.
- Own-gear refills retain their existing entry-loadout restoration; native
  entry gear has already been loaded by the game. PFT wave restoration does not
  enter the solo refill path.
- The template cache, loading-screen adopt/discard epoch/world/token checks,
  optic guard, required preload retry and native pool ownership are unchanged.
  `ObjectsFactory` still owns pool lifetime and native asynchronous work; no
  attempt is made to cancel native jobs already dispatched.

The relevant native source is `../_decompiled/EFT/ObjectsFactory.cs`, especially
`LoadBundlesAndCreatePools` and its raid pool/cancellation ownership. There is
no change to that native function or to shared pool settings.

## Review passes and verification

1. Implementation: audited both optional entry points rather than suppressing
   the shared required preload function.
2. Domain reread: followed opening reservation, active/armed session latching,
   the current-kit event, refill provider capture and native pool ownership.
3. Defect hunt: exercised ordinary/PFT exclusions, mutable-vs-frozen mode drift,
   rewarm/provider requests, an in-flight adopted opening, failed required work
   and stale arm epochs; compiled faults reintroduce these failure modes.
4. Polish: kept the production change narrow, documented one-life assumptions
   beside both seams and left all required preload callers unchanged.

`tools/ZombieKitWarmupChecks/Verify.ps1` compiles the actual controller, both
preloader partials, scope policy and preload plan against controlled host
boundaries. It checks both equipment choices at all loading scopes, unchanged
other-mode behavior, required load/retry/cache behavior, and ten compiled
negative controls. Compiler failures do not count as detected faults. The
runner only writes ignored artifacts, never production copies or installed DLLs.

The existing TDM warmup harness needs the new policy linked because it compiles
the production controller; its scheduling assertions remain valid. Separate
Zombie solo/team checks cover their one-life and refill behavior. Parent
integration owns that fixture, the staged mod build, independent review,
deployment and GPU comparison.

This removes known unused asset/pool work. It does not establish a steady-state
FPS improvement or a safe higher zombie count. Native evidence must distinguish
the opening/optional-load phase from already-warm gameplay and verify opening
weapon use plus intermission refill. No native acceptance is claimed here.
