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

Colored tracers are team-only (2026-10-08), like the team laser colors: the one
`Effects / TeamTracers` entry belongs to Team DM and protected Tactical 5v5, and
`Settings.TracerEntry` returns null for Deathmatch, Gun Game, Zombie and solo
Tactical, where the renderer prefix keeps the complete native method whatever
TeamTracers says. The harness drives the real prefix per mode (the stand-in
`TracerEntry` mirrors that rule), checks that solo shooters are still classified
(killfeed names and friendly markers keep using `ArenaTracerTeams.Classify`), and
pins the shape of the real `TracerEntry` body and the absence of the retired
per-mode solo settings by source text. `Verify-Regression.ps1` adds two mutants:
a renderer that treats a missing entry as enabled, and a `TracerEntry` that also
maps Deathmatch to TeamTracers (the real Settings behaviour is additionally
executed by `tools/TdmMatchMenuChecks`).
