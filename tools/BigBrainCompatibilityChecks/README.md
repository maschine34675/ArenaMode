# Optional BigBrain loading checks

Run from the ArenaMode repository:

```powershell
dotnet run --project tools/BigBrainCompatibilityChecks -c Release
```

The harness compiles the production [plugin bootstrap](../../ArenaMode.BigBrainCompat/Plugin.cs)
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

The loader dependency is checked as soft, and the typed entry method must retain
`NoInlining`. No movement logic, bot identities, path safety, or layer priorities
are replaced by these tests; their existing behavioral harnesses remain relevant.

On 2026-10-17 the final run passed **142 assertions**. The real companion also
built against the installed game/BigBrain API with zero warnings and errors,
using isolated output and overridden deployment targets. Nothing was deployed.
The installed BepInEx chainloader was independently inspected: discovery reads
Cecil metadata, then loads the named plugin type with `Assembly.GetType`; it does
not require loading every BigBrain-derived layer while the dependency is absent.

These are CoreCLR isolation and source-contract checks, **not native Unity/Mono
startup acceptance** or certification of a future BigBrain release. Future
versions are attempted with a warning and must still pass the real retained
layer/consumer checks; actual API failures leave support inactive. See the
[handoff](../../docs/HANDOFF.md) for release/package and native-test evidence.
