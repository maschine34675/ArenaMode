# Polygon enclosure review

This harness exercises the actual client enclosure plan and builder, plus their
narrow runtime wiring. The subsequent Customs-contour change adds an opt-in
side-only construction path; see `../../../CUSTOMS-CONTOUR-REVIEW.md` for its
selection/spawn contract and current verification. Legacy finite-height presets
still use the construction described below; their original milestone evidence
at the end of this file is historical.

## Physical and navigation contract

- The shared immutable CCW outline supplies one rotated wall per actual edge.
  Four-edge axis-aligned rectangles preserve the previous outside-only wall
  volumes and all six carving volumes. Their physical floor/ceiling are trimmed
  to the playable footprint; the overlapping walls close the edge of each cap.
- Other polygons use 0.6 m thick centered edge boxes, 0.05 m endpoint extensions,
  and 0.3 m vertical corner capsules. This closes sharp, oblique and reflex joints.
  The largest possible geometric intrusion at an extended endpoint is about
  0.3042 m, below the unchanged 0.4 m navigation inset. Native nav-agent-radius
  expansion can make the usable component smaller; connected-spawn readiness
  must still pass in the actual scene. This is not a claim about every agent size.
- Floor and ceiling are closed, static, nonconvex MeshCollider prisms built from
  the polygon triangulation. Convex cooking would incorrectly fill concave voids
  and is forbidden. No rigidbody is added. Collinear outline points may be unused
  by cap triangles; geometric T-junctions with the side mesh are supported without
  assuming exactly n-2 triangles or inflating the cap to the bounding box.
- Two additional box obstacles preserve the old vertical carving guard below
  MinY and above MaxY. Their XZ extents include the bounding box and can therefore
  carve beneath/above a concave void, but they do not enter the allowed height
  interval. They have no collider or renderer. Scene testing must verify the
  native carving result, especially walkable floors close to those boundaries.
- Crossing probes follow every edge at at most 4 m spacing, including endpoints
  and an extra corner bisector. Runtime tests the intended and sampled points
  are on opposite sides. Missing outside navmesh or an unusable sharp-corner
  pair is skipped; these probes are evidence of delayed carving, not an exhaustive
  topology proof. Existing full accepted-path checks remain independent.
- Side grid ribbons follow the edges. Horizontal cap ribbons are clipped against
  the triangulated polygon, so even their finite width cannot fill a concave void.
  Reverse triangle faces are explicit. Only three meshes and one material are owned.
  The visible grid retains its previous Default (0) camera layer; physical
  colliders retain LowPolyCollider. Integrity detects an altered visual layer.

## Bounded work and cleanup

Before runtime readiness, layout rejects extents over 2048 m X/Z, height over
256 m, absolute coordinates over 100000 m, perimeter over 8192 m, more than
4096 probes, over 20000 cap triangle/strip clips, or over 60000 grid vertices.
The shared constructor independently permits at most 64 outline points. Vertex
indices remain below Unity's default 16-bit index limit. Refusals retain a concrete
reason and use the existing failed-start path; they do not release spawns.

The builder checks the existing Player/LowPolyCollider matrix without changing
it. Integrity covers root/child parent, position, rotation, scale, active state,
collider dimensions/layer/trigger/static state, cap mesh binding/bounds/count,
carver shape/size/enabled/carving state, and visible mesh/material bindings.
It is not a general detector for arbitrary same-count/same-bounds mesh rewrites.
Reset first disables the root synchronously, then releases owned assets. Every
created child is tracked separately, including partially built or externally
reparented children, so teardown cannot leave a detached wall or carver behind.

`OwnsCollider` only accepts descendants of the current owned root. It gives the
capture tool permission to ignore those exact barriers during an explicit ray
capture; it neither modifies geometry nor alters collision layers.

## Verification and limits

Run serially from the parent coordinator:

```powershell
powershell -NoProfile -File tools/PolygonAreaChecks/Enclosure/Verify.ps1
```

The script links the actual plan, actual builder, shared bounds and map catalog.
Its independent geometry oracle covers the two unchanged presets, a rectangle,
oblique/acute/reflex contours and an outline containing collinear points: wall
volumes/orientation/joins, inset clearance, edge stations/normals, cap area/winding/
volume/concave holes, strip clipping, reverse faces and work-budget refusals.
Actual builder checks use purpose-limited Unity state doubles to exercise moved,
resized, disabled, destroyed, reparented and dynamic components; mesh binding,
convexification, missing shader/collision compatibility and three injected native
mesh-setter failures; repeated disposal and detached-child cleanup.

Nine deliberate production-source mutations must return nonzero and the named
behavioral assertion: inward legacy walls, omitted corners, reversed cap winding,
unclipped strips, convex caps, ignored visual disablement and leaked detached
children, an unexpected floor in an unlimited area, and ignored scene heights.
A fresh production run follows the mutations. Mutant sources and logs
live only in a unique ignored artifacts/polygon-checks/enclosure directory.

The small runtime source checks only verify wiring of the independently executed
plan/builder, sampled-side validation, ownership and teardown order; they do not
pretend to execute the live runtime. The state doubles have no PhysX, mesh cooking,
navigation, rendering, scene scheduling, transform inheritance or raid simulation.
Real installed client compilation and the parent regression suite are separate.

Native contract inspection, without copying implementation: installed
UnityEngine.AIModule.dll `UnityEngine.AI.NavMeshObstacle` exposes shape/center/size,
capsule radius/height and stationary carving; UnityEngine.PhysicsModule.dll
`UnityEngine.MeshCollider` exposes convex/sharedMesh. Native
`D:/SPT41/Development/_decompiled/EFT/GrenadeCartridge.cs:77-82` demonstrates
CapsuleCollider radius, center, height and direction. Root's first real solution
compilation passed with zero errors. The leaf has executed no builds or tests.
Parent serial oracle passed 65,998 assertions and all seven named mutations on
2026-09-07. See `POLYGON-AREA-REVIEW.md` for integration and deployment evidence;
no GPU scene/raid proof is claimed.

Four self-review passes completed before production freeze: geometry/winding and
budget limits; actual builder/native API contract; exact runtime/lifecycle diff;
adversarial oracle and cleanup ownership. The last pass found and fixed detached
child cleanup. The subsequent independent review closed the Default visual-layer
regression risk; actual scene tests remain required.
