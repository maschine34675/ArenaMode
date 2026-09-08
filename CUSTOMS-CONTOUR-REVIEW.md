# Surveyed Customs contour

2026-09-07; based on `235b285`, implemented on `feature/customs-contour`.

## 2026-09-08 measured capsule-height follow-up

The next user test, with client `922d656`, identifies the exact failed predicate
at **07:04:53.977 local**: `Arena boundary corner 0 navigation capsule`, radius
0.3 as intended, but height **27.7499981 instead of 55.4999962**. This is exactly
half the assigned height, not the small floating-point rounding suspected in
the earlier follow-up. All physical colliders and the preceding edge-box
obstacles passed the same integrity traversal. All 28 scene markers were again
accepted. The server restored and persisted the profile at 05:04:55 UTC.

Keep physical corner capsules and all side-wall/grid geometry unchanged. Only
corner navigation obstacles now use explicit `Box` shape, zero center and
`size=(2*radius, full height, 2*radius)`, through the same construction and
readback checks as edge boxes. There is no guessed height multiplier and no
acceptance of a half-height obstruction. The installed EFT `NavMeshDoorLink`
also constructs carvers via `size`; Unity documents the
[Box/size API](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AI.NavMeshObstacle-size.html).
Unity's [2022.3 inspector source](https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/2022.3/Editor/Mono/Inspector/NavMeshObstacleInspector.cs)
uses half extents internally, but that alone does not establish the native
getter implementation in this game's custom Unity build.

The 0.6 m square cross-section contains the old radius-0.3 m circle completely.
Its diagonal extends 0.124264 m farther from the vertex (0.424264 m total),
or 0.024264 m beyond the existing 0.4 m path inset. This can conservatively
remove a little more walkable navigation at a tight corner; it cannot open
a new passage. Connected-spawn, carve-crossing and full-path validation remain
strict. No additional actor-height restriction, physical floor/ceiling,
spawn-policy change or server update is introduced.

The state double now reproduces the observed capsule height readback relation.
Against that fixture, the original `922d656` builder fails its actual integrity
check at the same component/property (13.2 -> 6.6 m in the small fixture).
The updated builder passes, including the surveyed 15-point contour. These
checks model the reported getter mismatch, not native carving or PhysX. They
check unchanged physical capsules, full-height navigation boxes containing the
capsule envelope, size/shape/deletion faults and retained roundoff diagnostics.
Additional negative controls deliberately halve corner-box height and width.

Only the client needs delivery and a game restart. The next user GPU test must
still confirm `Arena area READY`, movement and respawns; successful load is
not claimed by offline verification.

Final follow-up checks: **69,023 enclosure assertions**, all **13 deliberate
fault controls**, and an isolated Release client build pass. Independent
adversarial review has no unresolved P0-P2 finding. The updated client was
installed locally on 2026-09-08 with build/staging/installed SHA256
`20D76AA252C3B2939DC17B6FC4E7A2A3B21C614F324D8EBC7512C7B640714167`.
The server remains unchanged. Prepared client, previous-DLL backup, manifest,
log excerpts and original-builder reproduction are retained in ignored
`artifacts/corner-carvers/prepared` in the shared checkout. GPU synchronization
and native raid behavior were not independently checked here.

## Previous 2026-09-08 loading follow-up (`922d656`)

The user GPU attempt with the original contour client failed at 06:45:16.077
local time. The scene accepted all 28 candidate markers and constructed side
walls covering Y -21.1..33.2, but `ArenaAreaEnclosure.Intact()` did not pass within
the 12-second preparation deadline. No bots were released. Server logs (UTC)
confirm successful arming and subsequent profile restoration; this was a client
boundary-integrity rejection before connected navigation was examined.

The old aggregate message "a boundary collider or navigation obstacle is missing"
does not identify which predicate failed. Polygon corners are the first live
use of capsule colliders/carvers; previously tested rectangles have none. Their
radius/height were checked using exact float equality, unlike vector dimensions.
Installed Unity APIs use native getters/setters, whose actual readback values
were not logged. Rounding is a candidate cause, not an established diagnosis.

