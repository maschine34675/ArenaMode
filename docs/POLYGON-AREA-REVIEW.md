# Polygon areas and scene capture

2026-09-07, `feature/polygon-arena-areas`, based on `e61b6e3`.
The user explicitly chose capture help first because no Customs vertices exist
yet. This change implements polygon support and a small drafting tool; **it does
not change either existing preset's ID, contour, height band or fixed 2v2 rule**.
Factory South remains a test area. No CTF, new team sizes, spawn viewer, runtime
polygon import or normal-raid editor is included. Waypoints remains the spawn
visualization tool. The previous native area menu received the user's broad
"ja passt" acceptance, not individual confirmation of every PFT/menu test case.

## Capture controls

Follow-up based on `83a26cf`, branch `fix/pmc-area-capture`: the user found that
Arena-only capture cannot be used to walk a new contour outside the old boundary.
An explicit normal-PMC survey option fixes that workflow. This is a client-only
change; restart the game. The server needs no update for this follow-up.

To walk an unrestricted Customs outline, start a **normal PMC raid**, not Arena.
In F12 -> ArenaMode -> `Development - Area outline`, set BOTH `Enabled` and
`AllowInNormalPmc` to true. Both default off; the second switch is new. The tool
only measures and exports: ordinary raid rules, equipment losses and profile
consequences still apply. It grants no invulnerability or Arena respawn/profile
protection. Disable the helper when not surveying. Scav and Hideout are excluded.

Arena capture still works with just `Enabled`, but its current physical boundary
stays enabled. There is no boundary bypass or playable whole-map Customs Arena.

| Default shortcut (left Ctrl) | Action |
| --- | --- |
| Ctrl+F6 | Record the map surface at the screen center, up to 250m away |
| Ctrl+F10 | Record the current player's foot position |
| Ctrl+F7 | Remove the last recorded point |
| Ctrl+F8 | Validate and export a new JSON draft |

Shortcuts can be changed in F12. Follow the desired perimeter in order, clockwise
or counterclockwise, including the corners of each recess. Use 3 to 64 distinct
XZ points. Do not repeat the first point: the final edge closes automatically.
Crossing or touching non-adjacent edges, duplicate points and zero-area shapes
are rejected at export. Undo can repair a temporarily invalid outline.

The numbered preview shows the outline, floor and ceiling; green means valid
draft geometry, orange means incomplete/invalid. It intentionally remains visible
through walls and is not the actual playable boundary. The capture ray ignores
only owned Arena barrier colliders and player/bot bodies, without disabling any
barrier. Ordinary map surfaces still stop it. This allows measuring visible
positions beyond the current border without opening that border.

F12 also provides `FloorY`, `CeilingY`, `SpawnMinY` and `SpawnMaxY`.
These are absolute world heights. Defaults 0 / 12 / 0.5 / 3 match the current
Customs candidate. Captured points retain their measured XYZ, but only XZ forms
the outline; the explicit floor/ceiling and separate spawn-foot band form the
prism. They must be checked for each scene/floor. Holes, multiple disconnected
areas and sloped/differently elevated floors within one definition are not yet
supported. A geometrically valid draft is not proof of sufficient spawns, live
connectivity, unobstructed terrain or runtime enclosure budget acceptance.

Capture/export is suspended while dead, respawning, spectating, in menus or with
an unlocked cursor. It requires the exact game's current own local player body.
Points survive an Arena respawn in that same raid. Disabling capture, leaving
that raid or changing the exact game/world, map or Arena/ordinary context clears
them. Switching off `AllowInNormalPmc` also clears a normal-raid draft. **Export before leaving or
disabling it.** There is no automatic save or import.

Each export is a separate complete file under the plugin's adjacent
`ArenaMode/AreaDrafts` directory. In the mirrored GPU installation this is:

`C:\SPT41\BepInEx\plugins\ArenaMode\AreaDrafts\area-draft-*.json`

