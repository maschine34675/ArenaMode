# Continuous Load Ammo: replacement-safe controller ownership

Implementation targets the published **1.2.0** client, optional GUID
`com.ozen.continuousloadammo`. Newer versions attempt the same metadata, API and
actual Harmony receipt checks and emit a warning; an unsafe contract blocks only
Arena entry. Ordinary PMC/Scav raids, saved settings, magazine presets, quick-load
selection and the foreign user interface remain foreign-owned.

## Physical lifetime contract

The upstream registration postfix creates a new controller every time the local
body registers, while its disposal callback clears one static current-controller
field without identifying which instance disposed. Arena's live kit swaps and
respawns can register the replacement before retiring the previous body.

The adapter disposes an already owned controller **before** upstream registration
replaces that field, preserving its loading cancellation, preset teardown and UI
disposal. Duplicate registration of the same body/inventory does not create
another controller. A disposal frame records the exact invoking controller;
old callbacks cannot clear a successor, including a subsequent ordinary raid.
Owned controllers retire once. Native player registration/disposal is never skipped.

Ownership requires a physical local body, its current inventory and GameWorld's
exact MainPlayer, the native game/world pair and either active Arena identity or
the exact server-confirmed pending arm/end identity. Initial registration occurs
before BotsController/IBotGame setup, so the sole early exception requires
Running, still-missing native LocalPlayer and the same Profile object as the
game's construction profile. IDs, matching map alone or outgoing start markers
never grant ownership. Leases are weak-keyed by controllers, inventories and tasks.

## The two delayed-completion hazards

The foreign LoadingEndPatches broadcasts an identity-free event after any native
load/unload process completes. Merely unsubscribing an old controller is not
enough: its old task can still notify the replacement controller. Arena removes
only each owned controller's own completion subscription. A first-priority native
Start postfix captures the exact originating inventory/controller and returned
task, with a separate operation epoch so an older same-controller process cannot
complete after a newer process. Only that task bypasses the foreign broadcast; its completion instead calls
the existing controller's LoadingEnd if that same controller/body/world/inventory
is still live and no newer load is active. Ordinary tasks and event subscribers
are not suppressed or cleared.

Capture must run before the foreign postfix even when the returned task is
already complete. Installation and admission verify the real Harmony sorted
postfix list, including priority/before/after constraints; an unsafe ordering
rejects Arena. The native seam inventory includes HarmonyX IL manipulators.

The controller's normal starting animation, configurable speed limit, hands
behavior and loading implementation remain unchanged. Its 800-ms post-load restore
is replaced only for owned controllers with a versioned restore ticket. A new
start/end, disposal, replacement, another world, active load or active MultiSelect
serializer invalidates that restore. It cannot equip a weapon on a retired body
or remove the speed limit of a newer load. The existing immediate sprint release
remains on a valid current completion; a stale broadcast never reaches it.
Clearing and reinitializing the binding cannot revive an old pending ticket,
even if every physical object is still the same.

Owned disposal also explicitly removes the controller's two global subscriptions,
because upstream skips those removals if Unity already considers the old body
destroyed. No inventory, profile, task queue or foreign configuration is cleared.
No other mod's source or DLL is bundled.

## Contracts and primary sources

