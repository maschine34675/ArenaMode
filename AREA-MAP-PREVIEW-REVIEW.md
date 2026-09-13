# Native area map preview

Prepared on 2026-09-13 on `feature/area-map-preview`; initially staged without
deployment during the user's ongoing tests. The user subsequently authorized
deployment: the clean `d7cde9d` client/server and map package are now installed
and hash-verified on D:. Native GPU acceptance remains untested. The exact test
binding is `GPU-AREA-MAP-PREVIEW-r1-BUILD.json`; see HANDOFF for delivery details.
The second contour was committed separately as `c7de967`.

## GPU r1 selection failure and correction

Report `588090f` tested the exact deployed `d7cde9d` pair on the GPU host:
all nine file hashes and four ProductVersions matched, with fresh processes.
The initial Center preview/layout was visible and unobscured at 1920x1080.
Factory East was offered but two native clicks could not select it. The tester
correctly stopped there: other contours, camera controls, lifecycle/resolution
checks and all raids remain untested. No Arena/Unity exception accompanied the
failure. Preserve `GPU-AREA-MAP-PREVIEW-TEST-RESULTS-r1.md` and its evidence as-is.

The cause was two missed settings admission sites: the handwritten whitelist in
`Settings.TrySelectArea` and the BepInEx `ExperimentalArea` acceptable-value list
both omitted East. A row could be offered by the shared catalog yet rejected
before the selected setting, UI or success log changed. The earlier native-menu
checks used a Settings stand-in; the preview checks exercised the proposed area,
not that real config write. Those tests did not cover this integration boundary.

The correction adds East to the BepInEx list and makes `TrySelectArea` consult
that same `Description.AcceptableValues.IsValid` constraint. F12, persisted
loading and menu admission therefore no longer maintain two separate whitelists.
Unknown/null IDs are rejected before mutation; the native owner, map/mode/PFT
guards, save/report behavior, shared server admission and profile boundaries are
unchanged. This is a client-only fix; the existing `d7cde9d` server already knows
the area and needs no rebuild.

`tools/AreaConfigChecks/` compiles the complete production Settings, area menu
state and catalog with the real installed BepInEx configuration implementation.
It exercises selection, saving and fresh-file loading against scratch config
files, including every offered ID. Its old-source control must reproduce the
original East rejection. It does not model the GUI, network or raid engine;
the native r2 retest still has to demonstrate a working click and preview.
Parent re-verification passes 165 assertions, both historical failure probes
and the fixed-source probes. Existing area/menu checks pass 406/73/24 assertions
and five source faults; the settings-linked TDM menu harness passes 92 assertions
and ten source faults. Independent production/oracle review has no open finding.

## Behavior

The Arena location screen now displays a Customs ground map beside the existing
area picker. AREA frames the selected contour with some surrounding context;
OVERVIEW shows its position on the whole map. The selected interior stays bright,
with a blue fill and boundary, while the outside is dimmed. Overview also draws
other available outlines in grey. The center contour and its team-start variant
share one outline and therefore are not drawn twice.

The image and boundary use the same projection. Geometry comes directly from
the selected `ArenaAreaCatalog` definition, including its actual triangulation;
there is no separately traced display polygon or runtime loading of AreaDrafts.
Choosing an area still happens in the existing list. Camera buttons do not save
settings, select a different area, arm a raid or contact the server.

Only Customs currently has artwork. Other maps and unsupported mode selections
retain the native map information. Ordinary PMC and Scav menus do not create the
Arena preview. No gameplay, profile, raid-start or network protocol was changed
for this presentation feature.

## Artwork, coordinates and distribution

`ArenaMode.Client/Assets/MapPreviews/` contains the reviewed PNG, unchanged source
SVG, unchanged supplied license, attribution and `MAP-SOURCES.json`. The ground
layer is from Shebuka / Tarkov.dev, distributed and modified by acidphantasm;
maschine's adaptation changes the palette and omits the sniper annotation.
It is **CC BY-NC-SA 4.0 artwork**, separately from the mod code license: preserve
attribution, noncommercial use and ShareAlike when redistributing. This static
pre-raid floorplan does not track entities. See the attribution for the upstream
use restrictions and exact source links.

