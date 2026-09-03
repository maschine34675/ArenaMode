# Arena leg-injury slowdown checks

Run from the ArenaMode repository:

```powershell
dotnet run --project tools/ArenaLegInjuryChecks -c Release
pwsh -NoProfile -File tools/ArenaLegInjuryChecks/Verify-Regression.ps1
```

The harness compiles the production leg-injury calculation and Harmony patch
against small stand-ins. It verifies the exact SPT 4.1 speed-limit overload,
the 50% one-leg and two-leg results, configuration boundaries, and the active
Arena/local-PMC gates.

Ordinary raids, AI actors, teammates, opponents, aiming limits and invalid
native values remain unchanged. Source contracts ensure that fractures,
destroyed body parts, sprint restrictions and sprint self-damage are not
suppressed by this feature, and that the old `NoFractures` binding and patch are
gone.

The mutation script removes the Arena gate and independently changes the
penalty-reduction formula in temporary source copies. Both mutants must fail
before the unchanged production sources are checked again. A real client test
is still needed to verify the resulting movement feel.