The log prints the full path. On this development installation the corresponding
path starts with `D:\SPT41`. Exports carry measured points, normalized outline,
height settings, source map/area, a geometry SHA-256 and a proposed new preset
ID. They explicitly say `draftOnly: true`; the mod does not load them. Send the
chosen JSON back for review, spawn/connectivity checks and publication of the
same new versioned preset in both halves. Do not overwrite a v1 preset's
geometry under its old ID. No existing captures are overwritten.

Normal-PMC drafts report `sourceAreaId: full-map`, even if a bounded Arena area is
saved in the menu. This describes where points were captured, not an active
whole-map Customs preset. The normal predicate requires a started `LocalGame`,
matching singleton/game world and map, PMC raid settings, and its own living
USEC/BEAR body. `_raidSettings.serverId` must be present and unmarked, or a
strictly valid ordinary-start correlation ID. Reserved malformed/Arena markers
remain rejected after Arena's active flag clears; an active mismatched Arena
can never fall through to normal capture. No Session flags or native settings
are written. Native evidence: `TarkovApplication.cs:2993`,
`BaseLocalGame.cs:442`, `LocalGameAccessor.GetLocalRaidSettings`.

## Geometry and enclosure

`ArenaAreaBounds` accepts a finite simple polygon extruded between two heights.
It defensively copies its contour, normalizes winding and triangulates concave
caps without a fan or convex hull. Extents accelerate rejection, not membership.
The old six-float rectangle constructor retains its inclusive comparisons and
convex path fast path. Server per-raid spawn filtering and client loaded-spawn,
destination, teleport and complete-path checks use the same linked definition.

Polygon paths test every segment, including origin to the first NavMesh corner.
Positive inset tests the whole segment's distance to all boundary segments;
zero inset allows boundary contact but rejects excursions across recesses,
including narrow ones. Geometry arithmetic uses double predicates over float
coordinates; it is not arbitrary-precision computational geometry.

The Unity enclosure derives rotated edge walls, corner closures, wall grid and
closed floor/ceiling meshes from that contour. Caps remain non-convex static
colliders. Cap grid strips are clipped to actual cap triangles. Axis-aligned
four-corner rectangles keep the previous external wall placement; other
polygons use 0.6m centered walls with 0.3m corner closures, inside the excluded
0.4m navigation clearance. Spawn clearance remains 0.75m. Existing collision
layers/matrix are checked, never globally modified.

Two conservative box-shaped cap **navigation carvers** retain vertical shortcut
protection below the floor and above the ceiling. Their bounding footprints may
include a recess only outside the permitted height; they are neither physical
caps nor visible geometry. Edge/corner probes check delayed carving before
release. These sampled probes do not prove every possible crossing is carved;
physical walls and complete path guards are separate protections. Projectile,
grenade and AI sight blocking is still not promised.

Work has explicit coordinate, extent, perimeter, vertex, grid and probe budgets.
Construction/periodic integrity checks cover owned objects, transforms, collider
and obstacle configuration, mesh references and visuals. The exact raid owns
the enclosure, including partially built assets. Teardown disables its root
synchronously, then disposes only owned meshes/material before deferred Unity
destruction. Session tokens, server snapshot/profile protection, normal-raid
behavior and native menu/respawn selection rules are unchanged.

## Verification

Run `tools/PolygonAreaChecks/Verify.ps1` with Geometry, Enclosure, Integration and
Build stages, serially. Pass `-SptRoot C:\SPT41` on the GPU host. Earlier stages
use standalone client models and a deliberately nonexistent server deployment
target. **Build deploys** the solution, runs the full database-backed suite and
compares all four final Release DLLs with the local installed files.

PMC follow-up verification (2026-09-07): the production-linked controller suite
passes 292 assertions, including capture/export outside the current Customs
boundary in normal USEC/BEAR raids, off/default behavior, both Scav checks,
world/map/body and active/ending Arena refusal. Nine deliberate polygon/capture
mutants fail named assertions, followed by original-source rechecks. The 101
draft/export assertions also pass. Independent review found no open P0-P2.

