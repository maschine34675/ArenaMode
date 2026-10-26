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
visibility restoration, close cleanup and rejection of retired callbacks. Four
compiled source-copy mutations prove the cost limits, snapshot replacement and
final-freshness assertions can fail. No production source is rewritten, no mod
build/deployment targets run, and the wrapper also supplies no-deploy properties.

This measures deterministic call counts, not actual frame time. Core/Harmony
adapter cost, Unity layout/rendering and native FPS must be measured on the GPU
host separately. The full existing native-hook and native-menu harnesses retain
their broader PFT/time-choice/controller/ordinary-raid checks.
