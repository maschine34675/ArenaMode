# Domination map marker checks

Run `pwsh -NoProfile -File tools/DominationMapMarkerChecks/Verify.ps1`.
Use `-AdditionalDynamicMapsPaths '<release DLL>;<other release DLL>'` (or
`ARENA_DYNAMICMAPS_CONTRACT_DLLS`) to check archived releases as well as the
installed DLL. Each DLL must retain its adjacent `Markers/dot.png`.
The complete production adapter is linked into a no-deploy executable. Only
external Unity, DynamicMaps, BepInEx and Harmony boundaries are doubled.
Checks cover absence/version refusal, partial patch rollback, retained inactive
views, coordinates, labels, faction/contested updates, floor opacity, late map
loads, unload/wrong-location selection, destroyed objects, terminal cleanup,
successor ownership and failures during both native removal and fallback hiding.
The full lifecycle runs against separate compiled 1.2.1 and 1.3.0 API fixtures:
the modern LoadMap has one parameter, private Image/Label getters and a protected
alpha table. Assertions use distinct observation aliases, not those bound names.
Both fixtures reject deliberate projection, lifetime, colour, marker-pack refresh
and API-selection faults. A third, mixed API fixture must be refused before any
hook or UI discovery; bypassing the accessor guard must fail that assertion.
Floor visibility observes the rendered image/label alpha, not the retained colour
value, matching native DynamicMaps. The final pass checks each requested DLL's
exact API shape, accessor visibility and native removal bookkeeping.
These checks do not prove native rendering or viewport transitions.

Contract: [Domination map markers](../../docs/reviews/DOMINATION-MAP-MARKERS-20261101.md).