`PmcCaptureVerify.ps1 -Stage Scope` reproduces those checks. `-Stage Build`
compiles the actual client against installed game references while automatic
deployment targets a fresh isolated staging directory. It verifies the installed
client/core stayed unchanged and that the staged and built client hashes match.
Staged client SHA-256:
`316954F9B9E82ECA4441B587B20093EF2FF88094E9CF590FD4CFE587C40D08E7`.
Delivery copies only this client DLL after game-state coordination. Server,
companion and Redeploy code are unchanged. Actual normal-PMC GPU use still needs
the user's test; no autonomous raid was run. The full server/data suite below is
historical polygon evidence, not a new run for this client-only follow-up.

Historical polygon implementation verification (`83a26cf`, 2026-09-07):

- 363 server/data tests passed with the installed database, no skips; the focused
  geometry/Customs group contains 46 cases, including 33 added polygon cases.
- Draft/export model: 101 assertions. Actual capture controller under modeled
  host contracts: 213 assertions. Actual enclosure plan/builder under modeled
  Unity contracts: 65,998 geometric sample/component assertions.
- Integration: 86 server area cases; native menu 324, area hooks 63, map view 60,
  mode selection 130, spawn policy 288, TDM runtime 209, give-up 385 and area
  respawn contracts 13 assertions.
- Fourteen deliberate mutations fail their named behavior assertions: seven
  enclosure, four polygon/capture and three existing area/respawn controls.
  Original-source rechecks follow the mutations; they never edit installed code.
- Independent adversarial review has no unresolved P0-P2 findings. It closed
  exact local-body/world capture bindings, preserved the original Default grid
  render layer, and corrected a test double that had assumed an extra session
  check. The new world guard is in production and has its own removal mutant.
  Partial/reparented enclosure children retain teardown ownership.

Final Release build and installed-file comparison passed locally. Dated SHA-256
snapshot (all four built/installed pairs equal):

| DLL | SHA-256 |
| --- | --- |
| ArenaMode.Client | `F36D63D6A9D249DC3D5130B3B22D6EB6D465E42660DA461CA749E18DC2029031` |
| ArenaMode.BigBrainCompat | `0AC1D4487F025CA5D94181F79B3F310429C831F802BE8F04402DD97E532952EA` |
| ArenaMode.Server | `B40733AEE18E541ADE051CDC23DF6956E65D87FE7579EC50921CDA5D55771F07` |
| Redeploy.Core | `7D950A475A1175FF3C7D727D0BA49000F69B74EAD59602B9058F7E803E1A2BCB` |

The local Syncthing API reported SPT-GPU connected/unpaused, the SPT41 folder
idle with no local errors/needed items and remote completion 100% with no needed
bytes/items after deployment. This is synchronization evidence, not an
independent remote DLL hash comparison or GPU runtime test.

Standalone Unity doubles test production layout, object ownership and control
flow; they do not simulate Unity physics, NavMesh carving, render appearance or
actual keyboard/game interaction. No autonomous raid was run.

## User GPU handoff

1. Start normal PMC Customs; enable both capture switches. Walk outside the old
   Factory Center contour, capture aim/feet points in order, undo and export.
   The panel must identify ordinary raid/profile consequences. Use Waypoints for
   native spawns. Default/off, Scav and Hideout must not admit capture.
   In an Arena match, capture must still leave the existing border impassable.
2. Check that menu, death, killcam and observer input does not add/export points;
   the draft should remain after respawn but disappear when disabled or after
   leaving the exact raid. Check return to ordinary/full-map raids for leftovers.
3. Send a chosen Customs JSON. Review its contour against buildings, elevations,
   all retained spawn positions and sufficient separated connected paths before
   adding a new shared preset. The existing 15 PMC/seven bot coordinate pool is
   not a promise that a narrower contour still supports a match.
4. Only after that preset exists, test concave walls/joins, sprint/jump/vault,
   floor/ceiling routes, bot navigation, repeated respawns and teardown in-game.
   No newly authored concave contour is selectable in this capture-first build.

The user confirmed game and server stopped before the original polygon deployment.
The normal-PMC follow-up is verified in isolated deployment staging first; actual
game-state coordination and installed hash comparison precede delivery. Live GPU
physics/UI and precise Customs fitting remain user-owned, unrun checks.
