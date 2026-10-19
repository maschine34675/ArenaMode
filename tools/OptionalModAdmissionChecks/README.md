# Optional mod admission checks

Run `dotnet run --project tools/OptionalModAdmissionChecks -c Release` from the
repository root. This compiles the real `ArenaOptionalMods.cs` with controlled
plugin discovery, cached foreign configuration and Harmony-registry boundaries.
No game assemblies, network requests, installed configuration or deployment are used.

`pwsh -NoProfile -File tools/OptionalModAdmissionChecks/Verify.ps1` additionally
checks eight isolated production mutations: accepting ORBIT, ignoring the whole-mod
switch, ignoring installed gameplay patches, losing sticky activation, and invoking
the network getter, plus bypassing each of the three new optional adapters
(Looting Bots, Remove The Dead, Smoothie). Each must fail a behavioral assertion; compilation failure does
not count. Evidence stays in the ignored `artifacts/optional-mod-admission-checks/`.

Coverage: missing mods; known QB 1.1.0/1.1.0.0 startup disable; active and partial
QB switches; a network-capable `CurrentConfig` getter that must never execute;
already-installed gameplay patches of every Harmony kind; unrelated patches;
missing/throwing/unknown contracts and versions; sticky active-to-off transitions;
late ORBIT discovery; and exact rejection propagation/short-circuiting across the five optional adapters.

`ProductionAdmissionSource` can point to a temporary mutated production file for
negative controls. These checks establish admission behavior outside Unity, not
actual plugin loading or combined in-raid compatibility. `NativeMenuChecks` owns
the separate production menu/final-start and ordinary-raid integration checks.
