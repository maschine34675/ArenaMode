# Domination map marker checks

Run `pwsh -NoProfile -File tools/DominationMapMarkerChecks/Verify.ps1`.
The complete production adapter is linked into a no-deploy executable. Only
external Unity, DynamicMaps, BepInEx and Harmony boundaries are doubled.
Checks cover absence/version refusal, partial patch rollback, retained inactive
views, coordinates, labels, faction/contested updates, floor opacity, late map
loads, unload/wrong-location selection, destroyed objects, terminal cleanup,
successor ownership and failures during both native removal and fallback hiding.
Seven deliberately broken source variants must fail their named behavioral check.
The final pass checks the installed DynamicMaps DLL's exact public signatures and
native removal bookkeeping. These checks do not prove native rendering.

Contract: [Domination map markers](../../docs/reviews/DOMINATION-MAP-MARKERS-20261101.md).
