# Optional BigBrain loading checks

Run from the ArenaMode repository:

```powershell
dotnet run --project tools/BigBrainCompatibilityChecks -c Release
pwsh -NoProfile -File tools/BigBrainCompatibilityChecks/Verify-Library.ps1 -SptPath D:/SPT41
```

The harness compiles the production [library bootstrap](../../ArenaMode.BigBrainCompat/Library.cs)
and [typed registration boundary](../../ArenaMode.BigBrainCompat/BigBrainRuntime.cs)
against small BepInEx/Arena doubles and a **separate** BigBrain API assembly.
Each case runs in an isolated assembly load context. Cases that must stay
inactive explicitly refuse to resolve BigBrain and assert zero resolution
attempts, including during update and destruction.

The 15 scenarios cover:

- BigBrain absent, too old (1.4.9), not loaded, or without version metadata.
- Tested 1.5.0 and newer 1.5.1/2.0.0, with one version warning only for newer releases.
- An incompatible Arena version and a bridge already owned by another consumer.
- A genuinely missing `AddCustomLayer` method in a second BigBrain assembly:
  the resulting JIT-time `MissingMethodException` must be caught by the bootstrap.
- A registration exception after claiming the bridge, missing registration,
  incorrect layer type, incorrect priority, and missing brain membership.
- Successful initialization, throttled maintenance, teardown, complete capability
  rollback, and preserving a foreign consumer through failed initialization.

Repeated initialization/shutdown cannot duplicate layers or revoke another owner.
Three client-loader cases cover absent BigBrain, missing files and a loaded legacy
plugin. The verification script additionally builds real binaries without deployment
and reads Cecil metadata: only the main client registers a plugin, its BigBrain
dependency is soft, it has no hard AI assembly reference, and its three lifecycle
methods call the library owner.

Disk-loaded fixtures in fresh processes exercise the real reflection loader:
success, initialization refusal/exception, maintenance/cleanup exceptions,
wrong API, assembly version/name, and an old plugin-shaped DLL without the new API.
These tests use logger/API doubles, not game AI. The script also runs the pickup,
Molotov and team expansion regression harnesses and compares installed file hashes.

The loader dependency is checked as soft, and the typed entry method must retain
`NoInlining`. No movement logic, bot identities, path safety, or layer priorities
are replaced by these tests; their existing behavioral harnesses remain relevant.

The installed BepInEx chainloader was independently inspected: discovery reads
Cecil metadata and ignores static types as plugin entries. The main client's
Start runs after BepInEx assigns its plugin instance and BigBrain patches Awake.
See the [dated results](../../docs/BIGBRAIN-LIBRARY-20261018.md).

These are CoreCLR isolation and source-contract checks, **not native Unity/Mono
startup acceptance** or certification of a future BigBrain release. Future
versions are attempted with a warning and must still pass the real retained
layer/consumer checks; actual API failures leave support inactive. See the
[handoff](../../docs/HANDOFF.md) for release/package and native-test evidence.