That first client follow-up allowed an absolute 0.0001 m dimension deviation for capsule
radius/height, still against planned geometry. Nonfinite values and larger
deviations remain invalid. All component, transform, collision and navigation
guards remain enabled. Preparation and active-match failures now report the
first failed object/property, including round-trip float values for geometry,
instead of describing every failure as missing components. Successful checks
do not format diagnostic strings.

Regression coverage injects both signs of small and excessive readback changes
and NaN/infinity into both dimensions of both capsule types. These limited state
doubles exercise the production validator, not Unity's native implementation.
The same suite retains deletion, disabled-object, transform, mesh, carving and
cleanup checks, plus deliberate exact-equality and disabled-dimension-guard
mutants. Runtime diagnostic forwarding is source-checked and client-compiled;
native execution and successful contour loading still require the user test.

Only the client changes for this follow-up; restart the game before retesting.
The server and the contour/spawn rules remain unchanged. If loading still fails,
the next log should distinguish the precise integrity failure from subsequent
navigation readiness failures.

Final follow-up verification: the planner/builder suite passes 67,002 assertions
and all 11 deliberate faults fail their named behavioral assertions. The Release
client compiles against the installed Unity/EFT assemblies into isolated staging;
installed client/Core stay unchanged during verification. Independent adversarial
review has no unresolved P0-P2 finding. These results do not prove the suspected
native rounding cause or successful in-raid loading.

That first follow-up client was installed locally on 2026-09-08 with matching SHA256
`326405FD7021EC2E9A7E60075EE70198851443370307549DA1FBA6D7754C891E`.
Prepared DLL, backup and deployment manifest are retained in ignored
`artifacts/contour-integrity/prepared` in the shared checkout. The server retains
the original hash below. Remote synchronization and the next GPU raid have not
been verified in this follow-up.

## Selection and scope

Choose **Arena -> Team Deathmatch -> Customs -> Factory Center (contour)**.
The new ID is `customs-factory-center-polygon-v1`. Both client and server must
know that exact ID; an old client/server cannot silently substitute a rectangle.
Existing Factory South and Customs Factory Center IDs, dimensions and saved
selection remain unchanged. The old Customs rectangle remains available and is
still the default for a first Customs selection. Select the contour explicitly.

This remains independent **2v2 Team Deathmatch**, without PitFireTeam. Other
modes, wrong maps, full-map Customs and incompatible arm replays stay rejected.
Ordinary raids, profile snapshot/recovery and exact raid ownership are unchanged.
Restart the game and SPT server after installing the two updated Arena DLLs.

## Geometry and spawn policy

The 15 X/Z vertices come unchanged from the user's survey exported at
2026-09-07 21:29:46 local time. The original file is preserved under
`tools/CustomsContourChecks/fixtures/area-draft-20260907-192946.json` and is never
loaded as configuration. Its original geometry hash is
`ff45e11f287393f189229d07e8034d7c5cb229cca81b96ce9ec782a9de990967`.
That hash describes the source draft, including its old height fields; it is
not a fingerprint of the newly published height-unlimited rules.

The contour spans about 195.1 by 143.7 metres and 21,815.9 square metres. There
is **no actor, path or spawn height limit** for this new preset. Its old draft
height fields are not enforced. NaN/infinite coordinates remain invalid.
Complete path segments must stay inside the horizontal polygon, including at
reflex corners, rather than merely keeping the path's endpoints inside it.

Spawn points retain the 0.75 m edge clearance. Instead of filtering by height,
both client and server explicitly exclude native sniper marker
`6e771bc5-ce82-4ab8-88e2-d50dedf4e34e` (`ZoneSnipeFactory`, Y about 14.75 m).
The installed Customs database then supplies **17 PMC points and 11 bot points**
in `ZoneFactoryCenter`. All starts have the required separated respawn and AI
choices in coordinate checks. Three extra PMC markers are too close to the
western boundary (0.12–0.61 m) and remain excluded. Counts describe candidate
positions, not simultaneously spawned bots or confirmed navigable points.