- [Published 1.2.0](https://github.com/ozen-m/SPT-ContinuousLoadAmmo/releases/tag/v1.2.0)
- [Pinned source](https://github.com/ozen-m/SPT-ContinuousLoadAmmo/tree/6337683e0088bad8e05c9c26e798f31e27318cff)
- [Registration](https://github.com/ozen-m/SPT-ContinuousLoadAmmo/blob/6337683e0088bad8e05c9c26e798f31e27318cff/ContinuousLoadAmmo/Patches/RegisterPlayerPatch.cs)
- [Controller](https://github.com/ozen-m/SPT-ContinuousLoadAmmo/blob/6337683e0088bad8e05c9c26e798f31e27318cff/ContinuousLoadAmmo/Controllers/LoadAmmoController.cs)

Published DLL SHA256:
`241B4F8FB5D5B99305854309B8C7E3E8486392354258971CBB0879C104430B7E`.
Published plugin and assembly versions are both 1.2.0. During the initial offline
review, the release was downloaded to ignored artifacts without installation.
Its metadata/IL were inspected with Mono.Cecil; foreign code was not executed
for discovery. The later temporary native installation is recorded below.

Native source checked: `../_decompiled/EFT/GameWorld.cs` RegisterPlayer sets
MainPlayer before its postfix; `../_decompiled/EFT/Player.cs` native load/unload
Start methods retain `_inventoryController`, return Task<IResult>, and their
outer inventory continuation clears the active process. Every actual foreign
completion hook must expose this same inventory-bearing process contract;
unknown implementations without it reject Arena rather than guessing ownership.

## Verification boundaries

Final leaf run: **81 assertions and 25 compiled negative controls passed**.
Evidence: `artifacts/continuous-load-ammo-checks/c5108ebb023741fcbe0c783a81ca3816/`.
The first mutation run exposed a missing old-controller callback assertion;
that test gap was corrected before the complete passing runs. It is not counted
as a successful run. The final suite also covers immediate completion, rejected
foreign-before-capture ordering, IL-manipulator drift and binding revocation.

`pwsh -NoProfile -File tools/ContinuousLoadAmmoCompatChecks/Verify.ps1` compiles all
three production files with independent instrumented native/foreign boundaries,
executes ordinary -> Arena replacements -> ordinary scenarios, validates published
DLL metadata, and compiles deliberately broken production variants. The fixtures
are not copies of the foreign inventory implementation. No source-string-only
checks establish runtime behavior; string replacements are used only to create
compiled negative controls.

The initial no-deploy client build passed with four existing CS8632 warnings.
Native Unity/Harmony acceptance remained separate at that point. Actual Harmony
postfix ordering, native asynchronous continuation order and foreign UI
destruction cannot be certified by desktop fixtures alone.

Four-pass review: (1) published/native signatures and initial registration timing;
(2) controller rebind, duplicate disposal, pending tasks and following ordinary
world; (3) global event fan-out, same-body newer load and MultiSelect; (4) exact
scope, weak ownership, failure admission and no-deploy/file ownership. Independent
reviews of the frozen source found no remaining blocker. The root rerun passed
the same 81 assertions and all 25 compiled controls:
`artifacts/continuous-load-ammo-checks/a9d7f9c8416346b8b66575b868de2ceb/`.

## Native followup — 2026-10-20

The [Factory results](GPU-OPTIONAL-COMPAT-20261020.md), Phase 2 and its followup,
cover published CLA 1.2.0 on ArenaMode `235cc69`: ordinary load/unload/cancel,
continuation with inventory closed, two confirmed pending-unload deaths with
physical replacement, and an ordinary unloading control after Arena. Actual
shots after replacement confirmed usable hands. Two later living round changes
also completed, with the new CLA owner directly checked after the first.

Neither living replacement was proven to overlap pending magazine work. The
800 ms newer-operation race, pending-work living kit swap, preset/MultiSelect
cancellation, quick-load hotkey and measured movement-limit reset are not covered
by these observations. No CLA exception was observed;
this is bounded native evidence, not certification of every timing or UI path.
Test settings and temporary-file cleanup are tracked in the combined report.

The later Factory PFT-Zombies probe kept CLA, Deminvincibility and SVM installed
with one companion. Four actual MP5 shots and a zombie kill cleared wave 1.
The living wave 1→2 refill changed the local body from ID 1 to 4, and CLA's
owner was inspected as ID 4. During wave 2, a native test-hook death entered
live ally spectating; after the companion cleared the wave, the restored local
body and CLA owner both had ID 7. The team was ready at 09:39:26, with entry
gear and 440/440 HP restored. The raid subsequently completed its protected end.

Evidence is retained as `pft-demi-direct-observations.json` and
`demi-secondchance-pft-before-recovery.log` under the combined report's ignored
artifact directory. No pending CLA process was established across either PFT
swap. This extends body-binding coverage to one-companion PFT Zombies, not
Tactical, all squad sizes, pending-load transitions or a killer replay.
