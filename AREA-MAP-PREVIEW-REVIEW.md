# Native area map preview

Prepared on 2026-09-13 on `feature/area-map-preview`. **Not deployed or tested in
the game**: the user's ongoing GPU tests retain their existing installed build.
The second contour was committed separately as `c7de967`.

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
manifest is the later test candidate; it is not the currently installed build.
Both client and server must eventually be updated for the new shared area ID.
