# StatTrack compatibility checks

Run `pwsh -NoProfile -File tools/StatTrackCompatChecks/Verify.ps1` from the repo.
Optional parameters point to the independently downloaded StatTrack 2.1.1
client/server and installed `Assembly-CSharp.dll`; see the contract document.

The harness compiles the three production adapter files plus Protocol with
independent world/Harmony boundaries. Executable counter fixtures simulate a
request snapshot and delayed completion, rather than asserting source strings.
Published foreign binaries and the actual native constructor/lifecycle are
inspected with Mono.Cecil, not loaded for execution.

Each negative control compiles a separate production-source copy and must fail
its named assertion. Build failures do not count as successful faults. Evidence
and temporary copies stay under ignored `artifacts/stattrack-compat-checks`.
No build/deploy targets or game/server mutations are invoked.

See [ownership and persistence contract](../../docs/STATTRACK-COMPAT-20261019.md)
for native boundaries and intentionally unchanged insurance/menu behavior.