## Side walls, navigation and cleanup

The new preset creates only the side grid, edge boxes and corner capsules.
There are no physical floors/ceilings, cap grid ribbons or vertical cap carvers.
Unity's finite side-wall dimensions cover the vertical span of loaded NavMesh
triangles whose X/Z bounding boxes overlap the contour's bounding box plus a
2 m probing margin. All vertices of an overlapping triangle contribute, even
if none are inside the area. Allowed spawn anchors and the actual player are
also included, then side walls receive 16 m padding above and below. This finite
wall coverage does not become an allowed-height interval for gameplay.

Existing enclosure work budgets stay enforced, including a maximum 256 m side
wall span; unsupported scene geometry aborts preparation instead of clipping
walkable floors or releasing an unverified match. The actual navigation surface
is sampled only once to size the walls. The legacy finite-height presets retain
their original floor/ceiling and construction path.

Normal readiness, full-path containment, body recovery, exact raid ownership,
component integrity and cleanup remain independent protections. The existing
crossing probes run at the initial player's height; they are not a proof of
carving at every floor. Physical walls and whole-path checks cover the derived
height span, but native PhysX/carving results still require an in-game test.

## Verification

`tools/CustomsContourChecks/Verify.ps1` offers Policy, Runtime, Integration and
Build stages. Server tests override automatic deployment with a nonexistent
destination. Build writes client/server outputs to a fresh staging directory;
its only junctions provide Managed and SPT dependency references. It compares
build/staging hashes and checks that installed client/server/Redeploy DLLs were
not changed. No live raid is launched by these checks.

Original contour (`4a1d0f4`) offline verification: **386/386 tests, no skips**, including 23 focused
contour cases and the installed database. Native menu checks passed 340
assertions. The actual enclosure planner/builder passed 66,836 assertions with
limited Unity state doubles, and nine deliberate faults failed their named
assertions. Two separate server/compiler-input faults (height limit restored,
sniper ID omitted) also failed behavioral assertions, then an explicit original
source rebuild and 23/23 passing tests verified cleanup of mutant outputs.
Area respawn, native map/mode/hook, capture-scope, TDM and surrender regressions
passed. These checks do not execute Unity/PhysX or constitute a raid test.

Independent adversarial review found no unresolved P0–P2 issue in geometry,
server/client exclusions, native selection, ownership, staging or fault-control
restoration. The native Unity API was inspected in the installed
`UnityEngine.AIModule.dll`: `NavMesh.CalculateTriangulation()` returns the
vertices/indices consumed by the pure enclosure planner.

Original contour (`4a1d0f4`) Release build/staging SHA256 (client superseded above):

| Artifact | SHA256 |
|---|---|
| Client | `EDCC4B486D80EB7E36A48BEC60AA646DA5615124465B58BF002D21918B0CB9AB` |
| Server | `B73791EE2E561D6791DA4B4FF8F17107797DA536B0B132074081843F47538E5F` |

Before the integrity follow-up, on 2026-09-08 both original DLLs were installed into the local D:\SPT41 client
and server plugin directories; their hashes match the table above. No other
plugin or default configuration file was replaced. Prepared files and their
deployment manifest remain under ignored `artifacts/customs-contour/prepared`
in the shared checkout. This proves local installation, not a loaded GPU raid.
The user removed the routine stop-confirmation prerequisite for delivery;
their mirror handles files in use. See the current delivery policy in HANDOFF.md.

## User GPU checks

1. Select the new contour explicitly; confirm the old rectangle is still a
   separate choice. Start independent 2v2 and look for `Arena area READY`.
2. Walk the surveyed edge, especially points 1–4 and 12–15. Confirm the grid
   follows the contour and the three near-edge PMC markers are not used.
3. Try any accessible higher/lower paths inside the contour: no artificial
   floor, ceiling or height-based teleport should occur. Side boundaries must
   still block passage, including on higher paths.
4. Check repeated player/bot respawns, no sniper rooftop spawn, and normal bot
   movement. After leaving, a normal PMC raid must have no Arena enclosure.
