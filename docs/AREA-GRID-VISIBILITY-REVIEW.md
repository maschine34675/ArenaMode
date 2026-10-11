# Area grid proximity visibility

Snapshot: 2026-09-12. Client-only change; native GPU acceptance is pending.

## Behavior

The existing enclosure grid fades per polygon side according to the current
raid MainPlayer's horizontal distance to that finite segment. It keeps its
original cyan material and opacity within 6 m, fades linearly from 6 to 20 m,
and is transparent beyond 20 m. Near a corner, both adjacent sides can appear.
The whole side has the same alpha, so approaching the middle of a long side
does not depend on proximity to its endpoints. This is a warning for the local
body, not a spectator-camera effect. No settings or area data need migrating.

Customs' unlimited-height contour still has no floor/ceiling; vertical movement
does not change its side visibility. Older height-capped test presets additionally
fade their existing caps by vertical distance. Missing or invalid observations
restore full visibility as a safe fallback. A newly built mesh initially keeps
its existing visible colors until the active world's first update.

The runtime updates at most every 0.1 s. It reuses a managed color array and
uploads mesh colors only when an alpha changes. Geometry, indices, grid density,
material, mesh bounds and the vertex-budget calculation are unchanged. Added
plan metadata records the existing contiguous vertices belonging to each side;
it does not subdivide the mesh or add surfaces.

## Ownership and safety

Only vertex alpha changes. The enclosure root and renderer remain enabled even
when every vertex is transparent. Colliders, NavMesh obstacles, carving,
containment checks, spawn filtering and runtime integrity checks are untouched.
The disposed enclosure releases the color array with its existing resources.
`ArenaAreaRuntime.Tick` calls the update only after its active exact-world guard;
ordinary raids do not acquire an enclosure or visibility mutation.

The original mesh uses the game's `Sprites/Default` shader and a material tint
with alpha 0.42. Vertex white/255 therefore preserves the previous visual, while
vertex alpha zero supplies the transparent end of the fade. Offline doubles
cannot certify actual shader output, DLSS appearance or perceptual smoothness.

## Verification

Run `pwsh -NoProfile -File tools/AreaVisibilityChecks/Verify.ps1`.
This isolated console helper links the actual policy, enclosure plan/builder
and shared area data; it has no mod project references or deployment targets.
It passed 77 assertions for finite-segment distance, long-side midpoint,
diagonal/corner approaches, fade endpoints and monotonicity, invalid positions,
cadence, cap/height behavior, mesh color upload and disposal. A direct unlimited
scene-plan/build/fade case verifies the Customs-style no-cap behavior and height
independence without substituting a capped plan. Four compiled
defects are rejected: always visible, endpoint-only distance, missing native
color upload, and disabling the physical root. Actual runtime call-site scope
is additionally checked as source text, not executed as a native EFT lifecycle.

The existing polygon enclosure verifier also passes: 69,023 assertions and 13
compiled defects covering geometry, caps, carving, renderer integrity and
resource lifetime. These checks preserve enclosure contracts; they are not a
gameplay or NavMesh simulation. Independent adversarial review found no
established production defect and prompted the direct unlimited-scene test.
The integrated staged client build is recorded in the working handoff.

GPU acceptance must approach and retreat from straight/diagonal/corner sections
of the Customs contour, confirm invisible walls remain impassable, and check
team spawns/movement, vertical independence and normal-raid isolation. Existing
r3 files and test instructions remain a separate, unchanged test baseline.