The image covers world X -372..698 and Z -306..235, with top-left image UV
`u = (698 - x) / 1070`, `v = (z + 306) / 541`. Texture V is inverted once for
Unity. Calibration follows the actual ground-layer image transform; checks use
the four map corners and four named world positions independently of production
projection. The original viewBox is 1062.48 x 535.17; the PNG is 2125 x 1070.
Its approximately 0.034% aspect rounding is not a crop or an extra image offset.

The PNG, attribution, license and provenance are embedded together in the client
DLL. The normal build/package path also includes the complete loose map/source
directory. No DynamicMaps assembly, runtime installation or live service is
required. `tools/AreaMapDelivery/Verify-Package.ps1` verifies actual PE resource
bytes and the loose package, without loading the Unity-dependent assembly.

## Ownership and failure behavior

One screen generation owns its root, two camera buttons and decoded texture.
Periodic refresh reuses them; selection changes reset only the crop and mesh.
Resize recalculates the presentation. Both mesh layers are noninteractive
children of the same clipping viewport; line widths remain constant in UI units.

The preview hides native information through a CanvasGroup, preserving its
previous alpha and input flags. Native GameObject activation stays vanilla-owned.
Fallback restores the exact values. Close removes only an added, owned group,
removes listeners, disables/destroys the root and releases its texture. Same-frame
close/reopen and callbacks retained from a retired screen are covered. A decode
or presentation failure restores native information and warns once for that
screen generation; a fresh screen may retry. The area/mode picker remains usable.

## Second measured area

`customs-factory-east-polygon-v1`, displayed as **Factory East (contour)**,
preserves all eleven XZ vertices of the user's `20260913-090513` draft. There is
no height limit. It initially uses the existing fixed independent TDM **2v2**
rules, not the center preset's experimental adjustable team starts. Existing
area IDs and modes keep their rules.

The database contains 20 PMC candidate positions usable by either faction and
15 Bot/BotPmc candidates inside this contour. Those are geometric candidates,
not proof of native scene navigation. Existing complete in-area path readiness,
blocking and enclosure checks still apply. See `tools/CustomsEastAreaChecks/`
for the original draft, hashes, exact geometry and admission evidence.

## Verification and remaining native acceptance

- Area harness: 2367 assertions and four separately compiled source faults.
- Preview harness: 1986 assertions and five separately compiled source faults,
  with a final clean rerun and unchanged bound source/PNG hashes.
- Menu integration: 406 selection/rule, 73 native-hook and 24 area-view
  assertions, plus five source faults, including repeated preview creation.
- Asset verifier: full PNG decode, source/license/output hashes, dimensions,
  calibration metadata and six deliberate tamper controls.
- Existing unit suite: 617 passed, two existing optional-fixture tests skipped.
- Staged client and server builds: no errors. Existing nullable/obsolete API
  warnings remain; no claim of a warning-free mod build.
- Independent adversarial review covers the preview, catalog/menu integration,
  lifecycle, resource packaging and no-deploy staging.

Offline checks execute production geometry, mesh generation and controller
code against external Unity/UI doubles. They do **not** prove native clipping,
font appearance, landmark alignment or usable scene routes. The first menu GPU
check is described in `GPU-AREA-MAP-PREVIEW-TEST-INSTRUCTIONS-r1.md`; the new area's
actual raid/navigation acceptance remains the user's test.

For a paired build without deployment, run:

```powershell
pwsh -NoProfile -File tools/AreaMapDelivery/Build-Staged.ps1 -SptRoot D:\SPT41
```

The helper records client/server hashes, source commit and working-tree state in
an ignored `artifacts/area-map-delivery/<run-id>/manifest.json`, checks installed
DLL/asset hashes before and after, and leaves deployment false. The final clean
manifest identifies the staged candidate; a staging run alone does not install
it. The separately recorded deployment above promoted both DLLs, required for
the new shared area ID. Do not rebuild during the bound GPU menu test.
