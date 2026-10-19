# Classic Movement compatibility checks

`pwsh -NoProfile -File tools/ClassicMovementCompatChecks/Verify.ps1`

The harness links all three production adapter sources and the real lifecycle
marker protocol. It drives the actual foreign-prefix guards with independent
method-signature fixtures and simulated native/foreign state caches and speed
sentinels. Foreign source is not copied or executed. Each negative control compiles
a separate production-source mutation and must fail its specific assertion;
compilation failure is not accepted as a detected mutation.

Cases include ordinary PMC/Scav before/after Arena, exact initial local
construction before IBotGame exists, server-confirmed loading bots, stale/failed
load markers, retired/reused contexts and bodies, replacements, preview bodies,
conditional NoInertia receipts, legacy-mode rejection, forward metadata checks,
unexpected/lost/partial Harmony bindings and bounded optional diagnostics.

The separately downloaded 1.1.3 payload defaults to the ignored release artifact
path in Verify.ps1; override with `-PublishedAssembly`. Mono.Cecil verifies its
SHA256, legacy assembly version, plugin/config metadata and the two native hook
registrations without loading it. These checks prove the adapter decision
boundaries, not native Unity detouring, movement distances or animator behavior.
