# Domination map-menu performance boundary

Run `pwsh -NoProfile -File tools/DominationMenuPerformanceChecks/Verify.ps1`.

The harness links the production location hooks, menu admission, map snapshot,
map/area policies and existing conditional-view ownership. It reuses the native
callback-order and presentation doubles of `NativeAreaHookChecks`. Eight native
Arena rows are materialized, including the optional MapPort registry entry. The
eligibility provider repeats `CompatibilityRejection` as the real SideSelection
provider does; compatibility invocations are counted, not replaced with a fake
production cache.

It verifies bounded idle and selected-map work, separate strict and roster-only
eligibility, fresh click/claim/final freeze after an external refusal, mode-switch
visibility restoration, close cleanup and rejection of retired callbacks.

Since 2026-10-31 every refresh is one display pass (`ArenaMenuSelection.BeginDisplayPass`,
docs/reviews/MAP-MENU-ADMISSION-20261031.md): the harness pins exactly one optional-mod
admission per idle refresh (four before the pass), counted both at the provider and by the
DeveloperMode compat timer, and reads the probe's cumulative "reused" counters to prove that
rows and the selected-map repair still go through the snapshot instead of asking
ArenaMenuSelection again. A native click right after a pass evaluates admission fresh, and a
refusal arriving between two refreshes is shown by the next one. Seven compiled source-copy
mutations prove the cost limits, snapshot replacement, final freshness and the pass boundary
(a pass that never ends, verdicts that survive the pass, a refresh without a pass) can fail;
the older snapshot-survives-refresh mutation is now caught by the refusal-between-refreshes
check, before the Domination-to-TDM row check it used to reach. No production source is rewritten, no mod
build/deployment targets run, and the wrapper also supplies no-deploy properties.

This measures deterministic call counts, not actual frame time. Core/Harmony
adapter cost, Unity layout/rendering and native FPS must be measured on the GPU
host separately. The full existing native-hook and native-menu harnesses retain
their broader PFT/time-choice/controller/ordinary-raid checks.
