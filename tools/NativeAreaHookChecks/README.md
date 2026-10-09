# Native area hook checks

Run from the repository root:

```powershell
dotnet run --project tools/NativeAreaHookChecks/NativeAreaHookChecks.csproj -c Release
```

Success requires exit code zero and `NATIVE_AREA_HOOK_CHECKS_PASSED` with the
measured assertion count. See `NATIVE-AREA-MENU-REVIEW.md` for the coordinating
agent's execution and negative-control results.

This standalone project has no project references, installed DLL dependencies,
post-build copy targets, profile access or deployment steps. It links the actual
`ArenaLocationSelectionPatch`, `ArenaMenuSelection`, area/mode state policies,
conditional map view, map extras restoration, updater and shared map/area catalogs.
It does **not** reimplement their admission or repair decisions.

The host calls the production Harmony prefix/postfix methods by reflection. Its
native screen double preserves the relevant installed callback ordering:

- `OnLocationSelected`: null guard; time resolution; level resolution; native
  selected-value setter; then the deliberate `SelectedLocation.Id` dereference.
- `UpdateSelectedTime`: time/settings update; time resolution; direct setter.
- Group refresh: level resolution; direct setter, including the native null-map
  fallback to Ground Zero.
- `ShowInternal`: preselection, native row materialization, two subscriptions,
  then the production postfix. It does not silently dispose older subscriptions.
- `LocationButton.Show`: native toggle callback invokes selection only on true.
  `Select(false)` intentionally retains the native behavior of selecting true.

These orders were checked against the local decompiled
`EFT.UI.Matchmaker/MatchMakerSelectionLocationScreen.cs` lines 315-368, 398-470 and
512-583, plus `EFT.UI/LocationButton.cs` lines 70-114. Changes to these installed
contracts require separate native verification; this fixture is not that proof.

Cases exercise actual hook composition for direct Customs selection from
full-map, remembered Customs, a retired Factory South save to night via click and time event,
rejected area writes before the native non-null branch, incoming direct setters,
group refresh, F12/mode changes, configured population display, strict final
request matching, PFT mode repair before row use, ordinary PMC/Scav flows, stale
controller/generation callbacks and own-view cleanup without repeated native
Show/subscriptions.

Protected Zombie cases select Factory day/night, Labs and Labyrinth through the
production hooks and assert native Ready, Factory time controls, strict request
matching and selection retention across group/updater refreshes. They also check
Tactical/Zombie switching, the five-player squad summary, and rejection of other
PFT modes, bounded Customs and imported maps. The roster seam uses the production
mode catalog; it does not authenticate real companion identities. PFT admission
and the native four-companion roster need their own checks and GPU run.

External doubles represent BepInEx setting acceptance/rejection, cached server
map configuration, PFT eligibility, the two renderer endpoints and a small
Unity object tree. Their bounds are intentional: this is not real Unity,
Harmony detouring, shader/layout, BepInEx disk persistence, native group/player
level logic, Labyrinth metadata projection, HTTP, profile/arm lifecycle or GPU
raid verification. The real Settings writer is compiled with the client and
source-reviewed; disk persistence is not executed by these fixtures. Final
raid-start integration has separate linked-source checks in NativeMenuChecks.
Renderers record state and callbacks; their actual native visual implementation
is source-reviewed and still needs a GPU menu check.

For a negative control, replace only the linked production hook source:

```powershell
dotnet run --project tools/NativeAreaHookChecks/NativeAreaHookChecks.csproj -c Release `
  -p:ProductionLocationPatchSource=C:/absolute/path/ArenaLocationSelectionPatch.mutant.cs
```

Meaningful controls and their targeted assertions:

- Remove the `SelectionPatch` early area-write preflight: the rejected Customs
  write reaches the retained native dereference and fails
  `rejected Customs area write enters native null branch, not its later dereference`.
- Remove `SelectedValuePatch` repair: direct Customs assignment after Factory
  fails `direct setter repairs area from incoming Customs despite old Factory settings`.
- Restore strict `IsMapAvailable` in the Factory time availability predicate:
  South prevents night and fails
  `Factory South does not suppress the configured native night variant`.
- Restore discovery-based materialization in `DisplayPatch`: strict PFT
  eligibility with a wrong saved mode prevents rows from being created before
  mode repair, failing `PFT wrong saved mode must not prevent native map materialization before postfix mode repair`.
- Make a stale updater generation call shared `state.Restore()`: the new view
  is destroyed, failing `stale updater generation must not destroy newer screen views`.

Each mutation must fail at its named assertion with nonzero exit. A compiler
error or missing source is not an accepted negative result. Always rerun the
unmodified baseline after controls. No mutation script here changes production
files or runs automatically.
# Map expansion follow-up — 2026-10-09

The current fixture offers whole-map Customs in addition to its three bounded
TDM choices. Bay5 supports all current modes; Equator supports all except
Tactical because its middle cannot provide five bot markers beyond 25 m.
PFT Tactical/Bay5/Customs and Zombie/all three maps are exercised through native
selection, Ready and final-request validation. Missing optional-map plugins,
failed area writes and retired IDs still fail closed. Earlier descriptions of
Customs as bounded-only or ports as DM/Gun Game-only are historical.
