# Arena team-tracer checks

Offline regression harness for the Arena-only native tracer replacement, exact
protected-5v5 roster classification, solo identity fallback, palette, geometry
guards and fail-open behavior. It compiles the production files against small
SPT stand-ins and rejects mutations that leak into ordinary raids, guess an
unknown 5v5 shooter, stop forcing tracers for normally tracerless ammunition,
or omit any of the three match/start-recovery cleanup paths.

```powershell
dotnet run --project tools/ArenaTracerChecks -c Release
pwsh -NoProfile -File tools/ArenaTracerChecks/Verify-Regression.ps1
```
