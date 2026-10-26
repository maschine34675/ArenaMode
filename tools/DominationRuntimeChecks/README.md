# Fixed Domination runtime checks

Run `pwsh -NoProfile -File tools/DominationRuntimeChecks/Verify.ps1`.
Current baseline: 311 assertions; all ten source mutants rejected by their
intended behavioral assertions, followed by a clean baseline (2026-10-26).
This small console harness links the production `DominationMapLayout`,
`DominationRuntime`, both objective policies, TDM phase policy, objective-pickup
spacing and original team-box catalogs. It has no mod project references or
deployment targets. Unity/NavMesh, pickup geometry, point views, physical-player
classification, bridge output and logging are explicit boundary doubles.

The independent expected table preserves the authored X point on Equator/Bay5,
individual A/B points on AirPit/Bowl and Factory A/B/C day/night. It checks exact
counts, coordinates and labels, rejects unknown maps and policy-count mismatch,
and verifies immutable layout views. The recorded geometry calls must disable
nearby repair on ports and retain Factory's original repair/network path.

Failed last-point floor, sample, height, forward/reverse false or partial paths
must allocate no markers and publish no pickup authority. After complete
preparation, all points reserve pickup space, live occupancy reaches the real
policy, and bridge snapshots/status labels contain the actual per-map points.
Dispose removes the markers and prevents later updates/placement authority.
A late view allocation failure is surfaced to the owning mode; explicit owner
Dispose releases earlier views without claiming that the constructor succeeded.

Port route tests first simulate disconnected closed booths, then connected gates,
and require both directions between both faction starts and every objective.
Failure on the last BEAR route must not cache success and must allow a later
retry. Factory must not invent booth checks. Bad start/end navigation samples,
height drift and forward/reverse false/partial paths are all rejected.

Ten compiled-source mutants target the legacy centroid, lost X label, port
relocation, count mismatch, missing return route, height drift, partial paths,
missing BEAR routes, prematurely cached success and post-disposal authority.
The runner requires each mutant's specific behavioral assertion and checks that
production files remain unchanged; its final baseline must pass again.

These tests validate the real runtime's decisions against controlled external
responses. They do not prove real floor/NavMesh connectivity, native collider
behavior, rendering or which physical bodies are eligible. Nor does manually
calling `VerifyTeamPaths` prove the parent invokes it after gate completion.
The current parent source was separately inspected: `TickMatchStart` waits for
`ArenaTeamBoxRuntime.TryOpenForCombat()`, then verifies objective routes before
advancing the live match. That caller and actual gates remain integration/native
acceptance, as do the original-level asset provenance and live point placement.
